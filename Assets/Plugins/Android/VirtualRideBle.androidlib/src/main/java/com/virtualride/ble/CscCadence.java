package com.virtualride.ble;

/** Pure CSC decoder; caller supplies monotonic receipt times in milliseconds. */
final class CscCadence {
    static final long TIMEOUT_MS = 2500;
    private boolean baseline;
    private int revolutions;
    private int eventTime;
    private long baselineAt;
    private long lastPacketAt = -1;
    private long lastProgressAt = -1;
    private double rpm;

    void reset() {
        baseline = false;
        lastPacketAt = lastProgressAt = -1;
        rpm = 0;
    }

    boolean accept(byte[] value, long now) {
        if (value == null || value.length < 1) return false;
        int flags = value[0] & 0xff;
        int offset = (flags & 1) != 0 ? 7 : 1; // Skip uint32 wheel count + uint16 time.
        if ((flags & 2) == 0 || (flags & 0xfc) != 0 || value.length < offset + 4) return false;
        int nextRevolutions = u16(value, offset);
        int nextTime = u16(value, offset + 2);
        // A long gap makes the 64-second event-clock wrap ambiguous. Re-baseline
        // already at the safety timeout so resumed input never uses stale deltas.
        if (!baseline || now - lastPacketAt >= TIMEOUT_MS || now < lastPacketAt) {
            baseline = true;
            revolutions = nextRevolutions;
            eventTime = nextTime;
            baselineAt = now;
            lastPacketAt = now;
            lastProgressAt = -1;
            rpm = 0;
            return true;
        }
        lastPacketAt = now;
        int deltaRevolutions = (nextRevolutions - revolutions) & 0xffff;
        int deltaTime = (nextTime - eventTime) & 0xffff;
        // Repeated values are normal when stationary; they must not renew freshness.
        if (deltaRevolutions == 0 && deltaTime == 0) return true;
        long elapsedSinceEvent = now - baselineAt;
        baselineAt = now;
        revolutions = nextRevolutions;
        eventTime = nextTime;
        if (deltaRevolutions == 0 || deltaTime == 0) {
            rpm = 0;
            lastProgressAt = -1;
            return true;
        }
        double candidate = deltaRevolutions * 60.0 * 1024.0 / deltaTime;
        // Counter resets/reordered data generally produce impossible deltas.
        // Re-baseline, never clamp an invalid packet into a plausible moving speed.
        if (candidate > 220 || elapsedSinceEvent >= TIMEOUT_MS) {
            rpm = 0;
            lastProgressAt = -1;
            return true;
        }
        rpm = candidate;
        lastProgressAt = now;
        return true;
    }

    long ageMs(long now) {
        return lastProgressAt < 0 ? -1 : Math.max(0, now - lastProgressAt);
    }

    double rpm(long now) {
        long age = ageMs(now);
        return age >= 0 && age < TIMEOUT_MS && now - lastPacketAt < TIMEOUT_MS ? rpm : 0;
    }

    private static int u16(byte[] value, int offset) {
        return (value[offset] & 0xff) | ((value[offset + 1] & 0xff) << 8);
    }
}
