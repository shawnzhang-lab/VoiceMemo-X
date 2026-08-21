using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using SherpaOnnx;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class LocalSpeakerRouterService
{
    public const bool ShadowModeEnabled = true;
    public const float ClusteringThreshold = 0.52f;
    public const float ClusterMergeThreshold = 0.45f;
    public const float SingleOutlierSafetyThreshold = 0.78f;
    public const double ClearSecondSpeakerSeconds = 0.8;
    private const int SampleRate = 16_000;
    private const int EmbeddingWindowSamples = SampleRate * 2;
    private const int EmbeddingStrideSamples = SampleRate;
    private const int SegmentationWindowSamples = SampleRate * 10;
    private const int SegmentationShiftSamples = SampleRate * 5;
    private const double SegmentationFrameSeconds = 270d / SampleRate;
    private const double OverlapSafetySeconds = 0.30;
    private static readonly WaveFormat AnalysisWaveFormat = new(16_000, 16, 1);
    private static readonly TimeSpan MaximumAnalyzedDuration = TimeSpan.FromMinutes(90);

    private static readonly string ModelDirectory = FindModelDirectory();
    private static string SegmentationModelPath => Path.Combine(
        ModelDirectory,
        "sherpa-onnx-pyannote-segmentation-3-0",
        "model.onnx");
    private static string EmbeddingModelPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("SPEAKER_ROUTER_MODEL_PATH");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? Path.GetFullPath(overridePath)
                : Path.Combine(ModelDirectory, "3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx");
        }
    }
    private static string VadModelPath => Path.Combine(ModelDirectory, "silero_vad.onnx");
    internal static string VadModelFilePath => VadModelPath;
    private static readonly Lazy<InferenceSession> OverlapSession = new(CreateOverlapSession);

    public bool ModelFilesAvailable =>
        File.Exists(EmbeddingModelPath) && File.Exists(VadModelPath) && File.Exists(SegmentationModelPath);

    public Task<SpeakerRoutingResult> AnalyzeAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => AnalyzeCore(mediaPath, cancellationToken), cancellationToken);
    }

    private SpeakerRoutingResult AnalyzeCore(string mediaPath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        SpeakerRoutingResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ModelFilesAvailable)
            {
                result = SafeFallback(
                    TimeSpan.Zero,
                    stopwatch.Elapsed,
                    "model_missing");
            }
            else
            {
                var decoded = DecodePcm16(mediaPath, cancellationToken);
                if (decoded.Duration > MaximumAnalyzedDuration)
                {
                    result = SafeFallback(
                        decoded.Duration,
                        stopwatch.Elapsed,
                        "duration_exceeds_90_minute_safety_limit");
                }
                else
                {
                    result = AnalyzeSamples(decoded.Samples, decoded.Duration, stopwatch, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("LocalSpeakerRouter", ex);
            result = SafeFallback(TimeSpan.Zero, stopwatch.Elapsed, $"analysis_error:{ex.GetType().Name}");
        }

        SpeakerRouterLogService.Write(mediaPath, result);
        return result;
    }

    private static SpeakerRoutingResult AnalyzeSamples(
        float[] samples,
        TimeSpan duration,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (samples.Length == 0)
        {
            return CreateResult(
                LocalSpeakerLabel.NoSpeech,
                MeetingAsrRoute.BlockOrReview,
                1,
                0,
                duration,
                TimeSpan.Zero,
                stopwatch.Elapsed,
                "decoded_audio_is_empty",
                []);
        }

        var speechSegments = DetectSpeech(samples, cancellationToken);
        var speechSeconds = UnionDurationSeconds(speechSegments.Select(segment =>
            (segment.Start / (double)SampleRate, (segment.Start + segment.Samples.Length) / (double)SampleRate)));
        var speechDuration = TimeSpan.FromSeconds(Math.Max(0, speechSeconds));
        if (speechSegments.Count == 0 || speechSeconds < 0.3)
        {
            return CreateResult(
                LocalSpeakerLabel.NoSpeech,
                MeetingAsrRoute.BlockOrReview,
                0.99,
                0,
                duration,
                speechDuration,
                stopwatch.Elapsed,
                "no_reliable_speech_segments",
                []);
        }

        var observations = ExtractEmbeddings(speechSegments, cancellationToken);
        if (observations.Count == 0)
        {
            return CreateResult(
                LocalSpeakerLabel.Uncertain,
                MeetingAsrRoute.Speaker20,
                0,
                0,
                duration,
                speechDuration,
                stopwatch.Elapsed,
                "speech_detected_but_no_embedding_ready",
                []);
        }

        var clusters = ClusterObservations(observations);
        var supportedClusters = clusters
            .Select((cluster, index) => new
            {
                Cluster = cluster,
                OriginalIndex = index,
                Seconds = UnionDurationSeconds(cluster.Observations.Select(item =>
                    (item.StartSeconds, item.EndSeconds)))
            })
            .OrderByDescending(item => item.Seconds)
            .ToArray();
        var segments = supportedClusters
            .SelectMany((item, stableIndex) => item.Cluster.Observations.Select(observation =>
                new SpeakerRoutingSegment(observation.StartSeconds, observation.EndSeconds, stableIndex)))
            .OrderBy(segment => segment.StartSeconds)
            .ToArray();

        if (supportedClusters.Length >= 2)
        {
            var secondSpeakerSeconds = supportedClusters[1].Seconds;
            if (secondSpeakerSeconds >= ClearSecondSpeakerSeconds)
            {
                var similarity = CosineSimilarity(
                    supportedClusters[0].Cluster.Centroid,
                    supportedClusters[1].Cluster.Centroid);
                var confidence = Math.Clamp(0.75 + (ClusteringThreshold - similarity), 0.75, 0.99);
                return CreateResult(
                    LocalSpeakerLabel.Multi,
                    MeetingAsrRoute.Speaker20,
                    confidence,
                    supportedClusters.Length,
                    duration,
                    speechDuration,
                    stopwatch.Elapsed,
                    $"second_speaker_seconds={secondSpeakerSeconds:F3}",
                    segments);
            }

            return CreateResult(
                LocalSpeakerLabel.Uncertain,
                MeetingAsrRoute.Speaker20,
                0.5,
                supportedClusters.Length,
                duration,
                speechDuration,
                stopwatch.Elapsed,
                $"possible_short_second_speaker_seconds={secondSpeakerSeconds:F3}",
                segments);
        }

        if (speechSeconds < 2)
        {
            return CreateResult(
                LocalSpeakerLabel.Uncertain,
                MeetingAsrRoute.Speaker20,
                0.45,
                1,
                duration,
                speechDuration,
                stopwatch.Elapsed,
                $"insufficient_speech_seconds={speechSeconds:F3}",
                segments);
        }

        var singleCluster = supportedClusters[0].Cluster;
        var averageSimilarity = singleCluster.Observations.Count == 0
            ? 0
            : singleCluster.Observations.Average(observation =>
                CosineSimilarity(singleCluster.Centroid, observation.Embedding));
        var minimumSimilarity = singleCluster.Observations.Count == 0
            ? 0
            : singleCluster.Observations.Min(observation =>
                CosineSimilarity(singleCluster.Centroid, observation.Embedding));
        var orderedObservations = singleCluster.Observations.OrderBy(item => item.StartSeconds).ToArray();
        var minimumAdjacentSimilarity = orderedObservations.Length < 2
            ? 1
            : Enumerable.Range(1, orderedObservations.Length - 1).Min(index =>
                CosineSimilarity(orderedObservations[index - 1].Embedding, orderedObservations[index].Embedding));
        if (minimumSimilarity < SingleOutlierSafetyThreshold)
        {
            return CreateResult(
                LocalSpeakerLabel.Uncertain,
                MeetingAsrRoute.Speaker20,
                Math.Clamp(minimumSimilarity, 0.3, 0.74),
                1,
                duration,
                speechDuration,
                stopwatch.Elapsed,
                $"single_cluster_contains_outlier;min_similarity={minimumSimilarity:F3};min_adjacent={minimumAdjacentSimilarity:F3}",
                segments);
        }
        var overlap = DetectOverlappingSpeech(samples, cancellationToken);
        if (overlap.MaximumContiguousSeconds >= OverlapSafetySeconds)
        {
            return CreateResult(
                LocalSpeakerLabel.Uncertain,
                MeetingAsrRoute.Speaker20,
                Math.Clamp(0.6 + overlap.MaximumContiguousSeconds / 4, 0.6, 0.95),
                1,
                duration,
                speechDuration,
                stopwatch.Elapsed,
                $"overlap_safety_signal;max_contiguous_seconds={overlap.MaximumContiguousSeconds:F3};max_window_overlap_seconds={overlap.MaximumWindowTotalSeconds:F3}",
                segments);
        }
        var singleConfidence = Math.Clamp(0.5 + averageSimilarity / 2, 0.5, 0.99);
        return CreateResult(
            LocalSpeakerLabel.Single,
            MeetingAsrRoute.StandardAsr,
            singleConfidence,
            1,
            duration,
            speechDuration,
            stopwatch.Elapsed,
            $"one_stable_cluster;mean_similarity={averageSimilarity:F3};min_similarity={minimumSimilarity:F3};min_adjacent={minimumAdjacentSimilarity:F3};overlap_max={overlap.MaximumContiguousSeconds:F3};overlap_total={overlap.MaximumWindowTotalSeconds:F3}",
            segments);
    }

    private static List<DetectedSpeechSegment> DetectSpeech(
        float[] samples,
        CancellationToken cancellationToken)
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = VadModelPath;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = 0.25f;
        config.SileroVad.MinSpeechDuration = 0.3f;
        config.SileroVad.MaxSpeechDuration = 4f;
        config.SampleRate = SampleRate;
        config.NumThreads = 1;
        using var vad = new VoiceActivityDetector(config, 30);
        var result = new List<DetectedSpeechSegment>();
        var windowSize = config.SileroVad.WindowSize;
        for (var start = 0; start < samples.Length; start += windowSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(windowSize, samples.Length - start);
            var block = new float[windowSize];
            Array.Copy(samples, start, block, 0, count);
            vad.AcceptWaveform(block);
            DrainVad(vad, result);
        }
        vad.Flush();
        DrainVad(vad, result);
        return result;
    }

    private static void DrainVad(VoiceActivityDetector vad, ICollection<DetectedSpeechSegment> result)
    {
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            vad.Pop();
            if (segment.Samples.Length >= SampleRate * 0.3)
            {
                result.Add(new DetectedSpeechSegment(segment.Start, segment.Samples));
            }
        }
    }

    private static List<EmbeddingObservation> ExtractEmbeddings(
        IReadOnlyList<DetectedSpeechSegment> speechSegments,
        CancellationToken cancellationToken)
    {
        var config = new SpeakerEmbeddingExtractorConfig
        {
            Model = EmbeddingModelPath,
            NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            Debug = 0,
            Provider = "cpu"
        };
        using var extractor = new SpeakerEmbeddingExtractor(config);
        var observations = new List<EmbeddingObservation>();
        foreach (var segment in speechSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var windows = CreateEmbeddingWindows(segment.Samples);
            foreach (var window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = extractor.CreateStream();
                stream.AcceptWaveform(SampleRate, window.Samples);
                stream.InputFinished();
                if (!extractor.IsReady(stream)) continue;
                var embedding = Normalize(extractor.Compute(stream));
                var absoluteStart = (segment.Start + window.Start) / (double)SampleRate;
                var absoluteEnd = (segment.Start + window.Start + window.OriginalLength) / (double)SampleRate;
                observations.Add(new EmbeddingObservation(absoluteStart, absoluteEnd, embedding));
            }
        }
        return observations;
    }

    private static IEnumerable<EmbeddingWindow> CreateEmbeddingWindows(float[] samples)
    {
        if (samples.Length <= EmbeddingWindowSamples)
        {
            var paddedLength = Math.Max(samples.Length, SampleRate);
            var padded = new float[paddedLength];
            Array.Copy(samples, padded, samples.Length);
            yield return new EmbeddingWindow(0, samples.Length, padded);
            yield break;
        }

        var emittedLastStart = -1;
        for (var start = 0; start + SampleRate <= samples.Length; start += EmbeddingStrideSamples)
        {
            var count = Math.Min(EmbeddingWindowSamples, samples.Length - start);
            var window = new float[Math.Max(count, SampleRate)];
            Array.Copy(samples, start, window, 0, count);
            yield return new EmbeddingWindow(start, count, window);
            emittedLastStart = start;
            if (start + count >= samples.Length) yield break;
        }

        var finalStart = Math.Max(0, samples.Length - EmbeddingWindowSamples);
        if (finalStart != emittedLastStart)
        {
            var count = samples.Length - finalStart;
            var window = new float[Math.Max(count, SampleRate)];
            Array.Copy(samples, finalStart, window, 0, count);
            yield return new EmbeddingWindow(finalStart, count, window);
        }
    }

    private static List<SpeakerCluster> ClusterObservations(IEnumerable<EmbeddingObservation> observations)
    {
        var clusters = new List<SpeakerCluster>();
        foreach (var observation in observations)
        {
            var best = clusters
                .Select(cluster => new { Cluster = cluster, Similarity = CosineSimilarity(cluster.Centroid, observation.Embedding) })
                .OrderByDescending(item => item.Similarity)
                .FirstOrDefault();
            if (best is not null && best.Similarity >= ClusteringThreshold)
            {
                best.Cluster.Add(observation);
            }
            else
            {
                clusters.Add(new SpeakerCluster(observation));
            }
        }

        var merged = true;
        while (merged)
        {
            merged = false;
            for (var left = 0; left < clusters.Count && !merged; left++)
            {
                for (var right = left + 1; right < clusters.Count; right++)
                {
                    if (CosineSimilarity(clusters[left].Centroid, clusters[right].Centroid) < ClusterMergeThreshold) continue;
                    clusters[left].Merge(clusters[right]);
                    clusters.RemoveAt(right);
                    merged = true;
                    break;
                }
            }
        }
        return clusters;
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(value => value * value));
        if (norm <= 1e-9) return vector;
        for (var index = 0; index < vector.Length; index++) vector[index] = (float)(vector[index] / norm);
        return vector;
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var count = Math.Min(left.Count, right.Count);
        double dot = 0;
        for (var index = 0; index < count; index++) dot += left[index] * right[index];
        return dot;
    }

    private static double UnionDurationSeconds(IEnumerable<(double Start, double End)> intervals)
    {
        var ordered = intervals
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ToArray();
        if (ordered.Length == 0) return 0;
        var total = 0d;
        var start = ordered[0].Start;
        var end = ordered[0].End;
        foreach (var interval in ordered.Skip(1))
        {
            if (interval.Start <= end)
            {
                end = Math.Max(end, interval.End);
            }
            else
            {
                total += end - start;
                start = interval.Start;
                end = interval.End;
            }
        }
        return total + end - start;
    }

    private static OverlapEvidence DetectOverlappingSpeech(
        float[] samples,
        CancellationToken cancellationToken)
    {
        var starts = new List<int>();
        if (samples.Length <= SegmentationWindowSamples)
        {
            starts.Add(0);
        }
        else
        {
            for (var start = 0; start + SegmentationWindowSamples <= samples.Length; start += SegmentationShiftSamples)
            {
                starts.Add(start);
            }
            var finalStart = samples.Length - SegmentationWindowSamples;
            if (starts[^1] != finalStart) starts.Add(finalStart);
        }

        double maximumContiguousSeconds = 0;
        double maximumWindowTotalSeconds = 0;
        const int batchSize = 4;
        for (var batchStart = 0; batchStart < starts.Count; batchStart += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(batchSize, starts.Count - batchStart);
            var inputData = new float[count * SegmentationWindowSamples];
            for (var batchIndex = 0; batchIndex < count; batchIndex++)
            {
                var sourceStart = starts[batchStart + batchIndex];
                var copyLength = Math.Min(SegmentationWindowSamples, samples.Length - sourceStart);
                Array.Copy(
                    samples,
                    sourceStart,
                    inputData,
                    batchIndex * SegmentationWindowSamples,
                    copyLength);
            }

            var input = new DenseTensor<float>(
                inputData,
                [count, 1, SegmentationWindowSamples]);
            using var results = OverlapSession.Value.Run(
                [NamedOnnxValue.CreateFromTensor("x", input)]);
            var output = results.First().AsTensor<float>();
            var frameCount = output.Dimensions[1];
            var classCount = output.Dimensions[2];
            for (var batchIndex = 0; batchIndex < count; batchIndex++)
            {
                var contiguous = 0;
                var maximumContiguousFrames = 0;
                var overlapFrames = 0;
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var bestClass = 0;
                    var bestValue = output[batchIndex, frame, 0];
                    for (var classIndex = 1; classIndex < classCount; classIndex++)
                    {
                        var value = output[batchIndex, frame, classIndex];
                        if (value <= bestValue) continue;
                        bestValue = value;
                        bestClass = classIndex;
                    }

                    var isOverlap = bestClass >= 4;
                    if (isOverlap)
                    {
                        overlapFrames++;
                        contiguous++;
                        maximumContiguousFrames = Math.Max(maximumContiguousFrames, contiguous);
                    }
                    else
                    {
                        contiguous = 0;
                    }
                }

                maximumContiguousSeconds = Math.Max(
                    maximumContiguousSeconds,
                    maximumContiguousFrames * SegmentationFrameSeconds);
                maximumWindowTotalSeconds = Math.Max(
                    maximumWindowTotalSeconds,
                    overlapFrames * SegmentationFrameSeconds);
            }
        }

        return new OverlapEvidence(maximumContiguousSeconds, maximumWindowTotalSeconds);
    }

    private static InferenceSession CreateOverlapSession()
    {
        var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        return new InferenceSession(SegmentationModelPath, options);
    }

    private static SpeakerRoutingResult SafeFallback(
        TimeSpan duration,
        TimeSpan elapsed,
        string reason)
    {
        return CreateResult(
            LocalSpeakerLabel.Uncertain,
            MeetingAsrRoute.Speaker20,
            0,
            0,
            duration,
            TimeSpan.Zero,
            elapsed,
            reason,
            []);
    }

    private static SpeakerRoutingResult CreateResult(
        LocalSpeakerLabel label,
        MeetingAsrRoute recommendedRoute,
        double confidence,
        int detectedSpeakerCount,
        TimeSpan audioDuration,
        TimeSpan speechDuration,
        TimeSpan elapsed,
        string reason,
        IReadOnlyList<SpeakerRoutingSegment> segments)
    {
        // Shadow mode protects uncertain speech by keeping it on Speaker 2.0,
        // but confirmed no-speech should never consume a cloud recognition call.
        var executionRoute = ShadowModeEnabled && recommendedRoute != MeetingAsrRoute.BlockOrReview
            ? MeetingAsrRoute.Speaker20
            : recommendedRoute;
        return new SpeakerRoutingResult(
            label,
            recommendedRoute,
            executionRoute,
            confidence,
            detectedSpeakerCount,
            audioDuration,
            speechDuration,
            elapsed,
            reason,
            ShadowModeEnabled,
            segments);
    }

    private static DecodedAudio DecodePcm16(string mediaPath, CancellationToken cancellationToken)
    {
        using var reader = new MediaFoundationReader(mediaPath);
        if (reader.TotalTime > MaximumAnalyzedDuration)
        {
            return new DecodedAudio([], reader.TotalTime);
        }

        using var resampler = new MediaFoundationResampler(reader, AnalysisWaveFormat)
        {
            ResamplerQuality = 30
        };
        var estimatedSamples = (int)Math.Clamp(
            Math.Ceiling(Math.Max(reader.TotalTime.TotalSeconds, 1) * 16_000),
            16_000,
            int.MaxValue - 16_000L);
        var samples = new float[estimatedSamples];
        var sampleCount = 0;
        var buffer = new byte[64 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = resampler.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            var incomingSamples = read / 2;
            EnsureCapacity(ref samples, sampleCount + incomingSamples);
            for (var offset = 0; offset + 1 < read; offset += 2)
            {
                samples[sampleCount++] = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)) / 32768f;
            }
        }

        Array.Resize(ref samples, sampleCount);
        return new DecodedAudio(samples, TimeSpan.FromSeconds(sampleCount / 16_000d));
    }

    private static void EnsureCapacity(ref float[] samples, int required)
    {
        if (required <= samples.Length) return;
        var next = Math.Max(required, Math.Min(int.MaxValue, (long)samples.Length * 2));
        Array.Resize(ref samples, checked((int)next));
    }

    private static string FindModelDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Models", "speaker-router"),
            Path.Combine(Environment.CurrentDirectory, "Models", "speaker-router")
        };
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            candidates.Add(Path.Combine(current.FullName, "Models", "speaker-router"));
        }

        return candidates.FirstOrDefault(candidate =>
                   File.Exists(Path.Combine(candidate, "3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx")))
               ?? candidates[0];
    }

    private sealed record DecodedAudio(float[] Samples, TimeSpan Duration);
    private sealed record DetectedSpeechSegment(int Start, float[] Samples);
    private sealed record EmbeddingWindow(int Start, int OriginalLength, float[] Samples);
    private sealed record EmbeddingObservation(double StartSeconds, double EndSeconds, float[] Embedding);
    private sealed record OverlapEvidence(double MaximumContiguousSeconds, double MaximumWindowTotalSeconds);

    private sealed class SpeakerCluster
    {
        public SpeakerCluster(EmbeddingObservation first)
        {
            Observations.Add(first);
            Centroid = first.Embedding.ToArray();
        }

        public List<EmbeddingObservation> Observations { get; } = [];
        public float[] Centroid { get; private set; }

        public void Add(EmbeddingObservation observation)
        {
            Observations.Add(observation);
            RecalculateCentroid();
        }

        public void Merge(SpeakerCluster other)
        {
            Observations.AddRange(other.Observations);
            RecalculateCentroid();
        }

        private void RecalculateCentroid()
        {
            var centroid = new float[Centroid.Length];
            foreach (var observation in Observations)
            {
                for (var index = 0; index < centroid.Length; index++)
                {
                    centroid[index] += observation.Embedding[index];
                }
            }
            Centroid = Normalize(centroid);
        }
    }
}
