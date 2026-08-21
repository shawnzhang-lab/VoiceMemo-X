using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceMemoryDemo.App.Services;

var recorder = new AudioRecorderService();
var frameCount = 0;
recorder.AudioFrameAvailable += (_, _) => Interlocked.Increment(ref frameCount);

try
{
    recorder.Start(includeSystemAudio: true);
    Console.WriteLine($"SOURCES={recorder.AudioSourceDescription}");
    Console.WriteLine($"SYSTEM_ACTIVE={recorder.SystemAudioActive}");
    if (!recorder.SystemAudioActive)
    {
        Console.Error.WriteLine($"FAIL: {recorder.SystemAudioWarning}");
        return 2;
    }

    await Task.Delay(350);
    using (var output = new WaveOutEvent())
    {
        var signal = new SignalGenerator(48_000, 2)
        {
            Frequency = 740,
            Gain = 0.06,
            Type = SignalGeneratorType.Sin
        };
        output.Init(signal);
        output.Play();
        await Task.Delay(1_200);
        output.Stop();
    }
    await Task.Delay(250);
    await recorder.StopAsync();

    Console.WriteLine($"FRAMES={frameCount}");
    Console.WriteLine($"MIC_MAX={recorder.MicrophoneMaxObservedLevel:F4}");
    Console.WriteLine($"SYSTEM_MAX={recorder.SystemAudioMaxObservedLevel:F4}");
    Console.WriteLine($"MIXED_MAX={recorder.MaxObservedLevel:F4}");

    if (frameCount < 25)
    {
        Console.Error.WriteLine("FAIL: mixed audio pump produced too few frames.");
        return 3;
    }
    if (recorder.SystemAudioMaxObservedLevel < 0.02)
    {
        Console.Error.WriteLine("FAIL: the playback test tone was not captured by WASAPI loopback.");
        return 4;
    }

    Console.WriteLine("PASS: Windows playback audio reached the system-audio channel.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    recorder.Dispose();
}
