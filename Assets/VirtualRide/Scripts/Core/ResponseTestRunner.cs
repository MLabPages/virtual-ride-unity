using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VirtualRide.Input;

namespace VirtualRide.Core
{
    /// <summary>
    /// Cue-based check of the live input: the rider pedals to a metronome and stops on cue while
    /// the app measures how quickly the estimate and the scenery respond. Only numbers are saved.
    /// </summary>
    public sealed class ResponseTestRunner : MonoBehaviour
    {
        public enum Phase
        {
            Idle,
            Prepare,
            Pedal,
            Stop,
            Finished
        }

        public const float PrepareSeconds = 5f;
        public const float PedalSeconds = 12f;
        public const float StopSeconds = 8f;
        public const float SteadySeconds = 6f;
        public const float DisplayStartFraction = 0.5f;
        public const float DisplayStoppedKph = 0.5f;
        private static readonly float[] CycleRpms = { 50f, 65f, 80f };

        public sealed class CycleResult
        {
            public float TargetRpm;
            public float? DetectLatency;
            public float? DisplayLatency;
            public float? StopDetectLatency;
            public float? DisplayStopLatency;
            public float? MeanRpm;
            public float? RpmErrorPercent;
            public float DetectedFraction;
        }

        private readonly List<CycleResult> _results = new List<CycleResult>();
        private readonly StringBuilder _frames = new StringBuilder();
        private VirtualRideApp _app;
        private AudioSource _audio;
        private AudioClip _click;
        private Phase _phase = Phase.Idle;
        private int _cycle;
        private float _startedAt;
        private float _phaseStartedAt;
        private float _nextBeatAt;
        private float _lastBeatAt = -10f;
        private CycleResult _current;
        private float _rpmSum;
        private int _rpmCount;
        private int _steadyFrames;
        private int _steadyDetected;

        public Phase CurrentPhase => _phase;
        public bool IsRunning => _phase == Phase.Prepare || _phase == Phase.Pedal || _phase == Phase.Stop;
        public bool HasResults => _phase == Phase.Finished;
        public int CycleNumber => Mathf.Min(_cycle + 1, CycleRpms.Length);
        public int CycleCount => CycleRpms.Length;
        public float TargetRpm => CycleRpms[Mathf.Min(_cycle, CycleRpms.Length - 1)];
        public bool BeatVisible => Time.realtimeSinceStartup - _lastBeatAt < 0.12f;
        public IReadOnlyList<CycleResult> Results => _results;
        public string SavedDirectory { get; private set; } = string.Empty;
        public string SaveMessage { get; private set; } = string.Empty;

        public float PhaseRemainingSeconds
        {
            get
            {
                float length = _phase == Phase.Prepare ? PrepareSeconds : _phase == Phase.Pedal ? PedalSeconds : StopSeconds;
                return Mathf.Max(0f, length - (Time.realtimeSinceStartup - _phaseStartedAt));
            }
        }

        private void Awake()
        {
            _app = GetComponent<VirtualRideApp>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0.8f;
            _click = MakeClick();
        }

        public void Begin()
        {
            _results.Clear();
            _frames.Length = 0;
            _frames.Append("elapsed_seconds,phase,cycle,target_rpm,input_state,estimated_rpm,input_speed_kph,display_speed_kph\n");
            SavedDirectory = string.Empty;
            SaveMessage = string.Empty;
            _cycle = 0;
            _startedAt = Time.realtimeSinceStartup;
            EnterPhase(Phase.Prepare);
        }

        public void Cancel()
        {
            if (!IsRunning)
            {
                return;
            }

            _phase = Phase.Idle;
        }

        public void Dismiss()
        {
            if (_phase == Phase.Finished)
            {
                _phase = Phase.Idle;
            }
        }

        public static float? Median(IEnumerable<CycleResult> results, Func<CycleResult, float?> selector)
        {
            var values = new List<float>();
            foreach (CycleResult result in results)
            {
                float? value = selector(result);
                if (value.HasValue)
                {
                    values.Add(value.Value);
                }
            }

            if (values.Count == 0)
            {
                return null;
            }

            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) * 0.5f;
        }

        private void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            float t = now - _phaseStartedAt;
            RideInputSample sample = _app.ActiveSample;
            float display = _app.DisplaySpeedKph;
            bool detected = sample.State == RideInputState.Detected && sample.HasCadence;
            LogFrame(now, sample, display);

            switch (_phase)
            {
                case Phase.Prepare:
                    if (t >= PrepareSeconds)
                    {
                        StartPedal(now);
                    }

                    break;
                case Phase.Pedal:
                    if (now >= _nextBeatAt)
                    {
                        _audio.PlayOneShot(_click);
                        _lastBeatAt = now;
                        // One click per pedal stroke: two clicks per revolution.
                        _nextBeatAt += 30f / TargetRpm;
                    }

                    if (!_current.DetectLatency.HasValue && detected)
                    {
                        _current.DetectLatency = t;
                    }

                    if (!_current.DisplayLatency.HasValue && display >= ExpectedSpeedKph() * DisplayStartFraction)
                    {
                        _current.DisplayLatency = t;
                    }

                    if (t >= PedalSeconds - SteadySeconds)
                    {
                        _steadyFrames++;
                        if (detected)
                        {
                            _steadyDetected++;
                            _rpmSum += sample.CadenceRpm;
                            _rpmCount++;
                        }
                    }

                    if (t >= PedalSeconds)
                    {
                        _current.DetectedFraction = _steadyFrames > 0 ? _steadyDetected / (float)_steadyFrames : 0f;
                        if (_rpmCount > 0)
                        {
                            _current.MeanRpm = _rpmSum / _rpmCount;
                            _current.RpmErrorPercent = (_current.MeanRpm.Value - _current.TargetRpm) / _current.TargetRpm * 100f;
                        }

                        EnterPhase(Phase.Stop);
                    }

                    break;
                case Phase.Stop:
                    if (!_current.StopDetectLatency.HasValue && !detected)
                    {
                        _current.StopDetectLatency = t;
                    }

                    if (!_current.DisplayStopLatency.HasValue && display < DisplayStoppedKph)
                    {
                        _current.DisplayStopLatency = t;
                    }

                    if (t >= StopSeconds)
                    {
                        _results.Add(_current);
                        _cycle++;
                        if (_cycle >= CycleRpms.Length)
                        {
                            _phase = Phase.Finished;
                            Save();
                        }
                        else
                        {
                            StartPedal(now);
                        }
                    }

                    break;
            }
        }

        private void StartPedal(float now)
        {
            _current = new CycleResult { TargetRpm = TargetRpm };
            _rpmSum = 0f;
            _rpmCount = 0;
            _steadyFrames = 0;
            _steadyDetected = 0;
            _nextBeatAt = now;
            EnterPhase(Phase.Pedal);
        }

        private void EnterPhase(Phase phase)
        {
            _phase = phase;
            _phaseStartedAt = Time.realtimeSinceStartup;
        }

        private float ExpectedSpeedKph()
        {
            return CadenceSpeedMapping.SpeedKph(TargetRpm, _app.CameraInput.MetersPerRevolution);
        }

        private void LogFrame(float now, RideInputSample sample, float display)
        {
            _frames.Append(Number(now - _startedAt)).Append(',')
                .Append(_phase.ToString().ToLowerInvariant()).Append(',')
                .Append(_phase == Phase.Prepare ? "0" : CycleNumber.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(_phase == Phase.Prepare ? string.Empty : Number(TargetRpm)).Append(',')
                .Append(sample.State.ToString()).Append(',')
                .Append(sample.HasCadence ? Number(sample.CadenceRpm) : string.Empty).Append(',')
                .Append(Number(sample.SpeedKph)).Append(',')
                .Append(Number(display)).Append('\n');
        }

        private void Save()
        {
            try
            {
                string directory = Path.Combine(_app.ResearchRecorder.DataDirectory, "response-tests");
                Directory.CreateDirectory(directory);
                string stem = "response_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                File.WriteAllText(Path.Combine(directory, stem + "_frames.csv"), _frames.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(directory, stem + ".json"), BuildJson(stem), new UTF8Encoding(false));
                SavedDirectory = directory;
                SaveMessage = "保存しました: " + Path.Combine(directory, stem + ".json");
            }
            catch (Exception exception)
            {
                SaveMessage = "結果を保存できませんでした: " + exception.Message;
                Debug.LogWarning("Response test could not be saved: " + exception);
            }
        }

        private string BuildJson(string stem)
        {
            CameraCadenceInput camera = _app.CameraInput;
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"testId\": \"").Append(stem).Append("\",\n");
            builder.Append("  \"recordedAtUtc\": \"").Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append("\",\n");
            builder.Append("  \"applicationVersion\": \"").Append(Escape(Application.version)).Append("\",\n");
            builder.Append("  \"cameraAlgorithm\": \"").Append(CadenceEstimator.AlgorithmVersion).Append("\",\n");
            builder.Append("  \"cameraDevice\": \"").Append(Escape(camera.SelectedDeviceName)).Append("\",\n");
            builder.Append("  \"measurementRegion\": \"").Append(camera.Region).Append("\",\n");
            builder.Append("  \"bothLegsVisible\": ").Append(camera.BothLegsVisible ? "true" : "false").Append(",\n");
            builder.Append("  \"sensitivity\": \"").Append(camera.Sensitivity).Append("\",\n");
            builder.Append("  \"metersPerRevolution\": ").Append(Number(camera.MetersPerRevolution)).Append(",\n");
            builder.Append("  \"displayAccelerationKphPerSecond\": ").Append(Number(VirtualRideApp.DisplayAccelerationKphPerSecond)).Append(",\n");
            builder.Append("  \"displayDecelerationKphPerSecond\": ").Append(Number(VirtualRideApp.DisplayDecelerationKphPerSecond)).Append(",\n");
            builder.Append("  \"speedMapping\": { \"version\": \"").Append(CadenceSpeedMapping.Version)
                .Append("\", \"baseMetresPerRevolution\": ").Append(Number(CadenceSpeedMapping.BaseMetresPerRevolution))
                .Append(", \"progressiveStartRpm\": ").Append(Number(CadenceSpeedMapping.ProgressiveStartRpm))
                .Append(", \"progressiveFullRpm\": ").Append(Number(CadenceSpeedMapping.ProgressiveFullRpm))
                .Append(", \"topMetresPerRevolution\": ").Append(Number(CadenceSpeedMapping.TopMetresPerRevolution))
                .Append(" },\n");
            builder.Append("  \"protocol\": { \"prepareSeconds\": ").Append(Number(PrepareSeconds))
                .Append(", \"pedalSeconds\": ").Append(Number(PedalSeconds))
                .Append(", \"stopSeconds\": ").Append(Number(StopSeconds))
                .Append(", \"steadySeconds\": ").Append(Number(SteadySeconds))
                .Append(", \"clicksPerRevolution\": 2 },\n");
            builder.Append("  \"latenciesIncludeHumanReaction\": true,\n");
            builder.Append("  \"cycles\": [\n");
            for (int i = 0; i < _results.Count; i++)
            {
                CycleResult r = _results[i];
                builder.Append("    { \"targetRpm\": ").Append(Number(r.TargetRpm))
                    .Append(", \"detectLatencySeconds\": ").Append(Nullable(r.DetectLatency))
                    .Append(", \"displayStartLatencySeconds\": ").Append(Nullable(r.DisplayLatency))
                    .Append(", \"stopDetectLatencySeconds\": ").Append(Nullable(r.StopDetectLatency))
                    .Append(", \"displayStopLatencySeconds\": ").Append(Nullable(r.DisplayStopLatency))
                    .Append(", \"meanRpm\": ").Append(Nullable(r.MeanRpm))
                    .Append(", \"rpmErrorPercent\": ").Append(Nullable(r.RpmErrorPercent))
                    .Append(", \"detectedFraction\": ").Append(Number(r.DetectedFraction))
                    .Append(i < _results.Count - 1 ? " },\n" : " }\n");
            }

            builder.Append("  ],\n");
            builder.Append("  \"medians\": { \"detectLatencySeconds\": ").Append(Nullable(Median(_results, r => r.DetectLatency)))
                .Append(", \"displayStartLatencySeconds\": ").Append(Nullable(Median(_results, r => r.DisplayLatency)))
                .Append(", \"stopDetectLatencySeconds\": ").Append(Nullable(Median(_results, r => r.StopDetectLatency)))
                .Append(", \"displayStopLatencySeconds\": ").Append(Nullable(Median(_results, r => r.DisplayStopLatency)))
                .Append(", \"absoluteRpmErrorPercent\": ")
                .Append(Nullable(Median(_results, r => r.RpmErrorPercent.HasValue ? Mathf.Abs(r.RpmErrorPercent.Value) : (float?)null)))
                .Append(" },\n");
            builder.Append("  \"framesFile\": \"").Append(stem).Append("_frames.csv\"\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        private static AudioClip MakeClick()
        {
            const int sampleRate = 44100;
            int length = sampleRate / 40;
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float envelope = 1f - i / (float)length;
                data[i] = Mathf.Sin(2f * Mathf.PI * 1500f * i / sampleRate) * envelope * envelope * 0.7f;
            }

            AudioClip clip = AudioClip.Create("Metronome click", length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "null";
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Nullable(float? value)
        {
            return value.HasValue ? Number(value.Value) : "null";
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '"' || c == '\\')
                {
                    builder.Append('\\').Append(c);
                }
                else if (c < 0x20)
                {
                    builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
