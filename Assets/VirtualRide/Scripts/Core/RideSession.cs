using UnityEngine;

namespace VirtualRide.Core
{
    public sealed class RideSession
    {
        public float DistanceMetres { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float MovingSeconds { get; private set; }
        public float MaximumSpeedKph { get; private set; }
        public float AverageSpeedKph => MovingSeconds > 0.01f
            ? DistanceMetres / MovingSeconds * 3.6f
            : 0f;

        public void Tick(float speedKph, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            ElapsedSeconds += deltaTime;
            MaximumSpeedKph = Mathf.Max(MaximumSpeedKph, speedKph);
            if (speedKph < 0.8f)
            {
                return;
            }

            MovingSeconds += deltaTime;
            DistanceMetres += speedKph / 3.6f * deltaTime;
        }

        public void Reset()
        {
            DistanceMetres = 0f;
            ElapsedSeconds = 0f;
            MovingSeconds = 0f;
            MaximumSpeedKph = 0f;
        }
    }
}
