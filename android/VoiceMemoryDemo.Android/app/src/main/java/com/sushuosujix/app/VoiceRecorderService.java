package com.sushuosujix.app;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.SystemClock;
import android.service.quicksettings.TileService;

import androidx.annotation.Nullable;
import androidx.core.app.NotificationCompat;
import androidx.core.app.ServiceCompat;
import androidx.core.content.ContextCompat;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayDeque;
import java.util.Arrays;
import java.util.Queue;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class VoiceRecorderService extends Service implements TencentAsrClient.Listener {
    static final String ACTION_START = "com.sushuosujix.action.START";
    static final String ACTION_STOP = "com.sushuosujix.action.STOP";
    static final String ACTION_STATE = "com.sushuosujix.action.STATE";
    static final String EXTRA_STATE = "state";
    static final String EXTRA_DETAIL = "detail";
    static final String EXTRA_RAW = "raw";
    static final String EXTRA_FINAL = "final";
    static final String EXTRA_LEVEL = "level";

    private static final String CHANNEL_ID = "voice_input";
    private static final int NOTIFICATION_ID = 4101;
    private static final int SAMPLE_RATE = 16_000;
    private static final int FRAME_SAMPLES = 640;
    private static final int PRELUDE_FRAME_LIMIT = 25;
    private static final int CONNECTING_FRAME_LIMIT = 300;

    private final Object audioGate = new Object();
    private final Queue<byte[]> pendingAudio = new ArrayDeque<>();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final ExecutorService textExecutor = Executors.newSingleThreadExecutor();
    private volatile boolean recording;
    private volatile boolean speechDetected;
    private volatile boolean asrReady;
    private volatile boolean finishWhenReady;
    private AudioRecord audioRecord;
    private Thread audioThread;
    private SpeechGate speechGate;
    private TencentAsrClient asrClient;
    private SettingsStore store;
    private AppSettings settings;
    private long startedAt;
    private String latestRaw = "";

    @Override
    public void onCreate() {
        super.onCreate();
        store = new SettingsStore(this);
        createNotificationChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? ACTION_START : intent.getAction();
        if (ACTION_STOP.equals(action)) requestStop();
        else startSession();
        return START_NOT_STICKY;
    }

    private void startSession() {
        if (recording) return;
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            fail("请先授予麦克风权限。", null);
            return;
        }

        settings = store.load();
        if (!settings.hasTencentCredentials()) {
            fail("请先打开应用，填写腾讯 AppID、SecretID 和 SecretKey。", null);
            return;
        }

        int minimum = AudioRecord.getMinBufferSize(
            SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        if (minimum <= 0) {
            fail("当前手机无法创建 16kHz 麦克风录音。", null);
            return;
        }

        try {
            audioRecord = new AudioRecord(
                MediaRecorder.AudioSource.VOICE_RECOGNITION,
                SAMPLE_RATE,
                AudioFormat.CHANNEL_IN_MONO,
                AudioFormat.ENCODING_PCM_16BIT,
                Math.max(minimum * 2, FRAME_SAMPLES * 8));
            if (audioRecord.getState() != AudioRecord.STATE_INITIALIZED) {
                throw new IllegalStateException("麦克风初始化失败");
            }

            speechGate = new SpeechGate();
            synchronized (audioGate) {
                pendingAudio.clear();
                speechDetected = false;
                asrReady = false;
                finishWhenReady = false;
            }
            latestRaw = "";
            startedAt = SystemClock.elapsedRealtime();
            recording = true;
            store.setRecording(true);
            requestTileRefresh();
            ServiceCompat.startForeground(
                this,
                NOTIFICATION_ID,
                notification("请在 5 秒内开始说话", "等待语音，暂未连接腾讯 ASR", true),
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);
            broadcast("waiting", "5 秒内没有检测到语音会自动关闭。", "", "", 0);
            audioRecord.startRecording();
            audioThread = new Thread(this::recordLoop, "sushuo-audio");
            audioThread.start();
        } catch (Exception ex) {
            fail("无法启动麦克风：" + ex.getMessage(), ex);
        }
    }

    private void recordLoop() {
        short[] samples = new short[FRAME_SAMPLES];
        try {
            while (recording) {
                int count = audioRecord.read(samples, 0, samples.length, AudioRecord.READ_BLOCKING);
                if (count <= 0) continue;
                int level = SpeechGate.levelPercent(samples, count);
                byte[] pcm = shortsToBytes(samples, count);
                boolean beginAsr = false;
                TencentAsrClient readyClient = null;

                synchronized (audioGate) {
                    if (!recording) break;
                    if (asrReady) {
                        readyClient = asrClient;
                    } else {
                        pendingAudio.add(pcm);
                        int limit = speechDetected ? CONNECTING_FRAME_LIMIT : PRELUDE_FRAME_LIMIT;
                        while (pendingAudio.size() > limit) pendingAudio.remove();
                        if (!speechDetected && speechGate.accept(samples, count)) {
                            speechDetected = true;
                            beginAsr = true;
                        }
                    }
                }

                if (readyClient != null) readyClient.sendAudio(pcm);
                broadcast(null, null, null, null, level);
                if (beginAsr) mainHandler.post(this::connectAsr);

                if (!speechDetected && SystemClock.elapsedRealtime() - startedAt >= SpeechGate.NO_SPEECH_TIMEOUT_MS) {
                    mainHandler.post(this::cancelForNoSpeech);
                    return;
                }
            }
        } catch (Exception ex) {
            if (recording) mainHandler.post(() -> fail("录音过程中出现错误：" + ex.getMessage(), ex));
        } finally {
            releaseAudioRecord();
        }
    }

    private void connectAsr() {
        if (!recording || asrClient != null) return;
        broadcast("connecting", "检测到语音，正在连接腾讯 ASR；开头声音已临时缓冲。", null, null, -1);
        updateNotification("连接中", "正在连接腾讯实时语音", true);
        asrClient = new TencentAsrClient(this);
        asrClient.connect(settings);
    }

    private void requestStop() {
        if (!recording) return;
        recording = false;
        store.setRecording(false);
        requestTileRefresh();
        stopAudioRecord();

        if (!speechDetected) {
            finishWithoutText("已取消", "没有检测到有效语音。", false);
            return;
        }

        broadcast("transcribing", "正在完成转写和 AI 整理…", null, null, 0);
        updateNotification("转写中", "正在完成文字", false);
        synchronized (audioGate) {
            if (asrReady && asrClient != null) asrClient.finish();
            else finishWhenReady = true;
        }
    }

    private void cancelForNoSpeech() {
        if (!recording || speechDetected) return;
        recording = false;
        store.setRecording(false);
        requestTileRefresh();
        stopAudioRecord();
        finishWithoutText("已自动关闭", "5 秒内没有检测到语音；未连接腾讯 ASR。", true);
    }

    private void finishWithoutText(String title, String detail, boolean showNotice) {
        synchronized (audioGate) {
            pendingAudio.clear();
        }
        broadcast("cancelled", detail, "", "", 0);
        if (showNotice) {
            updateNotification(title, detail, false);
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_DETACH);
            mainHandler.postDelayed(() -> {
                getSystemService(NotificationManager.class).cancel(NOTIFICATION_ID);
                stopSelf();
            }, 2_000);
        } else {
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE);
            stopSelf();
        }
    }

    @Override
    public void onReady() {
        synchronized (audioGate) {
            if (asrClient == null) return;
            while (!pendingAudio.isEmpty()) asrClient.sendAudio(pendingAudio.remove());
            asrReady = true;
            if (finishWhenReady) asrClient.finish();
        }
        if (recording) {
            broadcast("listening", "正在听你说；完成后再次点击结束。", null, null, -1);
            updateNotification("请输入语音", "点击通知中的“结束”完成转写", true);
        }
    }

    @Override
    public void onTranscript(String text) {
        latestRaw = text;
        broadcast(null, null, text, null, -1);
    }

    @Override
    public void onCompleted(String text) {
        latestRaw = text == null ? "" : text.trim();
        if (latestRaw.isBlank()) {
            fail("腾讯没有识别到有效语音，请连续说一句完整的话。", null);
            return;
        }

        textExecutor.execute(() -> {
            String finalText = latestRaw;
            String warning = "";
            try {
                finalText = new DeepSeekClient().refine(latestRaw, settings, store.recentHistory());
            } catch (Exception ex) {
                warning = ex.getMessage() == null ? "AI 整理失败，已保留 ASR 原文。" : ex.getMessage();
            }
            String completedText = finalText;
            String completedWarning = warning;
            mainHandler.post(() -> completeText(completedText, completedWarning));
        });
    }

    private void completeText(String finalText, String warning) {
        store.addHistory(finalText);
        ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        clipboard.setPrimaryClip(ClipData.newPlainText("速说速记X", finalText));
        String detail = warning.isBlank() ? "文字已经复制，返回输入框粘贴即可。" : warning + " 文字已复制。";
        broadcast("complete", detail, latestRaw, finalText, 0);
        updateNotification("转写完成，文字已复制", detail, false);
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_DETACH);
        mainHandler.postDelayed(() -> {
            getSystemService(NotificationManager.class).cancel(NOTIFICATION_ID);
            stopSelf();
        }, 4_000);
    }

    @Override
    public void onError(String message) {
        mainHandler.post(() -> fail(message, null));
    }

    private void fail(String message, Throwable ignored) {
        recording = false;
        store.setRecording(false);
        requestTileRefresh();
        stopAudioRecord();
        if (asrClient != null) asrClient.cancel();
        synchronized (audioGate) {
            pendingAudio.clear();
        }
        broadcast("error", message, latestRaw, "", 0);
        try {
            updateNotification("未完成", message, false);
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_DETACH);
            mainHandler.postDelayed(() -> {
                getSystemService(NotificationManager.class).cancel(NOTIFICATION_ID);
                stopSelf();
            }, 4_000);
        } catch (Exception ex) {
            stopSelf();
        }
    }

    private void stopAudioRecord() {
        AudioRecord record = audioRecord;
        if (record != null) {
            try {
                if (record.getRecordingState() == AudioRecord.RECORDSTATE_RECORDING) record.stop();
            } catch (Exception ignored) {
            }
        }
    }

    private void releaseAudioRecord() {
        AudioRecord record = audioRecord;
        audioRecord = null;
        if (record != null) {
            try { record.release(); } catch (Exception ignored) { }
        }
    }

    private static byte[] shortsToBytes(short[] samples, int count) {
        ByteBuffer buffer = ByteBuffer.allocate(count * 2).order(ByteOrder.LITTLE_ENDIAN);
        for (int i = 0; i < count; i++) buffer.putShort(samples[i]);
        return buffer.array();
    }

    private void broadcast(String state, String detail, String raw, String finalText, int level) {
        Intent intent = new Intent(ACTION_STATE).setPackage(getPackageName());
        if (state != null) intent.putExtra(EXTRA_STATE, state);
        if (detail != null) intent.putExtra(EXTRA_DETAIL, detail);
        if (raw != null) intent.putExtra(EXTRA_RAW, raw);
        if (finalText != null) intent.putExtra(EXTRA_FINAL, finalText);
        if (level >= 0) intent.putExtra(EXTRA_LEVEL, level);
        sendBroadcast(intent);
    }

    private Notification notification(String title, String detail, boolean includeStop) {
        Intent openIntent = new Intent(this, MainActivity.class);
        PendingIntent openPending = PendingIntent.getActivity(
            this, 10, openIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        NotificationCompat.Builder builder = new NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_mic_tile)
            .setContentTitle(title)
            .setContentText(detail)
            .setContentIntent(openPending)
            .setOngoing(includeStop)
            .setOnlyAlertOnce(true)
            .setPriority(NotificationCompat.PRIORITY_LOW);
        if (includeStop) {
            Intent stopIntent = new Intent(this, VoiceRecorderService.class).setAction(ACTION_STOP);
            PendingIntent stopPending = PendingIntent.getService(
                this, 11, stopIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            builder.addAction(0, "结束", stopPending);
        }
        return builder.build();
    }

    private void updateNotification(String title, String detail, boolean includeStop) {
        getSystemService(NotificationManager.class).notify(
            NOTIFICATION_ID, notification(title, detail, includeStop));
    }

    private void createNotificationChannel() {
        NotificationChannel channel = new NotificationChannel(
            CHANNEL_ID, getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW);
        channel.setDescription(getString(R.string.notification_channel_description));
        getSystemService(NotificationManager.class).createNotificationChannel(channel);
    }

    private void requestTileRefresh() {
        TileService.requestListeningState(this, new ComponentName(this, QuickSettingsTileService.class));
    }

    @Override
    public void onDestroy() {
        recording = false;
        store.setRecording(false);
        stopAudioRecord();
        if (asrClient != null) asrClient.cancel();
        textExecutor.shutdownNow();
        super.onDestroy();
    }

    @Nullable
    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
