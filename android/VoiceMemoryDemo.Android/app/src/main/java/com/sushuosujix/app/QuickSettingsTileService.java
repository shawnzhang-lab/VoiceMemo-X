package com.sushuosujix.app;

import android.annotation.SuppressLint;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import android.service.quicksettings.Tile;
import android.service.quicksettings.TileService;

public final class QuickSettingsTileService extends TileService {
    @Override
    public void onStartListening() {
        super.onStartListening();
        boolean recording = new SettingsStore(this).isRecording();
        Tile tile = getQsTile();
        if (tile == null) return;
        tile.setState(recording ? Tile.STATE_ACTIVE : Tile.STATE_INACTIVE);
        if (Build.VERSION.SDK_INT >= 29) {
            tile.setSubtitle(recording ? "再次点击结束" : "点击开始");
        }
        tile.updateTile();
    }

    @SuppressLint("StartActivityAndCollapseDeprecated")
    @Override
    public void onClick() {
        super.onClick();
        SettingsStore store = new SettingsStore(this);
        if (store.isRecording()) {
            startService(new Intent(this, VoiceRecorderService.class).setAction(VoiceRecorderService.ACTION_STOP));
            return;
        }

        Intent intent = new Intent(this, QuickStartActivity.class)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        if (Build.VERSION.SDK_INT >= 34) {
            PendingIntent pending = PendingIntent.getActivity(
                this, 21, intent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            startActivityAndCollapse(pending);
        } else {
            startActivityAndCollapse(intent);
        }
    }
}
