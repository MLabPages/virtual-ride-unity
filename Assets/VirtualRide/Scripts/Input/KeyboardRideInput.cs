using UnityEngine;

namespace VirtualRide.Input
{
    public sealed class KeyboardRideInput : MonoBehaviour, IRideInputSource
    {
        private const float MinimumSpeed = 0f;
        private const float MaximumSpeed = 40f;
        private const float ChangePerSecond = 9f;
        private const float MetersPerRevolution = 4.2f;

        private float _speedKph;
        private bool _isActive;

        public string DisplayName => "キーボード";
        public bool IsActive => _isActive;
        public bool IsUnvalidatedMeasurement => false;

        public RideInputSample Current
        {
            get
            {
                float cadence = _speedKph > 0.1f
                    ? _speedKph * 1000f / (MetersPerRevolution * 60f)
                    : -1f;
                string status = _speedKph > 0.1f
                    ? "試運転中（↑↓ または W/S で調整）"
                    : "↑ または W で速度を上げます";

                return new RideInputSample(
                    _speedKph,
                    cadence,
                    1f,
                    RideInputState.Ready,
                    status);
            }
        }

        public void Activate()
        {
            _isActive = true;
        }

        public void Deactivate()
        {
            _isActive = false;
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!_isActive)
            {
                return;
            }

            bool increase = UnityEngine.Input.GetKey(KeyCode.UpArrow) ||
                            UnityEngine.Input.GetKey(KeyCode.W) ||
                            UnityEngine.Input.GetKey(KeyCode.Equals) ||
                            UnityEngine.Input.GetKey(KeyCode.KeypadPlus);
            bool decrease = UnityEngine.Input.GetKey(KeyCode.DownArrow) ||
                            UnityEngine.Input.GetKey(KeyCode.S) ||
                            UnityEngine.Input.GetKey(KeyCode.Minus) ||
                            UnityEngine.Input.GetKey(KeyCode.KeypadMinus);

            if (increase != decrease)
            {
                float direction = increase ? 1f : -1f;
                SetSpeed(_speedKph + direction * ChangePerSecond * unscaledDeltaTime);
            }
        }

        public void SetSpeed(float speedKph)
        {
            _speedKph = Mathf.Clamp(speedKph, MinimumSpeed, MaximumSpeed);
        }

        public void Step(float amountKph)
        {
            SetSpeed(_speedKph + amountKph);
        }
    }
}
