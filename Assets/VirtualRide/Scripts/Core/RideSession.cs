using UnityEngine;

namespace VirtualRide.Core
{
    public sealed class RideSession
    {
        private float _movingDistanceMetres;
        public float DistanceMetres { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float MovingSeconds { get; private set; }
        public float MaximumSpeedKph { get; private set; }
        public float AverageSpeedKph => MovingSeconds > 0.01f
            ? _movingDistanceMetres / MovingSeconds * 3.6f
            : 0f;

        public void Tick(float speedKph, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            ElapsedSeconds += deltaTime;
            MaximumSpeedKph = Mathf.Max(MaximumSpeedKph, speedKph);
            float travelled = Mathf.Max(0f, speedKph) / 3.6f * deltaTime;
            DistanceMetres += travelled;
            if (speedKph < 0.8f)
            {
                return;
            }

            MovingSeconds += deltaTime;
            _movingDistanceMetres += travelled;
        }

        public void Reset()
        {
            DistanceMetres = 0f;
            _movingDistanceMetres = 0f;
            ElapsedSeconds = 0f;
            MovingSeconds = 0f;
            MaximumSpeedKph = 0f;
        }
    }
}
