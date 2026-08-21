package com.sushuosujix.app;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class SpeechGateTest {
    @Test
    public void silenceNeverOpensGate() {
        SpeechGate gate = new SpeechGate();
        short[] silence = new short[640];
        for (int i = 0; i < 150; i++) assertFalse(gate.accept(silence, silence.length));
    }

    @Test
    public void sustainedSpeechOpensAfterThreeFrames() {
        SpeechGate gate = new SpeechGate();
        short[] speech = new short[640];
        for (int i = 0; i < speech.length; i++) speech[i] = 2_000;
        assertFalse(gate.accept(speech, speech.length));
        assertFalse(gate.accept(speech, speech.length));
        assertTrue(gate.accept(speech, speech.length));
    }

    @Test
    public void shortNoiseSpikeDoesNotOpenGate() {
        SpeechGate gate = new SpeechGate();
        short[] speech = new short[640];
        short[] silence = new short[640];
        for (int i = 0; i < speech.length; i++) speech[i] = 2_000;
        assertFalse(gate.accept(speech, speech.length));
        assertFalse(gate.accept(silence, silence.length));
        assertFalse(gate.accept(speech, speech.length));
        assertFalse(gate.accept(speech, speech.length));
        assertTrue(gate.accept(speech, speech.length));
    }
}
