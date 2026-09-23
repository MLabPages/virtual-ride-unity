using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualRide.Input
{
    public sealed class CameraCadenceInput : MonoBehaviour, IRideInputSource
    {
        public enum DetectionSensitivity
        {
            High,
            Standard,
            Low
        }

        public enum MeasurementRegion
        {
            Full,
            Lower,
            Left,
            Right,
            Center
        }

        private struct MotionSample
        {
            public float Time;
            public float Motion;
            public float Focus;
            public float Globalness;
        }

        private struct Candidate
        {
            public float Rpm;
            public float Speed;
            public float Time;
            public int Seen;
        }

        private const int RequestedWidth = 320;
        private const int RequestedHeight = 240;
        private const int RequestedFps = 30;
        private const int AnalysisWidth = 48;
        private const int AnalysisHeight = 36;
        private const int GridColumns = 8;
        private const int GridRows = 6;
        private const float SampleInterval = 1f / 20f;
        private const float AnalysisInterval = 0.4f;
        private const float HistorySeconds = 6f;
        private const float MinimumRpm = 35f;
        private const float MaximumRpm = 115f;
        private const string CameraPreferenceKey = "VirtualRide.CameraDeviceName";
        private const string RegionPreferenceKey = "VirtualRide.CameraRegion";
        private const float DarkFrameBrightness = 10f;
        private const float DarkFrameSeconds = 2f;
        private const float StalledFrameSeconds = 3f;

        private readonly List<MotionSample> _motionSamples = new List<MotionSample>(140);
        private readonly List<string> _deviceNames = new List<string>();

        private WebCamTexture _camera;
        private Color32[] _cameraPixels;
        private float[] _previousGray;
        private float _nextSampleAt;
        private float _nextAnalysisAt;
        private float _lastGoodAt = -100f;
        private float _targetSpeed;
        private float _currentRpm = -1f;
        private float _confidence;
        private float _motionLevel;
        private bool _isActive;
        private string _status = "カメラは停止しています";
        private RideInputState _state = RideInputState.Offline;
        private Candidate? _candidate;
        private Coroutine _startRoutine;
        private string _selectedDeviceName = string.Empty;
        private bool _devicesLoaded;
        private bool _streaming;
        private float _lastFrameAt;
        private float _frameBrightness = -1f;
        private float _darkSince = -1f;
        private bool _frameProblem;

        public string DisplayName => "カメラ計測";
        public bool IsActive => _isActive;
        public bool IsUnvalidatedMeasurement => true;
        public Texture PreviewTexture => _camera;
        public bool HasPreview => _camera != null && _camera.width > 16;
        public bool BothLegsVisible { get; private set; } = true;
        public DetectionSensitivity Sensitivity { get; private set; } = DetectionSensitivity.Standard;
        public float MetersPerRevolution { get; private set; } = 4.2f;
        public float MotionLevel => _motionLevel;
        public MeasurementRegion Region { get; private set; } = MeasurementRegion.Full;
        public IReadOnlyList<string> DeviceNames => _deviceNames;
        public string SelectedDeviceName => _selectedDeviceName;
        public int SelectedDeviceIndex => _deviceNames.IndexOf(_selectedDeviceName);

        public string SelectedDeviceLabel
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedDeviceName))
                {
                    return _devicesLoaded ? "カメラが見つかりません" : "カメラを確認中…";
                }

                int index = SelectedDeviceIndex;
                return index >= 0 && _deviceNames.Count > 1
                    ? $"{index + 1}/{_deviceNames.Count}  {_selectedDeviceName}"
                    : _selectedDeviceName;
            }
        }

        public string RegionLabel
        {
            get
            {
                switch (Region)
                {
                    case MeasurementRegion.Lower:
                        return "下半分";
                    case MeasurementRegion.Left:
                        return "左半分";
                    case MeasurementRegion.Right:
                        return "右半分";
                    case MeasurementRegion.Center:
                        return "中央";
                    default:
                        return "全体";
                }
            }
        }

        /// <summary>
        /// Analysed area in texture coordinates (origin at the bottom-left, 0..1).
        /// </summary>
        public Rect RegionRect
        {
            get
            {
                switch (Region)
                {
                    case MeasurementRegion.Lower:
                        return new Rect(0f, 0f, 1f, 0.5f);
                    case MeasurementRegion.Left:
                        return new Rect(0f, 0f, 0.5f, 1f);
                    case MeasurementRegion.Right:
                        return new Rect(0.5f, 0f, 0.5f, 1f);
                    case MeasurementRegion.Center:
                        return new Rect(0.2f, 0.2f, 0.6f, 0.6f);
                    default:
                        return new Rect(0f, 0f, 1f, 1f);
                }
            }
        }

        public string SensitivityLabel
        {
            get
            {
                switch (Sensitivity)
                {
                    case DetectionSensitivity.High:
                        return "高";
                    case DetectionSensitivity.Low:
                        return "低";
                    default:
                        return "標準";
                }
            }
        }

        public RideInputSample Current => new RideInputSample(
            _targetSpeed,
            _currentRpm,
            _confidence,
            _state,
            _status);

        private void Awake()
        {
            int savedRegion = PlayerPrefs.GetInt(RegionPreferenceKey, (int)MeasurementRegion.Full);
            if (System.Enum.IsDefined(typeof(MeasurementRegion), savedRegion))
            {
                Region = (MeasurementRegion)savedRegion;
            }
        }

        public void Activate()
        {
            if (_isActive)
            {
                return;
            }

            _isActive = true;
            SetStatus(RideInputState.Searching, "カメラを準備しています…");
            _startRoutine = StartCoroutine(BeginCamera());
        }

        public void Deactivate()
        {
            _isActive = false;
            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
                _startRoutine = null;
            }

            StopCameraTexture();
            ResetDetection();
            SetStatus(RideInputState.Offline, "カメラは停止しています");
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!_isActive)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_camera != null && _camera.isPlaying && _camera.didUpdateThisFrame)
            {
                _lastFrameAt = now;
                if (now >= _nextSampleAt)
                {
                    _nextSampleAt = now + SampleInterval;
                    SampleMotion(now);
                }
            }

            if (_streaming)
            {
                UpdateFrameHealth(now);
            }

            if (!_frameProblem && _motionSamples.Count >= 50 && now >= _nextAnalysisAt)
            {
                _nextAnalysisAt = now + AnalysisInterval;
                AnalyzeCadence(now);
            }

            if (now - _lastGoodAt > 1.5f)
            {
                _targetSpeed = Mathf.MoveTowards(_targetSpeed, 0f, 5.5f * unscaledDeltaTime);
                if (_targetSpeed < 0.4f)
                {
                    _targetSpeed = 0f;
                    _currentRpm = -1f;
                    _confidence = 0f;
                }
            }
        }

        public void ToggleLegView()
        {
            BothLegsVisible = !BothLegsVisible;
            ResetRhythmCandidate();
        }

        public void CycleSensitivity()
        {
            Sensitivity = Sensitivity == DetectionSensitivity.High
                ? DetectionSensitivity.Standard
                : Sensitivity == DetectionSensitivity.Standard
                    ? DetectionSensitivity.Low
                    : DetectionSensitivity.High;
            ResetRhythmCandidate();
        }

        public void CycleRegion()
        {
            Region = (MeasurementRegion)(((int)Region + 1) % System.Enum.GetValues(typeof(MeasurementRegion)).Length);
            PlayerPrefs.SetInt(RegionPreferenceKey, (int)Region);
            PlayerPrefs.Save();
            _motionSamples.Clear();
            _previousGray = null;
            ResetRhythmCandidate();
        }

        /// <summary>
        /// Re-reads the cameras Windows currently exposes, e.g. after a USB camera was plugged in.
        /// Keeps the current choice when it is still connected.
        /// </summary>
        public void RefreshDevices()
        {
            _deviceNames.Clear();
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices != null)
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (!string.IsNullOrEmpty(devices[i].name) && !_deviceNames.Contains(devices[i].name))
                    {
                        _deviceNames.Add(devices[i].name);
                    }
                }
            }

            _devicesLoaded = true;
            if (!_deviceNames.Contains(_selectedDeviceName))
            {
                _selectedDeviceName = ChooseDefaultDevice();
            }
        }

        /// <summary>Selects the previous (-1) or next (+1) camera and restarts the preview.</summary>
        public void SelectAdjacentDevice(int direction)
        {
            RefreshDevices();
            if (_deviceNames.Count == 0)
            {
                if (_isActive)
                {
                    RestartCamera();
                }

                return;
            }

            int index = Mathf.Max(0, SelectedDeviceIndex);
            int count = _deviceNames.Count;
            index = ((index + (direction < 0 ? -1 : 1)) % count + count) % count;
            SelectDevice(_deviceNames[index]);
        }

        public void SelectDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return;
            }

            _selectedDeviceName = deviceName;
            PlayerPrefs.SetString(CameraPreferenceKey, deviceName);
            PlayerPrefs.Save();
            RestartCamera();
        }

        /// <summary>Stops and reopens the selected camera, e.g. after another app released it.</summary>
        public void RestartCamera()
        {
            if (!_isActive)
            {
                return;
            }

            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
                _startRoutine = null;
            }

            StopCameraTexture();
            ResetDetection();
            SetStatus(RideInputState.Searching, "カメラを準備しています…");
            _startRoutine = StartCoroutine(BeginCamera());
        }

        public void ChangeMetersPerRevolution(float amount)
        {
            MetersPerRevolution = Mathf.Clamp(
                Mathf.Round((MetersPerRevolution + amount) * 10f) / 10f,
                2f,
                8f);
        }

        private IEnumerator BeginCamera()
        {
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }

            if (!_isActive)
            {
                yield break;
            }

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                SetStatus(RideInputState.Error, "カメラの使用が許可されていません");
                yield break;
            }

            RefreshDevices();
            if (string.IsNullOrEmpty(_selectedDeviceName))
            {
                _startRoutine = null;
                SetStatus(RideInputState.Error, "利用できるカメラが見つかりません。USBカメラを挿してから「再接続」を押してください");
                yield break;
            }

            string deviceName = _selectedDeviceName;
            SetStatus(RideInputState.Searching, $"「{deviceName}」を準備しています…");
            _camera = new WebCamTexture(deviceName, RequestedWidth, RequestedHeight, RequestedFps);
            _camera.Play();

            float timeoutAt = Time.realtimeSinceStartup + 8f;
            while (_isActive && _camera.width <= 16 && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            _startRoutine = null;
            if (!_isActive)
            {
                yield break;
            }

            if (_camera.width <= 16)
            {
                SetStatus(RideInputState.Error,
                    $"「{deviceName}」を開始できませんでした。他のアプリを閉じるか、◀ ▶ で別のカメラを選んでください");
                StopCameraTexture();
                yield break;
            }

            _nextSampleAt = Time.realtimeSinceStartup;
            _nextAnalysisAt = Time.realtimeSinceStartup + 3f;
            _lastFrameAt = Time.realtimeSinceStartup;
            _streaming = true;
            SetStatus(RideInputState.Searching, "ペダルの動きを探しています…");
        }

        private void UpdateFrameHealth(float now)
        {
            string problem = null;
            if (now - _lastFrameAt > StalledFrameSeconds)
            {
                problem = "カメラ映像が止まっています。他のアプリがカメラを使っていないか確認し、「再接続」を押してください";
            }
            else if (_frameBrightness >= 0f && _frameBrightness < DarkFrameBrightness)
            {
                if (_darkSince < 0f)
                {
                    _darkSince = now;
                }

                if (now - _darkSince >= DarkFrameSeconds)
                {
                    problem = "映像が真っ暗です。カメラのシャッター・向きを確認するか、◀ ▶ で別のカメラを選んでください";
                }
            }
            else
            {
                _darkSince = -1f;
            }

            if (problem != null)
            {
                if (!_frameProblem)
                {
                    _frameProblem = true;
                    ResetRhythmCandidate();
                    _currentRpm = -1f;
                    _confidence = 0f;
                }

                SetStatus(RideInputState.Error, problem);
                return;
            }

            if (_frameProblem)
            {
                _frameProblem = false;
                _motionSamples.Clear();
                _previousGray = null;
                _nextAnalysisAt = now + 3f;
                SetStatus(RideInputState.Searching, "ペダルの動きを探しています…");
            }
        }

        private string ChooseDefaultDevice()
        {
            if (_deviceNames.Count == 0)
            {
                return string.Empty;
            }

            string saved = PlayerPrefs.GetString(CameraPreferenceKey, string.Empty);
            if (_deviceNames.Contains(saved))
            {
                return saved;
            }

            // Windows Hello infrared cameras produce dark monochrome frames; avoid them by default.
            for (int i = 0; i < _deviceNames.Count; i++)
            {
                if (!LooksLikeInfraredCamera(_deviceNames[i]))
                {
                    return _deviceNames[i];
                }
            }

            return _deviceNames[0];
        }

        private static bool LooksLikeInfraredCamera(string deviceName)
        {
            string lower = deviceName.ToLowerInvariant();
            if (lower.Contains("infrared") || lower.Contains("windows hello") || lower.Contains("赤外線"))
            {
                return true;
            }

            string[] tokens = lower.Split(' ', '-', '_', '(', ')', '[', ']', ',');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "ir")
                {
                    return true;
                }
            }

            return false;
        }

        private void SampleMotion(float now)
        {
            int width = _camera.width;
            int height = _camera.height;
            int requiredPixels = width * height;
            if (_cameraPixels == null || _cameraPixels.Length != requiredPixels)
            {
                _cameraPixels = new Color32[requiredPixels];
                _previousGray = null;
            }

            _camera.GetPixels32(_cameraPixels);
            Rect region = RegionRect;
            int regionX = Mathf.Clamp(Mathf.FloorToInt(region.x * width), 0, width - 1);
            int regionY = Mathf.Clamp(Mathf.FloorToInt(region.y * height), 0, height - 1);
            int regionWidth = Mathf.Clamp(Mathf.RoundToInt(region.width * width), 1, width - regionX);
            int regionHeight = Mathf.Clamp(Mathf.RoundToInt(region.height * height), 1, height - regionY);
            int analysisCount = AnalysisWidth * AnalysisHeight;
            float[] gray = new float[analysisCount];
            float brightnessSum = 0f;

            for (int y = 0; y < AnalysisHeight; y++)
            {
                int sourceY = Mathf.Clamp(
                    regionY + (y * regionHeight + regionHeight / 2) / AnalysisHeight, 0, height - 1);
                for (int x = 0; x < AnalysisWidth; x++)
                {
                    int sourceX = Mathf.Clamp(
                        regionX + (x * regionWidth + regionWidth / 2) / AnalysisWidth, 0, width - 1);
                    Color32 pixel = _cameraPixels[sourceY * width + sourceX];
                    float value = 0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b;
                    gray[y * AnalysisWidth + x] = value;
                    brightnessSum += value;
                }
            }

            _frameBrightness = brightnessSum / analysisCount;

            if (_previousGray != null)
            {
                float sum = 0f;
                float[] cellSums = new float[GridColumns * GridRows];
                for (int i = 0; i < analysisCount; i++)
                {
                    float difference = Mathf.Abs(gray[i] - _previousGray[i]);
                    sum += difference;
                    int x = i % AnalysisWidth;
                    int y = i / AnalysisWidth;
                    int gridX = Mathf.Min(GridColumns - 1, x * GridColumns / AnalysisWidth);
                    int gridY = Mathf.Min(GridRows - 1, y * GridRows / AnalysisHeight);
                    cellSums[gridY * GridColumns + gridX] += difference;
                }

                float motion = sum / analysisCount;
                float averageCell = sum / cellSums.Length;
                float maximumCell = 0f;
                for (int i = 0; i < cellSums.Length; i++)
                {
                    maximumCell = Mathf.Max(maximumCell, cellSums[i]);
                }

                int activeCells = 0;
                for (int i = 0; i < cellSums.Length; i++)
                {
                    if (maximumCell > 0f && cellSums[i] > maximumCell * 0.35f)
                    {
                        activeCells++;
                    }
                }

                _motionSamples.Add(new MotionSample
                {
                    Time = now,
                    Motion = motion,
                    Focus = maximumCell / Mathf.Max(1f, averageCell),
                    Globalness = activeCells / (float)cellSums.Length
                });
                while (_motionSamples.Count > 0 && now - _motionSamples[0].Time > HistorySeconds)
                {
                    _motionSamples.RemoveAt(0);
                }

                _motionLevel = Mathf.Lerp(_motionLevel, Mathf.Clamp01(motion / 8f), 0.25f);
            }

            _previousGray = gray;
        }

        private void AnalyzeCadence(float now)
        {
            GetThresholds(out float minimumMotion, out float minimumCorrelation,
                out float minimumFocus, out float maximumGlobalness);

            int count = _motionSamples.Count;
            float mean = 0f;
            float meanFocus = 0f;
            float meanGlobalness = 0f;
            for (int i = 0; i < count; i++)
            {
                mean += _motionSamples[i].Motion;
                meanFocus += _motionSamples[i].Focus;
                meanGlobalness += _motionSamples[i].Globalness;
            }

            mean /= count;
            meanFocus /= count;
            meanGlobalness /= count;

            if (mean < minimumMotion)
            {
                SetStatus(RideInputState.Searching, "動きが見えません。ペダルが映る位置を確認してください");
                ResetRhythmCandidate();
                return;
            }

            if (meanFocus < minimumFocus || meanGlobalness > maximumGlobalness)
            {
                SetStatus(RideInputState.Searching, "画面全体が揺れています。カメラを固定してください");
                ResetRhythmCandidate();
                return;
            }

            float[] centered = new float[count];
            for (int i = 0; i < count; i++)
            {
                centered[i] = _motionSamples[i].Motion - mean;
            }

            const float effectiveSampleRate = 1f / SampleInterval;
            int minimumLag = Mathf.RoundToInt(0.25f * effectiveSampleRate);
            int maximumLag = Mathf.Min(Mathf.RoundToInt(1.6f * effectiveSampleRate), count - 10);
            int bestLag = -1;
            float bestCorrelation = -1f;

            for (int lag = minimumLag; lag <= maximumLag; lag++)
            {
                float product = 0f;
                float energyA = 0f;
                float energyB = 0f;
                for (int i = 0; i < count - lag; i++)
                {
                    float a = centered[i];
                    float b = centered[i + lag];
                    product += a * b;
                    energyA += a * a;
                    energyB += b * b;
                }

                float denominator = Mathf.Sqrt(energyA * energyB);
                float correlation = denominator > 0.0001f ? product / denominator : 0f;
                if (correlation > bestCorrelation)
                {
                    bestCorrelation = correlation;
                    bestLag = lag;
                }
            }

            if (bestLag < 0 || bestCorrelation < minimumCorrelation)
            {
                SetStatus(RideInputState.Searching, "リズムを探しています。一定の速さで漕いでください");
                ResetRhythmCandidate();
                return;
            }

            float detectedPeriod = bestLag / effectiveSampleRate;
            float revolutionPeriod = BothLegsVisible ? detectedPeriod * 2f : detectedPeriod;
            float rpm = 60f / revolutionPeriod;
            if (rpm < MinimumRpm || rpm > MaximumRpm)
            {
                SetStatus(RideInputState.Searching, "ペダルらしいリズムを探しています…");
                ResetRhythmCandidate();
                return;
            }

            float speed = rpm * MetersPerRevolution * 60f / 1000f;
            if (!_candidate.HasValue || now - _candidate.Value.Time > 1.8f ||
                Mathf.Abs(rpm - _candidate.Value.Rpm) > Mathf.Max(10f, _candidate.Value.Rpm * 0.18f))
            {
                _candidate = new Candidate { Rpm = rpm, Speed = speed, Time = now, Seen = 1 };
                SetStatus(RideInputState.Searching, "リズムを確認しています…");
                return;
            }

            Candidate candidate = _candidate.Value;
            candidate.Rpm = Mathf.Lerp(candidate.Rpm, rpm, 0.35f);
            candidate.Speed = Mathf.Lerp(candidate.Speed, speed, 0.35f);
            candidate.Time = now;
            candidate.Seen++;
            _candidate = candidate;
            if (candidate.Seen < 2)
            {
                return;
            }

            _currentRpm = candidate.Rpm;
            _targetSpeed = Mathf.MoveTowards(_targetSpeed, candidate.Speed, 3.2f);
            _confidence = Mathf.InverseLerp(minimumCorrelation, 0.85f, bestCorrelation);
            _lastGoodAt = now;
            SetStatus(RideInputState.Detected, $"検出中: {Mathf.RoundToInt(_currentRpm)} rpm");
        }

        private void GetThresholds(
            out float minimumMotion,
            out float minimumCorrelation,
            out float minimumFocus,
            out float maximumGlobalness)
        {
            switch (Sensitivity)
            {
                case DetectionSensitivity.High:
                    minimumMotion = 1.0f;
                    minimumCorrelation = 0.30f;
                    minimumFocus = 1.35f;
                    maximumGlobalness = 0.86f;
                    break;
                case DetectionSensitivity.Low:
                    minimumMotion = 2.8f;
                    minimumCorrelation = 0.48f;
                    minimumFocus = 1.8f;
                    maximumGlobalness = 0.68f;
                    break;
                default:
                    minimumMotion = 1.8f;
                    minimumCorrelation = 0.38f;
                    minimumFocus = 1.55f;
                    maximumGlobalness = 0.78f;
                    break;
            }
        }

        private void SetStatus(RideInputState state, string status)
        {
            _state = state;
            _status = status;
        }

        private void ResetRhythmCandidate()
        {
            _candidate = null;
        }

        private void ResetDetection()
        {
            _motionSamples.Clear();
            _previousGray = null;
            _cameraPixels = null;
            _targetSpeed = 0f;
            _currentRpm = -1f;
            _confidence = 0f;
            _motionLevel = 0f;
            _lastGoodAt = -100f;
            _candidate = null;
            _streaming = false;
            _frameProblem = false;
            _frameBrightness = -1f;
            _darkSince = -1f;
        }

        private void StopCameraTexture()
        {
            if (_camera == null)
            {
                return;
            }

            if (_camera.isPlaying)
            {
                _camera.Stop();
            }

            Destroy(_camera);
            _camera = null;
        }

        private void OnDestroy()
        {
            StopCameraTexture();
        }
    }
}
