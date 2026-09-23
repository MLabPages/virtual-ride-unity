using System.Collections.Generic;
using UnityEngine;

namespace VirtualRide.Input
{
    /// <summary>
    /// Estimates pedal cadence from a per-frame motion-energy signal. It has no camera access,
    /// so the player smoke test can verify accuracy and latency with synthetic signals.
    /// </summary>
    public sealed class CadenceEstimator
    {
        public const string AlgorithmVersion = "camera-cadence-2026.09b";
        public const float AnalysisInterval = 0.2f;
        public const float ResampleRate = 30f;
        public const float WindowSeconds = 3.2f;
        public const float MinimumSpanSeconds = 2.0f;
        public const float StopWindowSeconds = 0.7f;
        public const float StopMotionRatio = 0.7f;
        public const float HoldSeconds = 1.0f;
        public const float MinimumRpm = 35f;
        public const float MaximumRpm = 115f;
        private const float HistorySeconds = 4f;
        private const float MinimumPeriodSeconds = 0.25f;
        private const float MaximumPeriodSeconds = 1.75f;

        public enum Status
        {
            Waiting,
            NoMotion,
            Stopped,
            Shaking,
            NoRhythm,
            OutOfRange,
            Confirming,
            Detected
        }

        public struct Thresholds
        {
            public Thresholds(float minimumMotion, float minimumCorrelation, float minimumFocus, float maximumGlobalness)
            {
                MinimumMotion = minimumMotion;
                MinimumCorrelation = minimumCorrelation;
                MinimumFocus = minimumFocus;
                MaximumGlobalness = maximumGlobalness;
            }

            public float MinimumMotion { get; }
            public float MinimumCorrelation { get; }
            public float MinimumFocus { get; }
            public float MaximumGlobalness { get; }
        }

        private struct Sample
        {
            public float Time;
            public float Motion;
            public float Focus;
            public float Globalness;
        }

        private readonly List<Sample> _samples = new List<Sample>(160);
        private float[] _series = new float[128];
        private float[] _correlation = new float[64];
        private float _rpm = -1f;
        private float _lastGoodTime = -100f;
        private float _candidateRpm = -1f;
        private float _candidateTime = -100f;
        private int _candidateSeen;

        public float Rpm => _rpm;
        public bool HasCadence => _rpm > 0f;
        public float Confidence { get; private set; }
        public Status LastStatus { get; private set; } = Status.Waiting;

        public void Clear()
        {
            _samples.Clear();
            LoseCadence();
            LastStatus = Status.Waiting;
        }

        public void LoseCadence()
        {
            _rpm = -1f;
            Confidence = 0f;
            _candidateRpm = -1f;
            _candidateSeen = 0;
        }

        public void AddSample(float time, float motion, float focus, float globalness)
        {
            if (_samples.Count > 0 && time <= _samples[_samples.Count - 1].Time)
            {
                return;
            }

            _samples.Add(new Sample { Time = time, Motion = motion, Focus = focus, Globalness = globalness });
            float cutoff = time - HistorySeconds;
            int remove = 0;
            while (remove < _samples.Count && _samples[remove].Time < cutoff)
            {
                remove++;
            }

            if (remove > 0)
            {
                _samples.RemoveRange(0, remove);
            }
        }

        public Status Analyze(float now, Thresholds thresholds, bool bothLegsVisible)
        {
            LastStatus = Evaluate(now, thresholds, bothLegsVisible);
            return LastStatus;
        }

        private Status Evaluate(float now, Thresholds thresholds, bool bothLegsVisible)
        {
            int count = _samples.Count;
            if (count < 4 || now - _samples[0].Time < MinimumSpanSeconds)
            {
                return Hold(now, Status.Waiting);
            }

            // Stop check on the most recent frames only, so stopping is not masked by older pedalling.
            float recentSum = 0f;
            int recentCount = 0;
            for (int i = count - 1; i >= 0 && _samples[i].Time >= now - StopWindowSeconds; i--)
            {
                recentSum += _samples[i].Motion;
                recentCount++;
            }

            if (recentCount >= 3 && recentSum / recentCount < thresholds.MinimumMotion * StopMotionRatio)
            {
                Status stopped = HasCadence ? Status.Stopped : Status.NoMotion;
                LoseCadence();
                return stopped;
            }

            float end = _samples[count - 1].Time;
            float start = Mathf.Max(_samples[0].Time, end - WindowSeconds);
            int n = Mathf.FloorToInt((end - start) * ResampleRate) + 1;
            if (n < Mathf.RoundToInt(MinimumSpanSeconds * ResampleRate))
            {
                return Hold(now, Status.Waiting);
            }

            if (_series.Length < n)
            {
                _series = new float[n];
            }

            // Camera frames arrive with jitter; resample onto a uniform grid before autocorrelation.
            int j = 0;
            float mean = 0f;
            for (int k = 0; k < n; k++)
            {
                float t = start + k / ResampleRate;
                while (j < count - 2 && _samples[j + 1].Time < t)
                {
                    j++;
                }

                Sample a = _samples[j];
                Sample b = _samples[Mathf.Min(j + 1, count - 1)];
                float span = b.Time - a.Time;
                float u = span > 1e-5f ? Mathf.Clamp01((t - a.Time) / span) : 0f;
                _series[k] = Mathf.Lerp(a.Motion, b.Motion, u);
                mean += _series[k];
            }

            mean /= n;
            float meanFocus = 0f;
            float meanGlobalness = 0f;
            int windowSamples = 0;
            for (int i = 0; i < count; i++)
            {
                if (_samples[i].Time < start)
                {
                    continue;
                }

                meanFocus += _samples[i].Focus;
                meanGlobalness += _samples[i].Globalness;
                windowSamples++;
            }

            meanFocus /= Mathf.Max(1, windowSamples);
            meanGlobalness /= Mathf.Max(1, windowSamples);

            if (mean < thresholds.MinimumMotion)
            {
                LoseCadence();
                return Status.NoMotion;
            }

            if (meanFocus < thresholds.MinimumFocus || meanGlobalness > thresholds.MaximumGlobalness)
            {
                _candidateRpm = -1f;
                return Hold(now, Status.Shaking);
            }

            for (int k = 0; k < n; k++)
            {
                _series[k] -= mean;
            }

            int minimumLag = Mathf.RoundToInt(MinimumPeriodSeconds * ResampleRate);
            int maximumLag = Mathf.Min(Mathf.RoundToInt(MaximumPeriodSeconds * ResampleRate), n / 2);
            if (maximumLag <= minimumLag + 2)
            {
                return Hold(now, Status.Waiting);
            }

            if (_correlation.Length < maximumLag + 2)
            {
                _correlation = new float[maximumLag + 2];
            }

            float best = -1f;
            for (int lag = minimumLag - 1; lag <= maximumLag + 1; lag++)
            {
                _correlation[lag] = lag >= 1 && lag < n - 1 ? Correlate(n, lag) : -1f;
                if (lag >= minimumLag && lag <= maximumLag && _correlation[lag] > best)
                {
                    best = _correlation[lag];
                }
            }

            if (best < thresholds.MinimumCorrelation)
            {
                _candidateRpm = -1f;
                return Hold(now, Status.NoRhythm);
            }

            // The shortest strong peak is the motion period; longer peaks are its multiples.
            int chosen = -1;
            for (int lag = minimumLag; lag <= maximumLag; lag++)
            {
                float c = _correlation[lag];
                if (c >= best * 0.85f && c >= thresholds.MinimumCorrelation &&
                    c >= _correlation[lag - 1] && c >= _correlation[lag + 1])
                {
                    chosen = lag;
                    break;
                }
            }

            if (chosen < 0)
            {
                _candidateRpm = -1f;
                return Hold(now, Status.NoRhythm);
            }

            float previous = _correlation[chosen - 1];
            float peak = _correlation[chosen];
            float next = _correlation[chosen + 1];
            float denominator = previous - 2f * peak + next;
            float offset = denominator < -1e-6f ? Mathf.Clamp(0.5f * (previous - next) / denominator, -0.5f, 0.5f) : 0f;
            float period = (chosen + offset) / ResampleRate;
            float revolutionSeconds = bothLegsVisible ? period * 2f : period;
            float rpm = 60f / revolutionSeconds;
            if (rpm < MinimumRpm || rpm > MaximumRpm)
            {
                _candidateRpm = -1f;
                return Hold(now, Status.OutOfRange);
            }

            if (HasCadence && Mathf.Abs(rpm - _rpm) <= _rpm * 0.25f)
            {
                _rpm = Mathf.Lerp(_rpm, rpm, 0.5f);
                MarkGood(now, peak, thresholds);
                return Status.Detected;
            }

            if (_candidateRpm > 0f && now - _candidateTime <= 1f &&
                Mathf.Abs(rpm - _candidateRpm) <= Mathf.Max(6f, _candidateRpm * 0.12f))
            {
                _candidateRpm = Mathf.Lerp(_candidateRpm, rpm, 0.5f);
                _candidateTime = now;
                _candidateSeen++;
                if (_candidateSeen >= 2)
                {
                    _rpm = _candidateRpm;
                    _candidateRpm = -1f;
                    _candidateSeen = 0;
                    MarkGood(now, peak, thresholds);
                    return Status.Detected;
                }

                return Hold(now, Status.Confirming);
            }

            _candidateRpm = rpm;
            _candidateTime = now;
            _candidateSeen = 1;
            return Hold(now, Status.Confirming);
        }

        private float Correlate(int n, int lag)
        {
            float product = 0f;
            float energyA = 0f;
            float energyB = 0f;
            for (int i = 0; i < n - lag; i++)
            {
                float a = _series[i];
                float b = _series[i + lag];
                product += a * b;
                energyA += a * a;
                energyB += b * b;
            }

            float denominator = Mathf.Sqrt(energyA * energyB);
            return denominator > 0.0001f ? product / denominator : 0f;
        }

        private void MarkGood(float now, float correlation, Thresholds thresholds)
        {
            _lastGoodTime = now;
            Confidence = Mathf.InverseLerp(thresholds.MinimumCorrelation, 0.85f, correlation);
        }

        private Status Hold(float now, Status status)
        {
            if (HasCadence && now - _lastGoodTime > HoldSeconds)
            {
                LoseCadence();
            }

            return status;
        }
    }
}
