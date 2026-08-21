package com.sushuosujix.app;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import org.json.JSONArray;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.ArrayList;
import java.util.List;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

final class SettingsStore {
    private static final String PREFERENCES = "sushuo_secure_settings";
    private static final String KEY_ALIAS = "sushuo_api_key_v1";
    private final SharedPreferences preferences;

    SettingsStore(Context context) {
        preferences = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }

    AppSettings load() {
        AppSettings value = new AppSettings();
        value.tencentAppId = preferences.getString("tencent_app_id", "");
        value.tencentSecretId = decrypt(preferences.getString("tencent_secret_id", ""));
        value.tencentSecretKey = decrypt(preferences.getString("tencent_secret_key", ""));
        value.tencentEngine = preferences.getString("tencent_engine", "16k_zh");
        value.deepSeekApiKey = decrypt(preferences.getString("deepseek_key", ""));
        value.deepSeekBaseUrl = preferences.getString("deepseek_base_url", "https://api.deepseek.com");
        value.deepSeekModel = preferences.getString("deepseek_model", "deepseek-v4-flash");
        value.enableAi = preferences.getBoolean("enable_ai", true);
        value.enableTranslation = preferences.getBoolean("enable_translation", false);
        value.targetLanguage = preferences.getString("target_language", "en");
        return value;
    }

    void save(AppSettings value) {
        preferences.edit()
            .putString("tencent_app_id", value.tencentAppId)
            .putString("tencent_secret_id", encrypt(value.tencentSecretId))
            .putString("tencent_secret_key", encrypt(value.tencentSecretKey))
            .putString("tencent_engine", value.tencentEngine)
            .putString("deepseek_key", encrypt(value.deepSeekApiKey))
            .putString("deepseek_base_url", value.deepSeekBaseUrl)
            .putString("deepseek_model", value.deepSeekModel)
            .putBoolean("enable_ai", value.enableAi)
            .putBoolean("enable_translation", value.enableTranslation)
            .putString("target_language", value.targetLanguage)
            .apply();
    }

    boolean isRecording() {
        return preferences.getBoolean("is_recording", false);
    }

    void setRecording(boolean value) {
        preferences.edit().putBoolean("is_recording", value).apply();
    }

    List<String> recentHistory() {
        List<String> result = new ArrayList<>();
        try {
            String raw = decrypt(preferences.getString("recent_history", ""));
            if (raw.isBlank()) return result;
            JSONArray array = new JSONArray(raw);
            for (int i = 0; i < array.length(); i++) result.add(array.optString(i));
        } catch (Exception ignored) {
        }
        return result;
    }

    void addHistory(String text) {
        if (text == null || text.isBlank()) return;
        List<String> history = recentHistory();
        history.add(0, text.trim());
        while (history.size() > 20) history.remove(history.size() - 1);
        preferences.edit().putString("recent_history", encrypt(new JSONArray(history).toString())).apply();
    }

    private String encrypt(String plaintext) {
        if (plaintext == null || plaintext.isEmpty()) return "";
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey());
            byte[] encrypted = cipher.doFinal(plaintext.getBytes(StandardCharsets.UTF_8));
            byte[] iv = cipher.getIV();
            byte[] combined = new byte[1 + iv.length + encrypted.length];
            combined[0] = (byte) iv.length;
            System.arraycopy(iv, 0, combined, 1, iv.length);
            System.arraycopy(encrypted, 0, combined, 1 + iv.length, encrypted.length);
            return Base64.encodeToString(combined, Base64.NO_WRAP);
        } catch (Exception ex) {
            throw new IllegalStateException("无法使用 Android 系统密钥库保存设置。", ex);
        }
    }

    private String decrypt(String encoded) {
        if (encoded == null || encoded.isEmpty()) return "";
        try {
            byte[] combined = Base64.decode(encoded, Base64.NO_WRAP);
            int ivLength = combined[0] & 0xff;
            byte[] iv = new byte[ivLength];
            byte[] encrypted = new byte[combined.length - 1 - ivLength];
            System.arraycopy(combined, 1, iv, 0, ivLength);
            System.arraycopy(combined, 1 + ivLength, encrypted, 0, encrypted.length);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), new GCMParameterSpec(128, iv));
            return new String(cipher.doFinal(encrypted), StandardCharsets.UTF_8);
        } catch (Exception ignored) {
            return "";
        }
    }

    private SecretKey getOrCreateKey() throws Exception {
        KeyStore keyStore = KeyStore.getInstance("AndroidKeyStore");
        keyStore.load(null);
        if (keyStore.containsAlias(KEY_ALIAS)) {
            return (SecretKey) keyStore.getKey(KEY_ALIAS, null);
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(
            KEY_ALIAS,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .build());
        return generator.generateKey();
    }
}
