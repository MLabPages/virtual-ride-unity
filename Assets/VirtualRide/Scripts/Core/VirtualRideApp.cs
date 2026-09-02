using System;
using UnityEngine;
using VirtualRide.Input;
using VirtualRide.UI;
using VirtualRide.World;

namespace VirtualRide.Core
{
    public sealed class VirtualRideApp : MonoBehaviour
    {
        private const float BlockedActionMessageSeconds = 3f;

        private RideRoute _route;
        private RideController _rideController;
        private RideSession _session;
        private KeyboardRideInput _keyboardInput;
        private CameraCadenceInput _cameraInput;
        private ResearchSessionRecorder _researchRecorder;
        private IRideInputSource _activeInput;
        private IRideInputSource _externalInput;
        private float _displaySpeed;
        private bool _paused;
        private bool _helpVisible = true;
        private bool _researchPanelVisible;
        private string _blockedActionMessage = string.Empty;
        private float _blockedActionUntil;

        public static VirtualRideApp Instance { get; private set; }

        public RideRoute Route => _route;
        public RideSession Session => _session;
        public CameraCadenceInput CameraInput => _cameraInput;
        public KeyboardRideInput KeyboardInput => _keyboardInput;
        public ResearchSessionRecorder ResearchRecorder => _researchRecorder;
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
        public bool ResearchPanelVisible => _researchPanelVisible;
        public bool WindEnabled => _rideController != null && _rideController.WindEnabled;
        public string InputModeName => _activeInput != null ? _activeInput.DisplayName : "入力なし";
        public bool IsInputLocked => _researchRecorder != null && _researchRecorder.IsRecording;
        public bool ActiveInputIsUnvalidatedMeasurement =>
            _activeInput != null && _activeInput.IsUnvalidatedMeasurement;
        public string BlockedActionMessage => _blockedActionMessage;
        public bool IsBlockedActionMessageVisible =>
            !string.IsNullOrEmpty(_blockedActionMessage) && Time.unscaledTime < _blockedActionUntil;

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
            _researchRecorder = new ResearchSessionRecorder();
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

            bool wasRecording = _researchRecorder.IsRecording;
            _researchRecorder.Tick(unscaledDeltaTime, this);
            if (wasRecording && !_researchRecorder.IsRecording)
            {
                _researchPanelVisible = true;
                _helpVisible = false;
            }
        }

        public void UseKeyboardInput()
        {
            TrySetInput(_keyboardInput);
        }

        public void UseCameraInput()
        {
            if (!TrySetInput(_cameraInput))
            {
                return;
            }

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

            if (IsInputLocked)
            {
                throw new InvalidOperationException("記録中は入力方式を変更できません。");
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
            if (_helpVisible)
            {
                _researchPanelVisible = false;
            }
        }

        public void HideHelp()
        {
            _helpVisible = false;
        }

        public void ToggleResearchPanel()
        {
            _researchPanelVisible = !_researchPanelVisible;
            if (_researchPanelVisible)
            {
                _helpVisible = false;
            }
        }

        public void HideResearchPanel()
        {
            _researchPanelVisible = false;
        }

        public bool BeginResearchSession(string participantId, string condition, float trialDurationSeconds = 0f)
        {
            _session.Reset();
            _paused = false;
            return _researchRecorder.Start(participantId, condition, this, trialDurationSeconds);
        }

        public bool EndResearchSession(string reason = ResearchSessionRecorder.StopReasonCompleted)
        {
            return _researchRecorder.Stop(this, reason);
        }

        public bool AddResearchEventMarker(string markerType, string note = "")
        {
            return _researchRecorder.AddEventMarker(markerType, note);
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
            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中は走行値をリセットできません。");
                return;
            }

            _session.Reset();
        }

        public void AdjustKeyboardSpeed(float amountKph)
        {
            if (!ReferenceEquals(_activeInput, _keyboardInput))
            {
                if (!TrySetInput(_keyboardInput))
                {
                    return;
                }
            }

            _keyboardInput.Step(amountKph);
            _paused = false;
        }

        public bool TryToggleCameraLegView()
        {
            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中はカメラ設定を変更できません。開始前に合わせてください。");
                return false;
            }

            _cameraInput.ToggleLegView();
            return true;
        }

        public bool TryCycleCameraSensitivity()
        {
            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中はカメラ設定を変更できません。開始前に合わせてください。");
                return false;
            }

            _cameraInput.CycleSensitivity();
            return true;
        }

        private bool TrySetInput(IRideInputSource source)
        {
            if (source == null)
            {
                return false;
            }

            if (ReferenceEquals(_activeInput, source))
            {
                return true;
            }

            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中は入力方式を変更できません。終了してから切り替えてください。");
                return false;
            }

            SetInput(source);
            return true;
        }

        private void NotifyActionBlocked(string message)
        {
            _blockedActionMessage = message;
            _blockedActionUntil = Time.unscaledTime + BlockedActionMessageSeconds;
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

            if (_researchRecorder.IsRecording && UnityEngine.Input.GetKeyDown(KeyCode.F8))
            {
                AddResearchEventMarker(ResearchSessionRecorder.MarkerInstruction);
            }

            if (_researchRecorder.IsRecording && UnityEngine.Input.GetKeyDown(KeyCode.F9))
            {
                AddResearchEventMarker(ResearchSessionRecorder.MarkerRest);
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                _researchRecorder?.Stop(this, ResearchSessionRecorder.StopReasonApplicationClosed);
                _activeInput?.Deactivate();
                Instance = null;
            }
        }
    }
}
