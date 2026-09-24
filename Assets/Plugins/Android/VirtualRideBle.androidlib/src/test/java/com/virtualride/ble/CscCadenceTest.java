package com.virtualride.ble;

/** Dependency-free JVM regression test; never packaged in the Android player. */
public final class CscCadenceTest {
    private static int checks;

    public static void main(String[] args) {
        CscCadence cadence = new CscCadence();
        check(cadence.rpm(0) == 0, "initial zero");
        check(cadence.accept(crank(10, 1000), 0), "initial packet accepted");
        check(cadence.rpm(0) == 0, "first packet is baseline only");
        cadence.accept(crank(11, 2024), 1000);
        near(cadence.rpm(1000), 60, "60 rpm");
        cadence.accept(crank(14, 4072), 2000);
        near(cadence.rpm(2000), 90, "multiple revolutions");
        near(cadence.rpm(4499), 90, "before timeout");
        check(cadence.rpm(4500) == 0, "exact 2.5-second timeout");
        cadence.accept(crank(18, 5096), 5000);
        check(cadence.rpm(5000) == 0, "gap re-baselines");
        cadence.accept(crank(19, 6120), 6000);
        near(cadence.rpm(6000), 60, "resume from new baseline");

        cadence.reset();
        cadence.accept(crank(65535, 65000), 0);
        cadence.accept(crank(0, 488), 1000);
        near(cadence.rpm(1000), 60, "both uint16 counters wrap");
        cadence.accept(crank(2, 2536), 2000);
        near(cadence.rpm(2000), 60, "continues after wrap");

        cadence.reset();
        cadence.accept(both(100, 65000), 0);
        cadence.accept(both(101, 488), 1000);
        near(cadence.rpm(1000), 60, "wheel fields skipped");
        check(!cadence.accept(new byte[] { 1, 0, 0, 0, 0, 0, 0 }, 2000), "wheel only ignored");
        check(!cadence.accept(new byte[] { 3, 0, 0, 0, 0 }, 2200), "truncated combined packet ignored");
        check(!cadence.accept(new byte[] { 2, 0 }, 2300), "truncated crank packet ignored");
        check(!cadence.accept(null, 2400), "null ignored");
        check(!cadence.accept(new byte[0], 2500), "empty ignored");
        check(!cadence.accept(new byte[] { 6, 1, 0, 1, 0 }, 3000), "reserved flags ignored");
        check(cadence.rpm(3500) == 0, "malformed packets do not extend freshness");

        cadence.reset();
        cadence.accept(crank(10, 0), 0);
        cadence.accept(crank(11, 1024), 1000);
        cadence.accept(crank(11, 1024), 2000);
        cadence.accept(crank(11, 1024), 3000);
        cadence.accept(crank(11, 1024), 3500);
        check(cadence.rpm(3500) == 0, "duplicate notifications still stop after 2.5 seconds");
        // Longer than a complete 64-second event-clock wrap, with ongoing duplicates.
        for (long now = 4000; now <= 70000; now += 1000) cadence.accept(crank(11, 1024), now);
        cadence.accept(crank(12, 2048), 71000);
        check(cadence.rpm(71000) == 0, "long stationary wrap resumes with baseline only");
        cadence.accept(crank(13, 3072), 72000);
        near(cadence.rpm(72000), 60, "stationary resume follows next revolution");

        cadence.accept(crank(14, 3072), 73000);
        check(cadence.rpm(73000) == 0, "zero event delta rejected");
        cadence.accept(crank(14, 4000), 74000);
        check(cadence.rpm(74000) == 0, "zero revolution delta rejected");
        cadence.accept(crank(13, 5000), 75000);
        check(cadence.rpm(75000) == 0, "counter reset does not become huge cadence");
        cadence.accept(crank(14, 6024), 76000);
        near(cadence.rpm(76000), 60, "recover after reset");
        cadence.accept(crank(15, 6124), 76100);
        check(cadence.rpm(76100) == 0, "impossible rpm rejected instead of clamped");
        cadence.accept(crank(16, 7148), 77100);
        near(cadence.rpm(77100), 60, "recover after invalid cadence");
        cadence.reset();
        check(cadence.rpm(77100) == 0 && cadence.ageMs(77100) == -1, "disconnect clears freshness");
        System.out.println("CSC cadence: " + checks + " checks passed");
    }

    private static byte[] crank(int count, int time) {
        return new byte[] { 2, (byte)count, (byte)(count >> 8), (byte)time, (byte)(time >> 8) };
    }

    private static byte[] both(int count, int time) {
        return new byte[] { 3, 1, 2, 3, 4, 5, 6, (byte)count, (byte)(count >> 8), (byte)time, (byte)(time >> 8) };
    }

    private static void near(double actual, double expected, String label) {
        check(Math.abs(actual - expected) < 0.001, label);
    }

    private static void check(boolean condition, String label) {
        if (!condition) throw new AssertionError(label);
        ++checks;
    }
}
