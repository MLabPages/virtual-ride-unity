using UnityEngine;

namespace VirtualRide.Input
{
    /// <summary>
    /// Shared cadence to virtual-speed conversion for every pedalling input.
    /// Up to 60 rpm one crank turn covers 4.2 m (the original linear mapping).
    /// Between 60 and 110 rpm the distance per turn rises smoothly to 6.0 m, like
    /// shifting into a bigger gear, so harder pedalling is clearly visible in the scenery.
    /// The mapping is monotonic and is written to every research summary.
    /// </summary>
    public static class CadenceSpeedMapping
    {
        public const string Version = "progressive-gear-2026.09";
        public const float BaseMetresPerRevolution = 4.2f;
        public const float ProgressiveStartRpm = 60f;
        public const float ProgressiveFullRpm = 110f;
        public const float TopMetresPerRevolution = 6.0f;
        public const float MaximumSpeedKph = 45f;

        public static float GearFactor(float rpm)
        {
            return Mathf.Lerp(1f, TopMetresPerRevolution / BaseMetresPerRevolution,
                Mathf.InverseLerp(ProgressiveStartRpm, ProgressiveFullRpm, rpm));
        }

        public static float MetresPerRevolution(float rpm, float baseMetres = BaseMetresPerRevolution)
        {
            return baseMetres * GearFactor(rpm);
        }

        public static float SpeedKph(float rpm, float baseMetres = BaseMetresPerRevolution)
        {
            if (float.IsNaN(rpm) || float.IsInfinity(rpm) || rpm <= 0f) return 0f;
            return Mathf.Clamp(rpm * MetresPerRevolution(rpm, baseMetres) * 60f / 1000f, 0f, MaximumSpeedKph);
        }

        /// <summary>Inverse of <see cref="SpeedKph"/> for the default base distance (keyboard display).</summary>
        public static float CadenceForSpeed(float speedKph)
        {
            if (float.IsNaN(speedKph) || speedKph <= 0f) return 0f;
            float low = 0f;
            float high = 220f;
            for (int i = 0; i < 40; i++)
            {
                float middle = (low + high) * 0.5f;
                if (middle * MetresPerRevolution(middle) * 60f / 1000f < speedKph) low = middle;
                else high = middle;
            }

            return (low + high) * 0.5f;
        }
    }
}
