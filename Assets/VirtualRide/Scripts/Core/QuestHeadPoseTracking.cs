using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VirtualRide.Core
{
    /// <summary>
    /// Seated Quest tracking, relative to the first valid center-eye pose. The parent
    /// Rider Camera Rig owns route translation/yaw; this component owns only the camera.
    /// Uses Unity's XR device API backed by OpenXR, without a second pose driver.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class QuestHeadPoseTracking : MonoBehaviour
    {
        private readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();
        private XRInputSubsystem _inputSubsystem;
        private InputDevice _headDevice;
        private Vector3 _originPosition;
        private Quaternion _originRotation;
        private bool _hasOrigin;

        public bool HasTracking { get; private set; }

        /// <summary>Capture a new origin on the next valid pose, never on lost tracking.</summary>
        public void Recenter()
        {
            _hasOrigin = false;
        }

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Unity 6 XR does not use the legacy implicit camera tracker. This is
            // the sole pose driver; do not also attach a TrackedPoseDriver/XR Origin.
            Recenter();
            Application.onBeforeRender += UpdateHeadPose;
#else
            enabled = false;
#endif
        }

        private void Start()
        {
            // The XR compositor controls cadence (including the actual panel refresh rate).
            // Run after VirtualRideApp.Awake so its desktop 60 fps cap is not retained.
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
        }

        private void Update()
        {
            UpdateHeadPose();
        }

        // Poll again immediately before rendering to reduce motion-to-photon latency.
        [BeforeRenderOrder(100)]
        private void UpdateHeadPose()
        {
            if (_inputSubsystem == null || !_inputSubsystem.running)
            {
                BindInputSubsystem();
            }

            if (_inputSubsystem == null)
            {
                HasTracking = false;
                return;
            }

            if (!_headDevice.isValid)
            {
                InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                if (device.isValid && !device.Equals(_headDevice))
                {
                    Recenter();
                }
                _headDevice = device;
            }

            const InputTrackingState required = InputTrackingState.Position | InputTrackingState.Rotation;
            HasTracking = _headDevice.isValid &&
                _headDevice.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                _headDevice.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state) &&
                (state & required) == required;
            if (!HasTracking)
            {
                // Keep the last valid pose and origin through transient tracking loss.
                return;
            }

            bool validPosition = _headDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position);
            bool validRotation = _headDevice.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation);
            if (!validPosition || !validRotation)
            {
                validPosition = _headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                validRotation = _headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            }

            if (!validPosition || !validRotation || !IsFinite(position) || !IsValid(rotation))
            {
                HasTracking = false;
                return;
            }

            rotation = rotation.normalized;
            if (!_hasOrigin)
            {
                _originPosition = position;
                _originRotation = rotation;
                _hasOrigin = true;
            }

            Pose relative = RelativePose(_originPosition, _originRotation, position, rotation);
            transform.SetLocalPositionAndRotation(relative.position, relative.rotation);
        }

        private static Pose RelativePose(Vector3 originPosition, Quaternion originRotation,
            Vector3 position, Quaternion rotation)
        {
            Quaternion inverseOrigin = Quaternion.Inverse(originRotation);
            return new Pose(inverseOrigin * (position - originPosition), inverseOrigin * rotation);
        }

        private void BindInputSubsystem()
        {
            if (_inputSubsystem != null)
            {
                _inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            }
            _inputSubsystem = null;
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (XRInputSubsystem subsystem in _subsystems)
            {
                if (!subsystem.running) continue;
                _inputSubsystem = subsystem;
                _inputSubsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                // Device space is preferred for seated use. The origin subtraction also
                // handles runtimes that retain floor space, avoiding a second eye height.
                _inputSubsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
                Recenter();
                break;
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            // System recenter changed the tracking coordinate system.
            Recenter();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= UpdateHeadPose;
            if (_inputSubsystem != null)
            {
                _inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                _inputSubsystem = null;
            }
            HasTracking = false;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsValid(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z) && float.IsFinite(value.w) && Quaternion.Dot(value, value) > 0.0001f;
    }
}
