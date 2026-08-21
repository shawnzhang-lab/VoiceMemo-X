package com.sushuosujix.app;

final class AppSettings {
    String tencentAppId = "";
    String tencentSecretId = "";
    String tencentSecretKey = "";
    String tencentEngine = "16k_zh";
    String deepSeekApiKey = "";
    String deepSeekBaseUrl = "https://api.deepseek.com";
    String deepSeekModel = "deepseek-v4-flash";
    boolean enableAi = true;
    boolean enableTranslation = false;
    String targetLanguage = "en";

    boolean hasTencentCredentials() {
        return !tencentAppId.isBlank() && !tencentSecretId.isBlank() && !tencentSecretKey.isBlank();
    }

    boolean needsDeepSeek() {
        return enableAi || enableTranslation;
    }
}
