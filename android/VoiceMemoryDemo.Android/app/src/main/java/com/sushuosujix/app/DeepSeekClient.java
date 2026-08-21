package com.sushuosujix.app;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.List;
import java.util.concurrent.TimeUnit;

import okhttp3.MediaType;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.RequestBody;
import okhttp3.Response;

final class DeepSeekClient {
    private static final MediaType JSON = MediaType.get("application/json; charset=utf-8");
    private static final OkHttpClient HTTP = new OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(25, TimeUnit.SECONDS)
        .build();

    String refine(String rawText, AppSettings settings, List<String> recentHistory) throws Exception {
        if (!settings.needsDeepSeek()) return rawText;
        if (settings.deepSeekApiKey.isBlank()) {
            throw new IllegalStateException("尚未填写 DeepSeek API Key，已保留 ASR 原文。");
        }

        String target = languageName(settings.targetLanguage);
        String systemPrompt = settings.enableTranslation
            ? "你是语音输入法的翻译与整理引擎。先修正中文转写中的明显错字、口头禅、重复、断句和标点，再翻译为自然地道的" + target + "。忠实保留原意、事实、语气、称呼、数字和格式；不要回答用户，不新增信息；最终只输出" + target + "正文。"
            : "你是语音输入法的文本整理引擎。不要回答用户，只把转写文本整理成可直接发送的正文。保留原意、事实和语气；删除无意义口头禅与重复，修正明显错字、断句和标点；不要增加标题、说明或 Markdown。";

        StringBuilder examples = new StringBuilder();
        for (int i = 0; i < Math.min(5, recentHistory.size()); i++) {
            examples.append(i + 1).append(". ").append(recentHistory.get(i)).append('\n');
        }
        String userPrompt = "最近成文示例（只参考表达习惯，不照抄）：\n" +
            (examples.length() == 0 ? "暂无\n" : examples) +
            "\n待处理转写：\n" + rawText;

        JSONArray messages = new JSONArray()
            .put(new JSONObject().put("role", "system").put("content", systemPrompt))
            .put(new JSONObject().put("role", "user").put("content", userPrompt));
        JSONObject payload = new JSONObject()
            .put("model", settings.deepSeekModel.isBlank() ? "deepseek-v4-flash" : settings.deepSeekModel)
            .put("messages", messages)
            .put("thinking", new JSONObject().put("type", "disabled"))
            .put("max_tokens", 2048)
            .put("stream", false);

        String endpoint = settings.deepSeekBaseUrl.replaceAll("/+$", "") + "/chat/completions";
        Request request = new Request.Builder()
            .url(endpoint)
            .header("Authorization", "Bearer " + settings.deepSeekApiKey)
            .post(RequestBody.create(payload.toString(), JSON))
            .build();
        try (Response response = HTTP.newCall(request).execute()) {
            String body = response.body() == null ? "" : response.body().string();
            if (!response.isSuccessful()) {
                throw new IllegalStateException("DeepSeek 整理失败（HTTP " + response.code() + "），已保留 ASR 原文。");
            }
            String text = new JSONObject(body).getJSONArray("choices").getJSONObject(0)
                .getJSONObject("message").optString("content", "").trim();
            return text.isBlank() ? rawText : text;
        }
    }

    private static String languageName(String code) {
        return switch (code) {
            case "ja" -> "日语";
            case "ko" -> "韩语";
            case "fr" -> "法语";
            case "de" -> "德语";
            case "es" -> "西班牙语";
            case "ru" -> "俄语";
            default -> "英语";
        };
    }
}
