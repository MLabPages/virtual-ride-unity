using System;
using UnityEngine;
using VirtualRide.Input;
using VirtualRide.UI;
using VirtualRide.World;

namespace VirtualRide.Core
{
    public sealed class VirtualRideApp : MonoBehaviour
    {
        private RideRoute _route;
        private RideController _rideController;
        private RideSession _session;
        private KeyboardRideInput _keyboardInput;
        private CameraCadenceInput _cameraInput;
        private IRideInputSource _activeInput;
        private IRideInputSource _externalInput;
        private float _displaySpeed;
        private bool _paused;
        private bool _helpVisible = true;

        public static VirtualRideApp Instance { get; private set; }

        public RideRoute Route => _route;
        public RideSession Session => _session;
        public CameraCadenceInput CameraInput => _cameraInput;
        public KeyboardRideInput KeyboardInput => _keyboardInput;
        public IRideInputSource ActiveInput => _activeInput;
        public RideInputSample ActiveSample => _activeInput != null
            ? _activeInput.Current
            : new RideInputSample(0f, -1f, 0f, RideInputState.Offline, "入力がありません");
        public float DisplaySpeedKph => _displaySpeed;
        public float RouteDistance => _rideController != null ? _rideController.RouteDistance : 0f;
        public string AreaName => _route != null ? _route.GetAreaName(RouteDistance) : "準備中";
        public float RouteProgress => _route != null ? _route.GetProgress01(RouteDistance) : 0f;
        public bool IsPaused => _paused;
        public bool HelpVisible => _helpVisible;
        public bool WindEnabled => _rideController != null && _rideController.WindEnabled;
        public string InputModeName => _activeInput != null ? _activeInput.DisplayName : "入力なし";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _route = new RideRoute();
            _session = new RideSession();
            ScenicWorldBuilder.Build(_route);

            GameObject rider = new GameObject("Rider");
            _rideController = rider.AddComponent<RideController>();
            _rideController.Initialize(_route);

            GameObject inputObject = new GameObject("Ride Inputs");
            _keyboardInput = inputObject.AddComponent<KeyboardRideInput>();
            _cameraInput = inputObject.AddComponent<CameraCadenceInput>();
            SetInput(_keyboardInput);

            gameObject.AddComponent<RideHud>();
            if (VirtualRideSmokeTest.IsRequested)
            {
                gameObject.AddComponent<VirtualRideSmokeTest>();
            }
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            _activeInput?.Tick(unscaledDeltaTime);

            RideInputSample sample = ActiveSample;
            float targetSpeed = _paused ? 0f : Mathf.Clamp(sample.SpeedKph, 0f, 45f);
            float changeRate = targetSpeed > _displaySpeed ? 5.5f : 7.5f;
            _displaySpeed = Mathf.MoveTowards(_displaySpeed, targetSpeed, changeRate * Time.deltaTime);
            if (_displaySpeed < 0.05f)
            {
                _displaySpeed = 0f;
            }

            _rideController.SetSpeed(_displaySpeed);
            _session.Tick(_displaySpeed, Time.deltaTime);
        }

        public void UseKeyboardInput()
        {
            SetInput(_keyboardInput);
        }

        public void UseCameraInput()
        {
            SetInput(_cameraInput);
            _paused = false;
        }

        /// <summary>
        /// Entry point for a future Bluetooth or USB cadence sensor adapter.
        /// The adapter remains owned by its integration layer; this app only activates it.
        /// </summary>
        public void AttachExternalInput(IRideInputSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _externalInput = source;
            SetInput(_externalInput);
        }

        public void TogglePause()
        {
            _paused = !_paused;
        }

        public void ToggleHelp()
        {
            _helpVisible = !_helpVisible;
        }

        public void HideHelp()
        {
            _helpVisible = false;
        }

        public void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        public void ToggleWind()
        {
            _rideController.ToggleWind();
        }

        public void ResetSession()
        {
            _session.Reset();
        }

        public void AdjustKeyboardSpeed(float amountKph)
        {
            if (!ReferenceEquals(_activeInput, _keyboardInput))
            {
                UseKeyboardInput();
            }

            _keyboardInput.Step(amountKph);
            _paused = false;
        }

        private void SetInput(IRideInputSource source)
        {
            if (source == null || ReferenceEquals(_activeInput, source))
            {
                return;
            }

            _activeInput?.Deactivate();
            _activeInput = source;
            _activeInput.Activate();
        }

        private void HandleKeyboardShortcuts()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.C))
            {
                UseCameraInput();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.K))
            {
                UseKeyboardInput();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.H) || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                ToggleHelp();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.F))
            {
                ToggleFullscreen();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.R))
            {
                ResetSession();
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                _activeInput?.Deactivate();
                Instance = null;
            }
        }
    }
}
