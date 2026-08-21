package com.sushuosujix.app;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Bundle;

import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

public final class QuickStartActivity extends AppCompatActivity {
    private boolean launched;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (launched) return;
        launched = true;
        SettingsStore store = new SettingsStore(this);
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED ||
            !store.load().hasTencentCredentials()) {
            startActivity(new Intent(this, MainActivity.class));
        } else {
            ContextCompat.startForegroundService(this,
                new Intent(this, VoiceRecorderService.class).setAction(VoiceRecorderService.ACTION_START));
        }
        finish();
    }
}
