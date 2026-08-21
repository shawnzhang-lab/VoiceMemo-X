package com.sushuosujix.app;

import android.Manifest;
import android.app.StatusBarManager;
import android.content.BroadcastReceiver;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.graphics.drawable.Icon;
import android.os.Build;
import android.os.Bundle;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ProgressBar;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

import java.util.ArrayList;
import java.util.List;

public final class MainActivity extends AppCompatActivity {
    private static final String[] LANGUAGE_LABELS = {
        "英语 English", "日语 日本語", "韩语 한국어", "法语 Français",
        "德语 Deutsch", "西班牙语 Español", "俄语 Русский"
    };
    private static final String[] LANGUAGE_CODES = {"en", "ja", "ko", "fr", "de", "es", "ru"};

    private SettingsStore store;
    private TextView statusTitle;
    private TextView statusDetail;
    private TextView rawText;
    private TextView finalText;
    private ProgressBar audioLevel;
    private Button toggleButton;
    private EditText tencentAppId;
    private EditText tencentSecretId;
    private EditText tencentSecretKey;
    private EditText tencentEngine;
    private EditText deepSeekKey;
    private EditText deepSeekModel;
    private Switch enableAi;
    private Switch enableTranslation;
    private Spinner targetLanguage;

    private final ActivityResultLauncher<String[]> permissionLauncher = registerForActivityResult(
        new ActivityResultContracts.RequestMultiplePermissions(), result -> {
            if (Boolean.TRUE.equals(result.get(Manifest.permission.RECORD_AUDIO))) startRecording();
            else Toast.makeText(this, "需要麦克风权限才能进行语音输入。", Toast.LENGTH_LONG).show();
        });

    private final BroadcastReceiver stateReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            String state = intent.getStringExtra(VoiceRecorderService.EXTRA_STATE);
            String detail = intent.getStringExtra(VoiceRecorderService.EXTRA_DETAIL);
            String raw = intent.getStringExtra(VoiceRecorderService.EXTRA_RAW);
            String completed = intent.getStringExtra(VoiceRecorderService.EXTRA_FINAL);
            if (intent.hasExtra(VoiceRecorderService.EXTRA_LEVEL)) {
                audioLevel.setProgress(intent.getIntExtra(VoiceRecorderService.EXTRA_LEVEL, 0));
            }
            if (raw != null) rawText.setText(raw);
            if (completed != null) finalText.setText(completed);
            if (state != null) applyState(state, detail == null ? "" : detail);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        store = new SettingsStore(this);
        bindViews();
        setupLanguages();
        populateSettings(store.load());

        toggleButton.setOnClickListener(view -> {
            if (store.isRecording()) stopRecording();
            else ensurePermissionsAndStart();
        });
        findViewById(R.id.saveButton).setOnClickListener(view -> saveSettings(true));
        findViewById(R.id.copyButton).setOnClickListener(view -> copyResult());
        findViewById(R.id.addTileButton).setOnClickListener(view -> addQuickTile());
        applyState(store.isRecording() ? "listening" : "idle",
            store.isRecording() ? "语音输入正在运行。" : "点击开始后，请在 5 秒内开始说话。");
    }

    private void bindViews() {
        statusTitle = findViewById(R.id.statusTitle);
        statusDetail = findViewById(R.id.statusDetail);
        rawText = findViewById(R.id.rawText);
        finalText = findViewById(R.id.finalText);
        audioLevel = findViewById(R.id.audioLevel);
        toggleButton = findViewById(R.id.toggleButton);
        tencentAppId = findViewById(R.id.tencentAppId);
        tencentSecretId = findViewById(R.id.tencentSecretId);
        tencentSecretKey = findViewById(R.id.tencentSecretKey);
        tencentEngine = findViewById(R.id.tencentEngine);
        deepSeekKey = findViewById(R.id.deepSeekKey);
        deepSeekModel = findViewById(R.id.deepSeekModel);
        enableAi = findViewById(R.id.enableAi);
        enableTranslation = findViewById(R.id.enableTranslation);
        targetLanguage = findViewById(R.id.targetLanguage);
    }

    private void setupLanguages() {
        ArrayAdapter<String> adapter = new ArrayAdapter<>(
            this, android.R.layout.simple_spinner_dropdown_item, LANGUAGE_LABELS);
        targetLanguage.setAdapter(adapter);
    }

    private void populateSettings(AppSettings value) {
        tencentAppId.setText(value.tencentAppId);
        tencentSecretId.setText(value.tencentSecretId);
        tencentSecretKey.setText(value.tencentSecretKey);
        tencentEngine.setText(value.tencentEngine);
        deepSeekKey.setText(value.deepSeekApiKey);
        deepSeekModel.setText(value.deepSeekModel);
        enableAi.setChecked(value.enableAi);
        enableTranslation.setChecked(value.enableTranslation);
        for (int i = 0; i < LANGUAGE_CODES.length; i++) {
            if (LANGUAGE_CODES[i].equals(value.targetLanguage)) targetLanguage.setSelection(i);
        }
    }

    private AppSettings collectSettings() {
        AppSettings value = new AppSettings();
        value.tencentAppId = tencentAppId.getText().toString().trim();
        value.tencentSecretId = tencentSecretId.getText().toString().trim();
        value.tencentSecretKey = tencentSecretKey.getText().toString().trim();
        value.tencentEngine = tencentEngine.getText().toString().trim();
        value.deepSeekApiKey = deepSeekKey.getText().toString().trim();
        value.deepSeekModel = deepSeekModel.getText().toString().trim();
        value.enableAi = enableAi.isChecked();
        value.enableTranslation = enableTranslation.isChecked();
        value.targetLanguage = LANGUAGE_CODES[targetLanguage.getSelectedItemPosition()];
        return value;
    }

    private void saveSettings(boolean notify) {
        AppSettings value = collectSettings();
        if (value.tencentEngine.isBlank()) value.tencentEngine = "16k_zh";
        if (value.deepSeekModel.isBlank()) value.deepSeekModel = "deepseek-v4-flash";
        store.save(value);
        if (notify) Toast.makeText(this, "设置已安全保存。", Toast.LENGTH_SHORT).show();
    }

    private void ensurePermissionsAndStart() {
        saveSettings(false);
        AppSettings value = store.load();
        if (!value.hasTencentCredentials()) {
            Toast.makeText(this, "请先填写并保存腾讯 ASR 配置。", Toast.LENGTH_LONG).show();
            return;
        }

        List<String> missing = new ArrayList<>();
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            missing.add(Manifest.permission.RECORD_AUDIO);
        }
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            missing.add(Manifest.permission.POST_NOTIFICATIONS);
        }
        if (missing.isEmpty()) startRecording();
        else permissionLauncher.launch(missing.toArray(new String[0]));
    }

    private void startRecording() {
        ContextCompat.startForegroundService(this,
            new Intent(this, VoiceRecorderService.class).setAction(VoiceRecorderService.ACTION_START));
    }

    private void stopRecording() {
        startService(new Intent(this, VoiceRecorderService.class).setAction(VoiceRecorderService.ACTION_STOP));
    }

    private void copyResult() {
        String text = finalText.getText().toString();
        if (text.isBlank()) text = rawText.getText().toString();
        if (text.isBlank()) {
            Toast.makeText(this, "目前还没有可复制的结果。", Toast.LENGTH_SHORT).show();
            return;
        }
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        clipboard.setPrimaryClip(ClipData.newPlainText("速说速记X", text));
        Toast.makeText(this, "文字已复制。", Toast.LENGTH_SHORT).show();
    }

    private void addQuickTile() {
        if (Build.VERSION.SDK_INT >= 33) {
            StatusBarManager manager = getSystemService(StatusBarManager.class);
            manager.requestAddTileService(
                new ComponentName(this, QuickSettingsTileService.class),
                getString(R.string.tile_label),
                Icon.createWithResource(this, R.drawable.ic_mic_tile),
                getMainExecutor(), result -> Toast.makeText(
                    this,
                    result == StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ADDED
                        ? "快捷开关已添加。" : "如果没有添加，请手动编辑通知栏快捷开关。",
                    Toast.LENGTH_LONG).show());
        } else {
            Toast.makeText(this, "下拉通知栏并点击编辑，把“速说速记X”拖到快捷开关区域。", Toast.LENGTH_LONG).show();
        }
    }

    private void applyState(String state, String detail) {
        String title;
        boolean active;
        switch (state) {
            case "waiting" -> { title = "请在 5 秒内开始说话"; active = true; }
            case "connecting" -> { title = "连接中"; active = true; }
            case "listening" -> { title = "请输入语音"; active = true; }
            case "transcribing" -> { title = "转写中"; active = false; }
            case "complete" -> { title = "转写完成，文字已复制"; active = false; }
            case "cancelled" -> { title = "已自动关闭"; active = false; }
            case "error" -> { title = "这次没有完成"; active = false; }
            default -> { title = "准备就绪"; active = false; }
        }
        statusTitle.setText(title);
        statusDetail.setText(detail);
        toggleButton.setText(active ? "结束并转写" : "开始语音输入");
        if (!active) audioLevel.setProgress(0);
    }

    @Override
    protected void onStart() {
        super.onStart();
        IntentFilter filter = new IntentFilter(VoiceRecorderService.ACTION_STATE);
        ContextCompat.registerReceiver(
            this, stateReceiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED);
    }

    @Override
    protected void onStop() {
        unregisterReceiver(stateReceiver);
        super.onStop();
    }
}
