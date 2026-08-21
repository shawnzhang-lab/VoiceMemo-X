using System.Buffers.Binary;
using System.Diagnostics;
using NAudio.Wave;
using SherpaOnnx;

internal static class FullDiarizationProbe
{
    public static Task RunAsync(string path) => Task.Run(() => Run(path));

    private static void Run(string path)
    {
        var samples = Decode(path);
        Console.WriteLine($"PROBE {Path.GetFileName(path)} duration={samples.Length / 16000d:F2}s");
        foreach (var threshold in new[] { 0.70f })
        {
            var modelRoot = Path.Combine(Environment.CurrentDirectory, "Models", "speaker-router");
            var config = new OfflineSpeakerDiarizationConfig();
            config.Segmentation.Pyannote.Model = Path.Combine(
                modelRoot,
                "sherpa-onnx-pyannote-segmentation-3-0",
                "model.onnx");
            config.Segmentation.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.Embedding.Model = Path.Combine(
                modelRoot,
                "3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx");
            config.Embedding.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.Clustering.NumClusters = -1;
            config.Clustering.Threshold = threshold;
            config.MinDurationOn = 0.3f;
            config.MinDurationOff = 0.5f;
            var stopwatch = Stopwatch.StartNew();
            using var diarizer = new OfflineSpeakerDiarization(config);
            var segments = diarizer.Process(samples);
            stopwatch.Stop();
            var speakers = segments.GroupBy(segment => segment.Speaker)
                .Select(group => new
                {
                    Speaker = group.Key,
                    Seconds = group.Sum(segment => segment.End - segment.Start)
                })
                .OrderByDescending(item => item.Seconds)
                .ToArray();
            Console.WriteLine(
                $"  threshold={threshold:F2} clusters={speakers.Length} " +
                $"durations=[{string.Join(',', speakers.Select(item => item.Seconds.ToString("F2")))}] " +
                $"elapsed={stopwatch.Elapsed.TotalMilliseconds:F0}ms");
        }
    }

    private static float[] Decode(string path)
    {
        using var reader = new MediaFoundationReader(path);
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1));
        var samples = new List<float>((int)Math.Ceiling(reader.TotalTime.TotalSeconds * 16000));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = resampler.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            for (var offset = 0; offset + 1 < read; offset += 2)
            {
                samples.Add(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)) / 32768f);
            }
        }
        return samples.ToArray();
    }
}
