namespace VirtualRide.Input
{
    public enum RideInputState
    {
        Offline,
        Ready,
        Searching,
        Detected,
        Error
    }

    public struct RideInputSample
    {
        public RideInputSample(
            float speedKph,
            float cadenceRpm,
            float confidence,
            RideInputState state,
            string status)
        {
            SpeedKph = speedKph;
            CadenceRpm = cadenceRpm;
            Confidence = confidence;
            State = state;
            Status = status;
        }

        public float SpeedKph { get; }
        public float CadenceRpm { get; }
        public float Confidence { get; }
        public RideInputState State { get; }
        public string Status { get; }
        public bool HasCadence => CadenceRpm >= 0f;
    }

    /// <summary>
    /// Boundary between a cadence/speed measuring device and the virtual ride.
    /// A future Bluetooth sensor only needs to implement this interface.
    /// </summary>
    public interface IRideInputSource
    {
        string DisplayName { get; }
        bool IsActive { get; }
        RideInputSample Current { get; }

        void Activate();
        void Deactivate();
        void Tick(float unscaledDeltaTime);
    }
}
