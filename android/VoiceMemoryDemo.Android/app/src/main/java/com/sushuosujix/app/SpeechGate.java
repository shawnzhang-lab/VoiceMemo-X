package com.sushuosujix.app;

final class SpeechGate {
    static final long NO_SPEECH_TIMEOUT_MS = 5_000;
    private static final double SPEECH_LEVEL = 0.015;
    private static final int REQUIRED_FRAMES = 3;
    private int consecutiveFrames;

    boolean accept(short[] samples, int count) {
        if (count <= 0) return false;
        double sum = 0;
        for (int i = 0; i < count; i++) {
            double normalized = samples[i] / 32768.0;
            sum += normalized * normalized;
        }
        double rms = Math.sqrt(sum / count);
        consecutiveFrames = rms >= SPEECH_LEVEL ? consecutiveFrames + 1 : 0;
        return consecutiveFrames >= REQUIRED_FRAMES;
    }

    static int levelPercent(short[] samples, int count) {
        if (count <= 0) return 0;
        double sum = 0;
        for (int i = 0; i < count; i++) {
            double normalized = samples[i] / 32768.0;
            sum += normalized * normalized;
        }
        return (int) Math.min(100, Math.sqrt(sum / count) * 400);
    }
}
