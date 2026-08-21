package com.sushuosujix.app;

import android.net.Uri;

import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.TreeMap;
import java.util.UUID;
import java.util.concurrent.TimeUnit;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;
import okhttp3.WebSocket;
import okhttp3.WebSocketListener;
import okio.ByteString;

final class TencentAsrClient {
    interface Listener {
        void onReady();
        void onTranscript(String text);
        void onCompleted(String text);
        void onError(String message);
    }

    private static final OkHttpClient HTTP = new OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .pingInterval(15, TimeUnit.SECONDS)
        .build();

    private final Listener listener;
    private final TreeMap<Integer, String> settled = new TreeMap<>();
    private WebSocket socket;
    private int partialIndex = -1;
    private String partialText = "";
    private boolean ready;
    private boolean finishing;
    private boolean cancelled;

    TencentAsrClient(Listener listener) {
        this.listener = listener;
    }

    void connect(AppSettings settings) {
        try {
            String url = buildSignedUrl(settings, UUID.randomUUID().toString(), System.currentTimeMillis() / 1000L);
            socket = HTTP.newWebSocket(new Request.Builder().url(url).build(), new WebSocketListener() {
                @Override
                public void onOpen(WebSocket webSocket, Response response) {
                    // Tencent sends a JSON acknowledgement before accepting audio.
                }

                @Override
                public void onMessage(WebSocket webSocket, String text) {
                    handleMessage(text);
                }

                @Override
                public void onFailure(WebSocket webSocket, Throwable throwable, Response response) {
                    if (!cancelled) listener.onError(friendlyNetworkError(throwable));
                }
            });
        } catch (Exception ex) {
            listener.onError("腾讯 ASR 签名失败：" + ex.getMessage());
        }
    }

    synchronized boolean isReady() {
        return ready;
    }

    synchronized void sendAudio(byte[] pcm) {
        if (!cancelled && !finishing && ready && socket != null) {
            socket.send(ByteString.of(pcm));
        }
    }

    synchronized void finish() {
        if (cancelled || finishing) return;
        finishing = true;
        if (ready && socket != null) socket.send("{\"type\":\"end\"}");
    }

    synchronized void cancel() {
        cancelled = true;
        if (socket != null) socket.cancel();
    }

    private synchronized void handleMessage(String jsonText) {
        if (cancelled) return;
        try {
            JSONObject response = new JSONObject(jsonText);
            int code = response.optInt("code", -1);
            if (code != 0) {
                listener.onError("腾讯 ASR 返回错误 " + code + "：" + response.optString("message"));
                return;
            }

            if (!ready) {
                ready = true;
                listener.onReady();
            }

            JSONObject result = response.optJSONObject("result");
            if (result != null) {
                int index = result.optInt("index", 0);
                int sliceType = result.optInt("slice_type", 0);
                String text = result.optString("voice_text_str", "");
                if (sliceType == 2) {
                    settled.put(index, text);
                    if (partialIndex == index) {
                        partialIndex = -1;
                        partialText = "";
                    }
                } else {
                    partialIndex = index;
                    partialText = text;
                }
                listener.onTranscript(buildTranscript());
            }

            if (response.optInt("final", 0) == 1) {
                listener.onCompleted(buildTranscript().trim());
                if (socket != null) socket.close(1000, "done");
            }
        } catch (Exception ex) {
            listener.onError("无法解析腾讯 ASR 返回内容：" + ex.getMessage());
        }
    }

    private String buildTranscript() {
        StringBuilder builder = new StringBuilder();
        for (Map.Entry<Integer, String> item : settled.entrySet()) builder.append(item.getValue());
        if (partialIndex >= 0 && !settled.containsKey(partialIndex)) builder.append(partialText);
        return builder.toString();
    }

    static String buildSignedUrl(AppSettings settings, String voiceId, long timestamp) throws Exception {
        TreeMap<String, String> parameters = new TreeMap<>();
        parameters.put("convert_num_mode", "1");
        parameters.put("engine_model_type", settings.tencentEngine);
        parameters.put("expired", Long.toString(timestamp + 24 * 60 * 60));
        parameters.put("filter_dirty", "0");
        parameters.put("filter_empty_result", "1");
        parameters.put("filter_modal", "0");
        parameters.put("filter_punc", "0");
        parameters.put("max_speak_time", "0");
        parameters.put("needvad", "1");
        parameters.put("nonce", Long.toString(timestamp));
        parameters.put("reinforce_hotword", "0");
        parameters.put("secretid", settings.tencentSecretId);
        parameters.put("timestamp", Long.toString(timestamp));
        parameters.put("voice_format", "1");
        parameters.put("voice_id", voiceId);
        parameters.put("word_info", "0");

        StringBuilder query = new StringBuilder();
        for (Map.Entry<String, String> item : parameters.entrySet()) {
            if (query.length() > 0) query.append('&');
            query.append(item.getKey()).append('=').append(item.getValue());
        }
        String canonical = "asr.cloud.tencent.com/asr/v2/" + settings.tencentAppId + "?" + query;
        Mac mac = Mac.getInstance("HmacSHA1");
        mac.init(new SecretKeySpec(settings.tencentSecretKey.getBytes(StandardCharsets.UTF_8), "HmacSHA1"));
        String signature = android.util.Base64.encodeToString(
            mac.doFinal(canonical.getBytes(StandardCharsets.UTF_8)), android.util.Base64.NO_WRAP);
        return "wss://" + canonical + "&signature=" + Uri.encode(signature);
    }

    private static String friendlyNetworkError(Throwable throwable) {
        String message = throwable.getMessage();
        if (message == null) message = throwable.getClass().getSimpleName();
        return "腾讯 ASR 连接失败：" + message;
    }
}
