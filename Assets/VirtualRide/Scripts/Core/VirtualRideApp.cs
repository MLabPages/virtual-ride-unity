using System;
using UnityEngine;
using VirtualRide.Input;
using VirtualRide.UI;
using VirtualRide.World;

namespace VirtualRide.Core
{
    public sealed class VirtualRideApp : MonoBehaviour
    {
        public enum VideoSpeedMode
        {
            PedalLinked,
            Fixed
        }

        public const float MinimumFixedVideoSpeedKph = 5f;
        public const float MaximumFixedVideoSpeedKph = 35f;
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
        private bool _minimalHud;
        private VideoSpeedMode _videoSpeedMode = VideoSpeedMode.PedalLinked;
        private float _fixedVideoSpeedKph = 15f;
        private bool _pedalPreviewVisible = true;
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
        public bool ComfortMode => _rideController != null && _rideController.ComfortMode;
        public bool MinimalHud => _minimalHud;
        public VideoSpeedMode VideoSpeed => _videoSpeedMode;
        public bool IsVideoSpeedFixed => _videoSpeedMode == VideoSpeedMode.Fixed;
        public float FixedVideoSpeedKph => _fixedVideoSpeedKph;
        public bool PedalPreviewVisible => _pedalPreviewVisible;
        public bool KeyboardControlsBlocked => _helpVisible || _researchPanelVisible;
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
            _keyboardInput.ControlsEnabled = !KeyboardControlsBlocked;
            _activeInput?.Tick(unscaledDeltaTime);

            RideInputSample sample = ActiveSample;
            bool validInput = !float.IsNaN(sample.SpeedKph) && !float.IsInfinity(sample.SpeedKph)
                && sample.State != RideInputState.Error && sample.State != RideInputState.Offline;
            // In the fixed condition the scenery ignores pedalling; the input is still measured and logged.
            float targetSpeed = _paused ? 0f
                : IsVideoSpeedFixed ? _fixedVideoSpeedKph
                : !validInput ? 0f
                : Mathf.Clamp(sample.SpeedKph, 0f, 45f);
            float step = _researchRecorder.IsRecording && _researchRecorder.HasTrialDuration
                ? Mathf.Min(unscaledDeltaTime, _researchRecorder.RemainingTrialSeconds) : unscaledDeltaTime;
            float changeRate = targetSpeed > _displaySpeed ? 5.5f : 7.5f;
            _displaySpeed = _paused ? 0f : Mathf.MoveTowards(_displaySpeed, targetSpeed, changeRate * step);
            if (_displaySpeed < 0.05f)
            {
                _displaySpeed = 0f;
            }

            _rideController.SetSpeed(_displaySpeed);
            _rideController.Advance(step);
            _session.Tick(_displaySpeed, step);

            bool wasRecording = _researchRecorder.IsRecording;
            _researchRecorder.Tick(step, this);
            if (wasRecording && !_researchRecorder.IsRecording)
            {
                FinishTrial();
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
            if (_paused) StopMotion();
            if (IsInputLocked) AddResearchEventMarker(_paused ? "pause" : "resume");
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
            if (!_researchRecorder.Start(participantId, condition, this, trialDurationSeconds)) return false;
            _session.Reset();
            _rideController.ResetRoute();
            StopMotion();
            _paused = false;
            _researchPanelVisible = false;
            _helpVisible = false;
            _blockedActionMessage = string.Empty;
            return true;
        }

        public bool EndResearchSession(string reason = ResearchSessionRecorder.StopReasonCompleted)
        {
            bool wasRecording = _researchRecorder.IsRecording;
            bool saved = _researchRecorder.Stop(this, reason);
            if (wasRecording) FinishTrial();
            return saved;
        }

        public bool AddResearchEventMarker(string markerType, string note = "")
        {
            bool wasRecording = IsInputLocked;
            bool result = _researchRecorder.AddEventMarker(markerType, note);
            if (wasRecording && !IsInputLocked) FinishTrial();
            return result;
        }

        public void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        public void ToggleWind()
        {
            if (IsInputLocked) { NotifyActionBlocked("記録中は音・表示設定を変更できません。"); return; }
            _rideController.ToggleWind();
        }

        public void ToggleComfortMode()
        {
            if (IsInputLocked) { NotifyActionBlocked("記録中は視点設定を変更できません。"); return; }
            _rideController.ToggleComfortMode();
        }

        public void ToggleMinimalHud()
        {
            if (IsInputLocked) { NotifyActionBlocked("記録中は表示設定を変更できません。"); return; }
            _minimalHud = !_minimalHud;
        }

        public bool SetVideoSpeedMode(VideoSpeedMode mode)
        {
            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中は映像の速度条件を変更できません。");
                return false;
            }

            _videoSpeedMode = mode;
            return true;
        }

        public bool SetFixedVideoSpeed(float speedKph)
        {
            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中は映像の速度条件を変更できません。");
                return false;
            }

            if (float.IsNaN(speedKph) || float.IsInfinity(speedKph))
            {
                return false;
            }

            _fixedVideoSpeedKph = Mathf.Clamp(Mathf.Round(speedKph * 10f) / 10f,
                MinimumFixedVideoSpeedKph, MaximumFixedVideoSpeedKph);
            return true;
        }

        public void TogglePedalPreview()
        {
            if (IsInputLocked) { NotifyActionBlocked("記録中はペダル映像の表示を変更できません。"); return; }
            _pedalPreviewVisible = !_pedalPreviewVisible;
        }

        private void StopMotion()
        {
            _displaySpeed = 0f;
            _rideController.SetSpeed(0f);
        }

        private void FinishTrial()
        {
            _blockedActionMessage = string.Empty;
            _paused = true;
            StopMotion();
            _researchPanelVisible = true;
            _helpVisible = false;
        }

        // Used only by the opt-in player verification, never during a participant session.
        internal void PreviewRouteForTesting(float progress)
        {
            if (!VirtualRideSmokeTest.IsRequested || IsInputLocked) return;
            _rideController.ResetRoute(_route.TotalLength * progress);
        }

        public void ResetSession()
        {
            if (IsInputLocked)
            {
                NotifyActionBlocked("記録中は走行値をリセットできません。");
                return;
            }

            _session.Reset();
            _rideController.ResetRoute();
            StopMotion();
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

        public bool TrySelectAdjacentCamera(int direction)
        {
            if (!CanChangeCameraSettings())
            {
                return false;
            }

            _cameraInput.SelectAdjacentDevice(direction);
            return true;
        }

        public bool TryRestartCamera()
        {
            if (!CanChangeCameraSettings())
            {
                return false;
            }

            _cameraInput.RestartCamera();
            return true;
        }

        public bool TryCycleCameraRegion()
        {
            if (!CanChangeCameraSettings())
            {
                return false;
            }

            _cameraInput.CycleRegion();
            return true;
        }

        private bool CanChangeCameraSettings()
        {
            if (!IsInputLocked)
            {
                return true;
            }

            NotifyActionBlocked("記録中はカメラ設定を変更できません。開始前に合わせてください。");
            return false;
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
            foreach (KeyCode key in ShortcutKeys)
                if (UnityEngine.Input.GetKeyDown(key)) HandleShortcut(key);
        }

        private static readonly KeyCode[] ShortcutKeys = { KeyCode.Escape, KeyCode.Space, KeyCode.C,
            KeyCode.K, KeyCode.H, KeyCode.F, KeyCode.R, KeyCode.Tab, KeyCode.F7, KeyCode.F8, KeyCode.F9 };

        internal void HandleShortcut(KeyCode key)
        {
            if (key == KeyCode.Escape)
            {
                if (_researchPanelVisible) HideResearchPanel();
                else ToggleHelp();
                return;
            }
            if (key == KeyCode.F8 && IsInputLocked) AddResearchEventMarker(ResearchSessionRecorder.MarkerInstruction);
            if (key == KeyCode.F9 && IsInputLocked) AddResearchEventMarker(ResearchSessionRecorder.MarkerRest);
            if (KeyboardControlsBlocked) return;
            switch (key)
            {
                case KeyCode.Space: TogglePause(); break;
                case KeyCode.C: UseCameraInput(); break;
                case KeyCode.K: UseKeyboardInput(); break;
                case KeyCode.H: ToggleHelp(); break;
                case KeyCode.F: ToggleFullscreen(); break;
                case KeyCode.R: ResetSession(); break;
                case KeyCode.Tab: ToggleMinimalHud(); break;
                case KeyCode.F7: ToggleResearchPanel(); break;
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
