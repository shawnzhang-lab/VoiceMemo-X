using System.Buffers.Binary;
using SherpaOnnx;

namespace VoiceMemoryDemo.App.Services;

/// <summary>
/// Small streaming VAD gate used before opening a paid ASR session.
/// It requires sustained model-confirmed speech instead of treating a brief
/// RMS spike as speech, so clicks, fan noise, and device wake-up transients do
/// not defeat the five-second no-speech safeguard.
/// </summary>
public sealed class LiveSpeechDetectionService : IDisposable
{
    private const int SampleRate = 16_000;
    private readonly object _gate = new();
    private readonly VoiceActivityDetector _vad;
    private readonly float[] _window;
    private readonly int _requiredSpeechSamples;
    private int _windowCount;
    private int _continuousSpeechSamples;
    private bool _speechConfirmed;
    private bool _disposed;

    public LiveSpeechDetectionService(TimeSpan minimumSpeechDuration)
    {
        var modelPath = LocalSpeakerRouterService.VadModelFilePath;
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The local voice-activity model is missing.", modelPath);
        }

        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = 0.55f;
        config.SileroVad.MinSilenceDuration = 0.20f;
        config.SileroVad.MinSpeechDuration = 0.20f;
        config.SileroVad.MaxSpeechDuration = 5f;
        config.SampleRate = SampleRate;
        config.NumThreads = 1;

        _vad = new VoiceActivityDetector(config, 10);
        _window = new float[config.SileroVad.WindowSize];
        _requiredSpeechSamples = Math.Max(
            _window.Length,
            (int)Math.Ceiling(minimumSpeechDuration.TotalSeconds * SampleRate));
    }

    public bool HasActiveSpeech
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && (_speechConfirmed || _vad.IsSpeechDetected());
            }
        }
    }

    public bool TryAcceptPcm16(ReadOnlySpan<byte> pcm)
    {
        lock (_gate)
        {
            if (_disposed || _speechConfirmed) return _speechConfirmed;

            var usableBytes = pcm.Length - pcm.Length % 2;
            for (var offset = 0; offset < usableBytes; offset += 2)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset, 2));
                _window[_windowCount++] = sample / 32768f;
                if (_windowCount < _window.Length) continue;

                _vad.AcceptWaveform(_window);
                _windowCount = 0;
                if (_vad.IsSpeechDetected())
                {
                    _continuousSpeechSamples += _window.Length;
                    if (_continuousSpeechSamples >= _requiredSpeechSamples)
                    {
                        _speechConfirmed = true;
                        return true;
                    }
                }
                else
                {
                    _continuousSpeechSamples = 0;
                }
            }

            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _vad.Dispose();
        }
    }
}
