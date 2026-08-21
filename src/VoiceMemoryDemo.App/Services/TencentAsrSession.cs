using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class TencentAsrSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    };
    private static readonly HttpMessageInvoker SharedInvoker = new(SharedHandler, disposeHandler: false);
    private readonly ClientWebSocket _socket = new();
    private readonly Channel<byte[]> _audioChannel = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(250)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<int, string> _settledSegments = new();
    private readonly ConcurrentDictionary<int, SpeakerSentenceState> _speakerSentences = new();
    private readonly HashSet<int> _speakerSegmentsEmitted = [];
    private readonly object _speakerGate = new();
    private readonly Stopwatch _telemetryClock = new();
    private Task? _receiveLoop;
    private Task? _sendLoop;
    private int _partialIndex = -1;
    private string _partialText = string.Empty;
    private bool _speakerDiarization;
    private bool _englishSpeakerLabels;
    private bool _finishing;
    private string _requestedEngineModel = string.Empty;
    private string _signedRequestEngineModel = string.Empty;
    private string _requestFingerprint = string.Empty;
    private string _voiceId = string.Empty;
    private string _serverAcknowledgedVoiceId = string.Empty;
    private bool _handshakeAcknowledged;
    private long _connectedAtTicks = -1;
    private long _firstAudioAtTicks = -1;
    private long _firstResultAtTicks = -1;
    private long _firstPartialAtTicks = -1;
    private long _audioEndAtTicks = -1;
    private long _finalAtTicks = -1;
    private long _queuedAudioBytes;
    private int? _serviceErrorCode;
    private string _serviceErrorMessage = string.Empty;
    private int _finalTranscriptLength;

    public event Action<string>? TranscriptChanged;
    public event Action<int, string>? SegmentSettled;

    public TencentAsrTelemetry Telemetry => new(
        _requestedEngineModel,
        _signedRequestEngineModel,
        _requestFingerprint,
        _voiceId,
        _serverAcknowledgedVoiceId,
        _handshakeAcknowledged &&
        !string.IsNullOrWhiteSpace(_serverAcknowledgedVoiceId) &&
        string.Equals(_voiceId, _serverAcknowledgedVoiceId, StringComparison.Ordinal) &&
        string.Equals(_requestedEngineModel, _signedRequestEngineModel, StringComparison.Ordinal),
        ToMilliseconds(_connectedAtTicks),
        DifferenceMilliseconds(_firstAudioAtTicks, _firstResultAtTicks),
        DifferenceMilliseconds(_firstAudioAtTicks, _firstPartialAtTicks),
        DifferenceMilliseconds(_audioEndAtTicks, _finalAtTicks),
        ToMilliseconds(_finalAtTicks),
        Interlocked.Read(ref _queuedAudioBytes) / (16_000d * 2),
        _serviceErrorCode,
        _serviceErrorMessage,
        _finalAtTicks >= 0 && _serviceErrorCode is null,
        _finalAtTicks >= 0 && _finalTranscriptLength == 0);

    public static async Task WarmupAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await Dns.GetHostAddressesAsync("asr.cloud.tencent.com", timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://asr.cloud.tencent.com/");
            using var response = await SharedInvoker.SendAsync(request, timeout.Token);
        }
        catch
        {
            // Warmup is best effort. A real session will surface actionable connection errors.
        }
    }

    public static Uri BuildSignedUri(
        AppSettings settings,
        string voiceId,
        long timestamp,
        bool enableSpeakerDiarization = false)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["convert_num_mode"] = "1",
            ["engine_model_type"] = enableSpeakerDiarization
                ? "16k_zh_en_speaker_2.0"
                : settings.TencentEngineModel,
            ["expired"] = (timestamp + 24 * 60 * 60).ToString(),
            ["filter_dirty"] = "0",
            ["filter_empty_result"] = "1",
            ["filter_modal"] = "0",
            ["needvad"] = "1",
            ["nonce"] = timestamp.ToString(),
            ["reinforce_hotword"] = "0",
            ["secretid"] = settings.TencentSecretId,
            ["timestamp"] = timestamp.ToString(),
            ["voice_format"] = "1",
            ["voice_id"] = voiceId,
            ["word_info"] = "0"
        };

        if (enableSpeakerDiarization)
        {
            // Tencent realtime V2 sentence mode returns a full sentence snapshot with speaker_id.
            // Keep these parameters isolated from normal voice input and legacy file import.
            parameters["result_mod"] = "1";
            parameters["sentence_strategy"] = "1";
            parameters["speaker_diarization"] = "1";
            parameters.Remove("word_info");
        }
        else
        {
            parameters["filter_punc"] = "0";
            parameters["max_speak_time"] = settings.EnableMeetingMode ? "60000" : "0";
        }

        var query = string.Join("&", parameters.Select(pair => $"{pair.Key}={pair.Value}"));
        var canonical = $"asr.cloud.tencent.com/asr/v2/{settings.TencentAppId}?{query}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(settings.TencentSecretKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        return new Uri($"wss://{canonical}&signature={Uri.EscapeDataString(signature)}");
    }

    public async Task StartAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default,
        bool enableSpeakerDiarization = false)
    {
        if (!settings.HasTencentCredentials)
        {
            throw new InvalidOperationException("请先在“连接设置”中填写腾讯云 AppID、SecretID 和 SecretKey。 ");
        }

        _speakerDiarization = enableSpeakerDiarization;
        _englishSpeakerLabels = MeetingSummaryTemplateDefaults.IsEnglish(settings.UiLanguage);
        var voiceId = Guid.NewGuid().ToString();
        _requestedEngineModel = enableSpeakerDiarization
            ? "16k_zh_en_speaker_2.0"
            : settings.TencentEngineModel;
        _voiceId = voiceId;
        _telemetryClock.Restart();
        var uri = BuildSignedUri(
            settings,
            voiceId,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            enableSpeakerDiarization);
        _signedRequestEngineModel = GetQueryValue(uri, "engine_model_type") ?? string.Empty;
        _requestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{uri.Host}|{uri.AbsolutePath}|engine_model_type={_signedRequestEngineModel}|voice_id={voiceId}|voice_format=1")));
        await _socket.ConnectAsync(uri, SharedInvoker, cancellationToken);

        _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
        await _started.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        _connectedAtTicks = _telemetryClock.ElapsedTicks;
        _sendLoop = SendLoopAsync(_lifetime.Token);
    }

    public bool QueueAudio(byte[] chunk)
    {
        if (_finishing || !_audioChannel.Writer.TryWrite(chunk)) return false;
        Interlocked.CompareExchange(ref _firstAudioAtTicks, _telemetryClock.ElapsedTicks, -1);
        Interlocked.Add(ref _queuedAudioBytes, chunk.Length);
        return true;
    }

    public async Task<string> CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_finishing) return await _completed.Task.WaitAsync(cancellationToken);
        _finishing = true;
        _audioChannel.Writer.TryComplete();
        if (_sendLoop is not null) await _sendLoop;
        _audioEndAtTicks = _telemetryClock.ElapsedTicks;

        var endMessage = Encoding.UTF8.GetBytes("{\"type\":\"end\"}");
        await _socket.SendAsync(endMessage, WebSocketMessageType.Text, true, cancellationToken);
        var result = await _completed.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);

        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken); }
            catch { }
        }

        return result.Trim();
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        var pacingClock = System.Diagnostics.Stopwatch.StartNew();
        var scheduledAudio = TimeSpan.Zero;
        await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(cancellationToken))
        {
            await _socket.SendAsync(chunk, WebSocketMessageType.Binary, true, cancellationToken);
            scheduledAudio += TimeSpan.FromSeconds(chunk.Length / (16_000d * 2));
            var delay = scheduledAudio - pacingClock.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var json = await ReceiveTextMessageAsync(cancellationToken);
                if (json is null) break;

                var response = JsonSerializer.Deserialize<AsrResponse>(json, JsonOptions);
                if (response is null) continue;
                if (response.Code != 0)
                {
                    _serviceErrorCode = response.Code;
                    _serviceErrorMessage = response.Message;
                    throw new InvalidOperationException($"腾讯 ASR 返回错误 {response.Code}：{response.Message}");
                }

                if (!string.IsNullOrWhiteSpace(response.VoiceId))
                {
                    _serverAcknowledgedVoiceId = response.VoiceId;
                }
                if (!_started.Task.IsCompleted && string.Equals(response.VoiceId, _voiceId, StringComparison.Ordinal))
                {
                    _handshakeAcknowledged = true;
                }

                _started.TrySetResult();
                if (response.Result is not null)
                {
                    ApplyResult(response.Result);
                }
                if (response.Sentences is not null)
                {
                    ApplySpeakerSentences(response.Sentences);
                }

                if (response.Final == 1)
                {
                    var transcript = BuildTranscript();
                    _finalTranscriptLength = transcript.Length;
                    _finalAtTicks = _telemetryClock.ElapsedTicks;
                    _completed.TrySetResult(transcript);
                    return;
                }
            }

            if (!_completed.Task.IsCompleted && _finishing)
            {
                _completed.TrySetException(new InvalidOperationException("腾讯 ASR 连接提前关闭，未收到最终结果。 "));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _started.TrySetCanceled(cancellationToken);
            _completed.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            _serviceErrorMessage = ex.Message;
            _started.TrySetException(ex);
            _completed.TrySetException(ex);
        }
    }

    private void ApplyResult(AsrResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.VoiceTextStr))
        {
            Interlocked.CompareExchange(ref _firstResultAtTicks, _telemetryClock.ElapsedTicks, -1);
            if (result.SliceType == 1)
            {
                Interlocked.CompareExchange(ref _firstPartialAtTicks, _telemetryClock.ElapsedTicks, -1);
            }
        }
        if (result.SliceType == 2)
        {
            var settledText = result.VoiceTextStr ?? string.Empty;
            var changed = !_settledSegments.TryGetValue(result.Index, out var previousText) ||
                          !string.Equals(previousText, settledText, StringComparison.Ordinal);
            _settledSegments[result.Index] = settledText;
            if (_partialIndex == result.Index)
            {
                _partialIndex = -1;
                _partialText = string.Empty;
            }
            if (changed && !string.IsNullOrWhiteSpace(settledText))
            {
                SegmentSettled?.Invoke(result.Index, settledText);
            }
        }
        else
        {
            _partialIndex = result.Index;
            _partialText = result.VoiceTextStr ?? string.Empty;
        }

        TranscriptChanged?.Invoke(BuildTranscript());
    }

    private void ApplySpeakerSentences(SpeakerSentences sentences)
    {
        foreach (var sentence in sentences.SentenceList)
        {
            var text = sentence.Sentence?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;
            Interlocked.CompareExchange(ref _firstResultAtTicks, _telemetryClock.ElapsedTicks, -1);

            var state = new SpeakerSentenceState(
                sentence.SentenceId,
                sentence.SpeakerId,
                sentence.SentenceType,
                sentence.StartTime,
                sentence.EndTime,
                text);
            _speakerSentences[sentence.SentenceId] = state;

            // The service can initially return speaker_id=-1 and replace it later.
            // Feed Flash only once the sentence is final and has a stable speaker label.
            if (sentence.SentenceType == 1 && sentence.SpeakerId >= 0)
            {
                var shouldEmit = false;
                lock (_speakerGate)
                {
                    shouldEmit = _speakerSegmentsEmitted.Add(sentence.SentenceId);
                }
                if (shouldEmit)
                {
                    SegmentSettled?.Invoke(sentence.SentenceId, FormatSpeakerSentence(state));
                }
            }
        }

        TranscriptChanged?.Invoke(BuildTranscript());
    }

    private string BuildTranscript()
    {
        if (_speakerDiarization)
        {
            return string.Join(
                Environment.NewLine,
                _speakerSentences.Values
                    .OrderBy(sentence => sentence.SentenceId)
                    .Select(FormatSpeakerSentence));
        }

        var builder = new StringBuilder();
        foreach (var segment in _settledSegments.OrderBy(pair => pair.Key))
        {
            builder.Append(segment.Value);
        }

        if (_partialIndex >= 0 && !_settledSegments.ContainsKey(_partialIndex))
        {
            builder.Append(_partialText);
        }

        return builder.ToString();
    }

    private string FormatSpeakerSentence(SpeakerSentenceState sentence)
    {
        var speaker = sentence.SpeakerId >= 0
            ? _englishSpeakerLabels
                ? $"Speaker {sentence.SpeakerId + 1}"
                : $"说话人{sentence.SpeakerId + 1}"
            : _englishSpeakerLabels
                ? "Speaker pending"
                : "说话人待定";
        return _englishSpeakerLabels
            ? $"{speaker}: {sentence.Text}"
            : $"{speaker}：{sentence.Text}";
    }

    private async Task<string?> ReceiveTextMessageAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage) break;
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        _audioChannel.Writer.TryComplete();
        _lifetime.Cancel();
        _started.TrySetCanceled(_lifetime.Token);
        _completed.TrySetCanceled(_lifetime.Token);
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None); }
            catch { }
        }
        _socket.Dispose();
        _lifetime.Dispose();
    }

    private double? ToMilliseconds(long ticks) => ticks < 0
        ? null
        : ticks * 1000d / Stopwatch.Frequency;

    private static double? DifferenceMilliseconds(long startTicks, long endTicks) =>
        startTicks < 0 || endTicks < 0
            ? null
            : (endTicks - startTicks) * 1000d / Stopwatch.Frequency;

    private static string? GetQueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal)) continue;
            return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
        return null;
    }

    private sealed class AsrResponse
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = string.Empty;
        public int Final { get; set; }
        public AsrResult? Result { get; set; }
        public SpeakerSentences? Sentences { get; set; }
    }

    private sealed class AsrResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("slice_type")]
        public int SliceType { get; set; }
        public int Index { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("voice_text_str")]
        public string? VoiceTextStr { get; set; }
    }

    private sealed class SpeakerSentences
    {
        [System.Text.Json.Serialization.JsonPropertyName("sentence_list")]
        public List<SpeakerSentence> SentenceList { get; set; } = [];
    }

    private sealed class SpeakerSentence
    {
        public string? Sentence { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sentence_type")]
        public int SentenceType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sentence_id")]
        public int SentenceId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("speaker_id")]
        public int SpeakerId { get; set; } = -1;
        [System.Text.Json.Serialization.JsonPropertyName("start_time")]
        public uint StartTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("end_time")]
        public uint EndTime { get; set; }
    }

    private sealed record SpeakerSentenceState(
        int SentenceId,
        int SpeakerId,
        int SentenceType,
        uint StartTime,
        uint EndTime,
        string Text);
}

public sealed record TencentAsrTelemetry(
    string RequestedEngineModel,
    string SignedRequestEngineModel,
    string RequestFingerprint,
    string VoiceId,
    string ServerAcknowledgedVoiceId,
    bool ServerAcceptedSignedRequest,
    double? ConnectionMilliseconds,
    double? FirstResultAfterFirstAudioMilliseconds,
    double? FirstPartialAfterFirstAudioMilliseconds,
    double? AudioEndToFinalMilliseconds,
    double? TotalMilliseconds,
    double QueuedAudioSeconds,
    int? ServiceErrorCode,
    string ServiceErrorMessage,
    bool CompletedSuccessfully,
    bool FinalTranscriptEmpty);
