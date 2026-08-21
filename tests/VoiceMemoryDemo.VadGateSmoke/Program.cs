using NAudio.Wave;
using VoiceMemoryDemo.App.Services;

const int frameBytes = 16_000 * 2 * 40 / 1000;
var repoRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot(AppContext.BaseDirectory);
var sampleRoot = Path.Combine(repoRoot, "test-data", "speaker-router", "samples");
var cases = new[]
{
    new GateCase("english-dictation", "fleurs_07_single_en_us.flac", TimeSpan.FromMilliseconds(240), true),
    new GateCase("english-meeting", "fleurs_08_single_en_us.flac", TimeSpan.FromMilliseconds(600), true),
    new GateCase("silence", "synth_11_no_speech_silence.flac", TimeSpan.FromMilliseconds(240), false),
    new GateCase("pink-noise", "synth_12_no_speech_pink_noise.flac", TimeSpan.FromMilliseconds(240), false)
};

var passed = true;
foreach (var item in cases)
{
    var path = Path.Combine(sampleRoot, item.FileName);
    var detectedAt = Detect(path, item.MinimumSpeech);
    var detected = detectedAt is not null;
    var casePassed = detected == item.ExpectedSpeech;
    passed &= casePassed;
    Console.WriteLine(
        $"CASE {item.Name}: expected={item.ExpectedSpeech}; detected={detected}; " +
        $"detectedAt={(detectedAt is null ? "n/a" : $"{detectedAt.Value.TotalMilliseconds:F0}ms")}; " +
        $"result={(casePassed ? "PASS" : "FAIL")}");
}

var transcriptCases = new[]
{
    ("english-filler", "Speaker 1: Ah.", false),
    ("chinese-filler", "说话人1：嗯。", false),
    ("english-content", "Speaker 1: Hi everyone.", true),
    ("chinese-content", "说话人1：现在开始开会。", true)
};
foreach (var item in transcriptCases)
{
    var meaningful = MeetingTranscriptQualityService.HasMeaningfulContent(item.Item2);
    var casePassed = meaningful == item.Item3;
    passed &= casePassed;
    Console.WriteLine(
        $"CASE {item.Item1}: expected={item.Item3}; meaningful={meaningful}; " +
        $"result={(casePassed ? "PASS" : "FAIL")}");
}

Console.WriteLine(passed
    ? "PASS: live VAD accepts real English speech and rejects silence/pink noise before paid ASR."
    : "FAIL: live VAD gate did not meet the frozen speech/noise expectations.");
return passed ? 0 : 1;

static TimeSpan? Detect(string path, TimeSpan minimumSpeech)
{
    using var gate = new LiveSpeechDetectionService(minimumSpeech);
    using var reader = new MediaFoundationReader(path);
    using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16_000, 16, 1))
    {
        ResamplerQuality = 60
    };
    var frame = new byte[frameBytes];
    long bytes = 0;
    while (true)
    {
        var total = 0;
        while (total < frame.Length)
        {
            var read = resampler.Read(frame, total, frame.Length - total);
            if (read == 0) break;
            total += read;
        }
        if (total == 0) return null;
        bytes += total;
        if (gate.TryAcceptPcm16(frame.AsSpan(0, total)))
        {
            return TimeSpan.FromSeconds(bytes / (16_000d * 2));
        }
    }
}

static string FindRepoRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "VoiceMemoryDemo.App")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the voice-memory-demo repository.");
}

sealed record GateCase(string Name, string FileName, TimeSpan MinimumSpeech, bool ExpectedSpeech);
