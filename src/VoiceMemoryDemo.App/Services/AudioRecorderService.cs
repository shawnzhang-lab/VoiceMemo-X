using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceMemoryDemo.App.Services;

public sealed class AudioRecorderService : IDisposable
{
    private const int OutputSampleRate = 16_000;
    private const int OutputSamplesPerChunk = 640;
    private static readonly TimeSpan OutputChunkDuration = TimeSpan.FromMilliseconds(40);

    private readonly List<CapturePipeline> _sources = [];
    private readonly float[] _mixedSamples = new float[OutputSamplesPerChunk];
    private CancellationTokenSource? _mixCancellation;
    private Task? _mixTask;
    private bool _stopping;

    public event Action<byte[]>? AudioAvailable;
    public event Action<byte[], double>? AudioFrameAvailable;
    public event Action<double>? LevelChanged;

    public bool IsRecording => _mixCancellation is not null;
    public string DeviceName { get; private set; } = "Windows 默认麦克风";
    public string SystemAudioDeviceName { get; private set; } = string.Empty;
    public string AudioSourceDescription => SystemAudioActive
        ? $"麦克风：{DeviceName} · 电脑声音：{SystemAudioDeviceName}"
        : $"麦克风：{DeviceName}";
    public bool SystemAudioRequested { get; private set; }
    public bool SystemAudioActive => _sources.Any(source => source.Kind == AudioSourceKind.System);
    public string SystemAudioWarning { get; private set; } = string.Empty;
    public double MaxObservedLevel { get; private set; }
    public double MicrophoneMaxObservedLevel { get; private set; }
    public double SystemAudioMaxObservedLevel { get; private set; }

    public void Start(bool includeSystemAudio = false)
    {
        if (IsRecording || _sources.Count > 0) throw new InvalidOperationException("录音已经开始。 ");

        SystemAudioRequested = includeSystemAudio;
        SystemAudioWarning = string.Empty;
        MaxObservedLevel = 0;
        MicrophoneMaxObservedLevel = 0;
        SystemAudioMaxObservedLevel = 0;
        _stopping = false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var microphone = GetDefaultDevice(enumerator, DataFlow.Capture, Role.Communications,
                "没有检测到 Windows 默认麦克风，请先在系统声音设置中选择输入设备。");
            if (microphone.State != DeviceState.Active)
            {
                microphone.Dispose();
                throw new InvalidOperationException("Windows 默认麦克风当前不可用，请检查连接或系统声音设置。 ");
            }

            DeviceName = microphone.FriendlyName;
            _sources.Add(CapturePipeline.CreateMicrophone(microphone));

            if (includeSystemAudio)
            {
                AddDefaultSystemAudioSources(enumerator);
                if (!SystemAudioActive)
                {
                    SystemAudioWarning = "没有检测到可用的 Windows 播放设备，本次只记录麦克风。";
                    DiagnosticLogService.WriteEvent("SystemAudioUnavailable", SystemAudioWarning);
                }
                else
                {
                    SystemAudioDeviceName = string.Join(" + ", _sources
                        .Where(source => source.Kind == AudioSourceKind.System)
                        .Select(source => source.DeviceName));
                }
            }

            foreach (var source in _sources.ToArray())
            {
                try
                {
                    source.Start();
                }
                catch (Exception ex) when (source.Kind == AudioSourceKind.System)
                {
                    DiagnosticLogService.Write("SystemAudioStart", ex);
                    source.Dispose();
                    _sources.Remove(source);
                }
            }

            if (!SystemAudioActive && includeSystemAudio && string.IsNullOrEmpty(SystemAudioWarning))
            {
                SystemAudioWarning = "Windows 电脑声音采集启动失败，本次只记录麦克风。";
            }

            _mixCancellation = new CancellationTokenSource();
            _mixTask = MixLoopAsync(_mixCancellation.Token);
            DiagnosticLogService.WriteEvent(
                "AudioCaptureStarted",
                $"systemAudioRequested={includeSystemAudio}; systemAudioActive={SystemAudioActive}; " +
                $"sources={AudioSourceDescription}; warning={SystemAudioWarning}");
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRecording || _stopping) return;
        _stopping = true;

        Exception? stopError = null;
        try
        {
            var stopTasks = _sources.Select(source => source.StopAsync()).ToArray();
            try
            {
                await Task.WhenAll(stopTasks).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                stopError = ex;
                DiagnosticLogService.Write("AudioCaptureStop", ex);
            }

            // Allow the 40 ms output pump to consume the final capture buffers so the
            // last syllable is not clipped when the user presses Right Alt.
            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }
        finally
        {
            _mixCancellation?.Cancel();
            if (_mixTask is not null)
            {
                try { await _mixTask; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { DiagnosticLogService.Write("AudioMixPump", ex); }
            }

            DiagnosticLogService.WriteEvent(
                "AudioCaptureStopped",
                $"microphoneMaxLevel={MicrophoneMaxObservedLevel:F4}; " +
                $"systemAudioMaxLevel={SystemAudioMaxObservedLevel:F4}; mixedMaxLevel={MaxObservedLevel:F4}");
            Cleanup();
        }

        if (stopError is not null) throw stopError;
    }

    private void AddDefaultSystemAudioSources(MMDeviceEnumerator enumerator)
    {
        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in new[] { Role.Communications, Role.Multimedia })
        {
            MMDevice? device = null;
            try
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                if (device.State != DeviceState.Active || !deviceIds.Add(device.ID))
                {
                    device.Dispose();
                    continue;
                }

                _sources.Add(CapturePipeline.CreateSystemAudio(device));
                device = null;
            }
            catch (Exception ex)
            {
                device?.Dispose();
                DiagnosticLogService.WriteEvent(
                    "SystemAudioDeviceDiscovery",
                    $"role={role}; error={ex.GetBaseException().Message}");
            }
        }
    }

    private static MMDevice GetDefaultDevice(
        MMDeviceEnumerator enumerator,
        DataFlow dataFlow,
        Role role,
        string errorMessage)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(dataFlow, role);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    private async Task MixLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(OutputChunkDuration);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            EmitMixedFrame();
        }
    }

    private void EmitMixedFrame()
    {
        Array.Clear(_mixedSamples);
        double microphoneRms = 0;
        double systemEnergy = 0;
        var systemSourceCount = 0;

        foreach (var source in _sources)
        {
            var count = source.ReadFrame();
            if (count <= 0) continue;

            double sourceEnergy = 0;
            for (var index = 0; index < count; index++)
            {
                var sample = source.Samples[index];
                sourceEnergy += sample * sample;
                _mixedSamples[index] += sample * (source.Kind == AudioSourceKind.Microphone ? 0.90f : 0.90f);
            }

            var rms = Math.Sqrt(sourceEnergy / count);
            if (source.Kind == AudioSourceKind.Microphone)
            {
                microphoneRms = Math.Max(microphoneRms, rms);
            }
            else
            {
                systemEnergy += rms * rms;
                systemSourceCount++;
            }
        }

        var systemRms = systemSourceCount == 0 ? 0 : Math.Sqrt(systemEnergy);
        var microphoneLevel = Math.Clamp(microphoneRms * 4, 0, 1);
        var systemLevel = Math.Clamp(systemRms * 4, 0, 1);
        MicrophoneMaxObservedLevel = Math.Max(MicrophoneMaxObservedLevel, microphoneLevel);
        SystemAudioMaxObservedLevel = Math.Max(SystemAudioMaxObservedLevel, systemLevel);

        var peak = 0f;
        for (var index = 0; index < _mixedSamples.Length; index++)
        {
            peak = Math.Max(peak, Math.Abs(_mixedSamples[index]));
        }
        var scale = peak > 0.98f ? 0.98f / peak : 1f;

        double mixedEnergy = 0;
        var pcm = new byte[_mixedSamples.Length * 2];
        for (var index = 0; index < _mixedSamples.Length; index++)
        {
            var sample = Math.Clamp(_mixedSamples[index] * scale, -1f, 1f);
            mixedEnergy += sample * sample;
            var value = (short)Math.Round(sample * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(index * 2, 2), value);
        }

        var mixedRms = Math.Sqrt(mixedEnergy / _mixedSamples.Length);
        var level = Math.Clamp(Math.Max(mixedRms * 4, Math.Max(microphoneLevel, systemLevel)), 0, 1);
        MaxObservedLevel = Math.Max(MaxObservedLevel, level);
        LevelChanged?.Invoke(level);
        AudioAvailable?.Invoke(pcm);
        AudioFrameAvailable?.Invoke(pcm, level);
    }

    private void Cleanup()
    {
        _mixCancellation?.Cancel();
        _mixCancellation?.Dispose();
        _mixCancellation = null;
        _mixTask = null;
        foreach (var source in _sources) source.Dispose();
        _sources.Clear();
        _stopping = false;
    }

    public void Dispose()
    {
        _mixCancellation?.Cancel();
        foreach (var source in _sources)
        {
            try { source.RequestStop(); } catch { }
        }
        Cleanup();
    }

    private enum AudioSourceKind
    {
        Microphone,
        System
    }

    private sealed class CapturePipeline : IDisposable
    {
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly BufferedWaveProvider _inputBuffer;
        private readonly WdlResamplingSampleProvider _resampler;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;
        private bool _disposed;

        private CapturePipeline(MMDevice device, WasapiCapture capture, AudioSourceKind kind)
        {
            _device = device;
            _capture = capture;
            Kind = kind;
            DeviceName = device.FriendlyName;
            Samples = new float[OutputSamplesPerChunk];
            _inputBuffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            ISampleProvider samples = _inputBuffer.ToSampleProvider();
            samples = new MonoMixSampleProvider(samples);
            _resampler = new WdlResamplingSampleProvider(samples, OutputSampleRate);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }

        public AudioSourceKind Kind { get; }
        public string DeviceName { get; }
        public float[] Samples { get; }

        public static CapturePipeline CreateMicrophone(MMDevice device) =>
            new(device, new WasapiCapture(device), AudioSourceKind.Microphone);

        public static CapturePipeline CreateSystemAudio(MMDevice device) =>
            new(device, new WasapiLoopbackCapture(device), AudioSourceKind.System);

        public void Start()
        {
            _capture.StartRecording();
            _started = true;
        }

        public int ReadFrame()
        {
            Array.Clear(Samples);
            return _resampler.Read(Samples, 0, Samples.Length);
        }

        public void RequestStop()
        {
            if (_started) _capture.StopRecording();
        }

        public async Task StopAsync()
        {
            if (!_started) return;
            _capture.StopRecording();
            await _stopped.Task;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0) _inputBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _started = false;
            if (e.Exception is null) _stopped.TrySetResult();
            else _stopped.TrySetException(e.Exception);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_started)
            {
                try { _capture.StopRecording(); } catch { }
            }
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _device.Dispose();
        }
    }

    private sealed class MonoMixSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _sourceBuffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(
            source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var required = count * _channels;
            if (_sourceBuffer.Length < required) _sourceBuffer = new float[required];
            var sourceRead = source.Read(_sourceBuffer, 0, required);
            var frames = sourceRead / _channels;

            for (var frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                var baseIndex = frame * _channels;
                for (var channel = 0; channel < _channels; channel++)
                {
                    sum += _sourceBuffer[baseIndex + channel];
                }
                buffer[offset + frame] = sum / _channels;
            }

            return frames;
        }
    }
}
