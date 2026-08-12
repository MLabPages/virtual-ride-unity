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

        private readonly List<MotionSample> _motionSamples = new List<MotionSample>(140);

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

        public string DisplayName => "カメラ計測";
        public bool IsActive => _isActive;
        public Texture PreviewTexture => _camera;
        public bool HasPreview => _camera != null && _camera.width > 16;
        public bool BothLegsVisible { get; private set; } = true;
        public DetectionSensitivity Sensitivity { get; private set; } = DetectionSensitivity.Standard;
        public float MetersPerRevolution { get; private set; } = 4.2f;
        public float MotionLevel => _motionLevel;

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
            if (_camera != null && _camera.isPlaying && _camera.didUpdateThisFrame && now >= _nextSampleAt)
            {
                _nextSampleAt = now + SampleInterval;
                SampleMotion(now);
            }

            if (_motionSamples.Count >= 50 && now >= _nextAnalysisAt)
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

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                SetStatus(RideInputState.Error, "利用できるカメラが見つかりません");
                yield break;
            }

            _camera = new WebCamTexture(devices[0].name, RequestedWidth, RequestedHeight, RequestedFps);
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
                SetStatus(RideInputState.Error, "カメラ映像を開始できませんでした");
                StopCameraTexture();
                yield break;
            }

            _nextSampleAt = Time.realtimeSinceStartup;
            _nextAnalysisAt = Time.realtimeSinceStartup + 3f;
            SetStatus(RideInputState.Searching, "ペダルの動きを探しています…");
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
            int analysisCount = AnalysisWidth * AnalysisHeight;
            float[] gray = new float[analysisCount];

            for (int y = 0; y < AnalysisHeight; y++)
            {
                int sourceY = Mathf.Clamp((y * height + height / 2) / AnalysisHeight, 0, height - 1);
                for (int x = 0; x < AnalysisWidth; x++)
                {
                    int sourceX = Mathf.Clamp((x * width + width / 2) / AnalysisWidth, 0, width - 1);
                    Color32 pixel = _cameraPixels[sourceY * width + sourceX];
                    gray[y * AnalysisWidth + x] =
                        0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b;
                }
            }

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
