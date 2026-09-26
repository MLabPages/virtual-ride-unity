using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using VirtualRide.Input;
using VirtualRide.World;

namespace VirtualRide.Core
{
    /// <summary>
    /// Player-only smoke test used by the automated verification command.
    /// It is never attached during ordinary launches.
    /// </summary>
    public sealed class VirtualRideSmokeTest : MonoBehaviour
    {
        private const string RequestArgument = "-virtualRideSmokeTest";
        private const string OutputArgument = "-smokeOutput";
        private VirtualRideApp _app;
        private string _outputDirectory;
        private string _manualCsvPath;
        private string _manualEventsPath;
        private string _manualSummaryPath;
        private string _timedCsvPath;
        private string _timedSummaryPath;
        private string _timedStopReason = string.Empty;
        private float _rideSpeedKph;
        private float _rideDistanceMetres;
        private float _rideRouteDistanceMetres;
        private readonly List<string> _regressions = new List<string>();
        private readonly List<float> _frameTimes = new List<float>();

        private void Check(bool condition, string reason)
        {
            if (!condition) _regressions.Add(reason);
        }

        public static bool IsRequested => Array.Exists(
            Environment.GetCommandLineArgs(),
            argument => string.Equals(argument, RequestArgument, StringComparison.OrdinalIgnoreCase));

        private void Awake()
        {
            _app = GetComponent<VirtualRideApp>();
            _outputDirectory = ResolveOutputDirectory();
        }

        private IEnumerator Start()
        {
            foreach (float yaw in new[] { -179f, 0f, 135f })
            {
                Quaternion head = Quaternion.Euler(25f, yaw, 18f);
                Quaternion origin = QuestHeadPoseTracking.HorizontalOrigin(head);
                Check(Vector3.Angle(origin * Vector3.up, Vector3.up) < 0.01f,
                    "Quest recenter tilted the horizon");
                Vector3 facing = Quaternion.Inverse(origin) * (head * Vector3.forward);
                Check(Mathf.Abs(facing.x) < 0.001f && facing.z > 0f,
                    "Quest recenter did not align horizontal heading");
                Check(Mathf.Abs(facing.y - (head * Vector3.forward).y) < 0.001f,
                    "Quest recenter removed natural head pitch");
            }
            Check(float.IsFinite(QuestHeadPoseTracking.HorizontalOrigin(Quaternion.Euler(90f, 30f, 0f)).w),
                "Quest vertical gaze recenter produced an invalid rotation");
            Directory.CreateDirectory(_outputDirectory);
            yield return null;
            yield return new WaitForEndOfFrame();

            string helpScreenshot = Path.Combine(_outputDirectory, "help.png");
            ScreenCapture.CaptureScreenshot(helpScreenshot);
            yield return WaitForFile(helpScreenshot, 8f);

            _app.HideHelp();
            _app.UseKeyboardInput();
            _app.ResearchRecorder.SetDataDirectoryForTesting(Path.Combine(_outputDirectory, "research-data"));
            _app.Session.Tick(18f, 5f);
            _app.PreviewRouteForTesting(.15f);
            float practiceDistance = _app.Session.DistanceMetres;
            float practiceRoute = _app.RouteDistance;
            Check(!_app.BeginResearchSession("", "invalid"), "Blank ID accepted");
            Check(!_app.BeginResearchSession("SMOKE01", "invalid", float.NaN), "NaN duration accepted");
            Check(!_app.BeginResearchSession("SMOKE01", "invalid", float.PositiveInfinity), "Infinite duration accepted");
            Check(!_app.BeginResearchSession("SMOKE/01", "invalid"), "ID was silently renamed");
            Check(_app.Session.DistanceMetres == practiceDistance && _app.RouteDistance == practiceRoute,
                "Failed start reset practice data");
            _app.ToggleResearchPanel();
            bool wasFullscreen = Screen.fullScreen;
            bool wasPaused = _app.IsPaused;
            foreach (KeyCode key in new[] { KeyCode.C, KeyCode.K, KeyCode.Space, KeyCode.H, KeyCode.F, KeyCode.R })
                _app.HandleShortcut(key);
            Check(ReferenceEquals(_app.ActiveInput, _app.KeyboardInput) && wasPaused == _app.IsPaused &&
                Screen.fullScreen == wasFullscreen && _app.ResearchPanelVisible && _app.Session.DistanceMetres == practiceDistance,
                "Typing in the research form triggered a ride shortcut");
            yield return null;
            Check(!_app.KeyboardInput.ControlsEnabled, "Speed keys remained active behind the research form");
            _app.HideResearchPanel();
            if (!_app.BeginResearchSession("SMOKE01", "automated"))
            {
                WriteResult(false, _app.ResearchRecorder.LastMessage, helpScreenshot, string.Empty, string.Empty);
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - " + _app.ResearchRecorder.LastMessage);
                Application.Quit(1);
                yield break;
            }

            _app.UseCameraInput();
            Check(_app.RouteDistance == 0f && _app.Session.DistanceMetres == 0f && _app.DisplaySpeedKph == 0f,
                "Successful start did not reset to the common starting line");
            _app.ToggleComfortMode(); _app.ToggleMinimalHud(); _app.ToggleWind();
            Check(_app.ComfortMode && !_app.MinimalHud && _app.WindEnabled, "Presentation settings changed while recording");
            if (!ReferenceEquals(_app.ActiveInput, _app.KeyboardInput) || !_app.IsInputLocked)
            {
                _app.EndResearchSession("smoke_test_failed");
                WriteResult(false, "Recording did not lock the keyboard input source.", helpScreenshot, string.Empty, string.Empty);
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - input source was not locked");
                Application.Quit(1);
                yield break;
            }

            if (string.IsNullOrEmpty(_app.BlockedActionMessage))
            {
                _app.EndResearchSession("smoke_test_failed");
                WriteResult(false, "Blocked input switch did not produce a visible message.", helpScreenshot, string.Empty, string.Empty);
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - input lock was silent");
                Application.Quit(1);
                yield break;
            }

            if (!_app.AddResearchEventMarker(ResearchSessionRecorder.MarkerInstruction, "start pedaling") ||
                !_app.AddResearchEventMarker(ResearchSessionRecorder.MarkerRest) ||
                !_app.AddResearchEventMarker(ResearchSessionRecorder.MarkerNote, "custom marker"))
            {
                string markerError = _app.ResearchRecorder.LastMessage;
                _app.EndResearchSession("smoke_test_failed");
                WriteResult(false, "Failed to add research event markers: " + markerError, helpScreenshot, string.Empty, string.Empty);
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - " + markerError);
                Application.Quit(1);
                yield break;
            }

            _app.ToggleResearchPanel();
            yield return new WaitForEndOfFrame();
            string researchScreenshot = Path.Combine(_outputDirectory, "research.png");
            ScreenCapture.CaptureScreenshot(researchScreenshot);
            yield return WaitForFile(researchScreenshot, 8f);
            _app.HideResearchPanel();

            _app.KeyboardInput.SetSpeed(18f);

            float testEndsAt = Time.realtimeSinceStartup + 10f;
            while (_app.DisplaySpeedKph < 16f && Time.realtimeSinceStartup < testEndsAt)
            {
                yield return null;
            }

            // Keep riding briefly after reaching speed so distance progression is visible.
            float distanceSampleEndsAt = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < distanceSampleEndsAt)
            {
                _frameTimes.Add(Time.unscaledDeltaTime * 1000f);
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            string rideScreenshot = Path.Combine(_outputDirectory, "ride.png");
            ScreenCapture.CaptureScreenshot(rideScreenshot);
            yield return WaitForFile(rideScreenshot, 8f);

            _rideSpeedKph = _app.DisplaySpeedKph;
            _rideDistanceMetres = _app.Session.DistanceMetres;
            _rideRouteDistanceMetres = _app.RouteDistance;
            Check(Mathf.Abs(_rideDistanceMetres - _rideRouteDistanceMetres) < .01f,
                "Logged distance diverged from actual route motion at low speed");

            Check(!_app.BeginResearchSession("DUPLICATE", "invalid") && _app.Session.DistanceMetres == _rideDistanceMetres,
                "Duplicate start changed an active trial");
            _app.ResetSession();
            Check(_app.Session.DistanceMetres == _rideDistanceMetres, "Reset changed an active trial");

            _app.TogglePause();
            float pauseDistance = _app.Session.DistanceMetres;
            yield return new WaitForSecondsRealtime(.15f);
            Check(_app.DisplaySpeedKph == 0f && _app.Session.DistanceMetres == pauseDistance && _app.ResearchRecorder.IsRecording,
                "Pause changed distance or ended the trial clock");
            _app.TogglePause();

            _app.EndResearchSession("smoke_test_completed");
            _manualCsvPath = _app.ResearchRecorder.CsvPath;
            _manualEventsPath = _app.ResearchRecorder.EventsPath;
            _manualSummaryPath = _app.ResearchRecorder.SummaryPath;
            Check(_app.IsPaused && _app.DisplaySpeedKph == 0f, "Manual end did not stop motion");
            float stoppedDistance = _app.Session.DistanceMetres;
            yield return new WaitForSecondsRealtime(.25f);
            Check(_app.Session.DistanceMetres == stoppedDistance, "Manual end kept accumulating distance");

            if (!_app.BeginResearchSession("SMOKE01", "timed", 0.6f))
            {
                WriteResult(false, _app.ResearchRecorder.LastMessage, helpScreenshot, researchScreenshot, rideScreenshot);
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - " + _app.ResearchRecorder.LastMessage);
                Application.Quit(1);
                yield break;
            }

            float timedDeadline = Time.realtimeSinceStartup + 3f;
            while (_app.ResearchRecorder.IsRecording && Time.realtimeSinceStartup < timedDeadline)
            {
                yield return null;
            }

            _timedCsvPath = _app.ResearchRecorder.CsvPath;
            _timedSummaryPath = _app.ResearchRecorder.SummaryPath;
            _timedStopReason = _app.ResearchRecorder.StopReason;
            Check(_app.IsPaused && _app.DisplaySpeedKph == 0f && _app.ResearchPanelVisible, "Timed end did not stop and show results");
            Check(Mathf.Abs(_app.ResearchRecorder.RecordingElapsedSeconds - .6f) < .0001f, "Timed trial overshot its duration");
            stoppedDistance = _app.Session.DistanceMetres;
            yield return new WaitForSecondsRealtime(.25f);
            Check(_app.Session.DistanceMetres == stoppedDistance, "Timed end kept accumulating distance");
            yield return CheckFixedVideoSpeed();
            CheckCadenceEstimator();
            CheckWriterFailure();
            yield return CaptureAdditionalViews();

            bool passed = Validate(helpScreenshot, researchScreenshot, rideScreenshot, out string reason);
            WriteResult(passed, reason, helpScreenshot, researchScreenshot, rideScreenshot);

            if (passed)
            {
                Debug.Log("VIRTUAL_RIDE_SMOKE_TEST: PASS");
                Application.Quit(0);
            }
            else
            {
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - " + reason);
                Application.Quit(1);
            }
        }

        private IEnumerator CheckFixedVideoSpeed()
        {
            _app.KeyboardInput.SetSpeed(0f);
            Check(_app.SetVideoSpeedMode(VirtualRideApp.VideoSpeedMode.Fixed) && _app.SetFixedVideoSpeed(12f),
                "Fixed video speed could not be selected before recording");
            _app.TogglePedalPreview();
            if (!_app.BeginResearchSession("SMOKE01", "fixed"))
            {
                Check(false, "Fixed video speed trial could not start: " + _app.ResearchRecorder.LastMessage);
                yield break;
            }

            Check(!_app.SetVideoSpeedMode(VirtualRideApp.VideoSpeedMode.PedalLinked) && !_app.SetFixedVideoSpeed(20f),
                "Video speed condition changed while recording");
            _app.TogglePedalPreview();
            Check(!_app.PedalPreviewVisible, "Pedal preview visibility changed while recording");

            float deadline = Time.realtimeSinceStartup + 6f;
            while (_app.DisplaySpeedKph < 11.9f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(.3f);
            Check(Mathf.Abs(_app.DisplaySpeedKph - 12f) < .05f && _app.ActiveSample.SpeedKph == 0f,
                "Fixed video speed followed the pedal input instead of the fixed value");
            _app.EndResearchSession("smoke_test_completed");
            string summary = ReadAllText(_app.ResearchRecorder.SummaryPath);
            Check(summary.Contains("\"videoSpeedMode\": \"fixed\"") && summary.Contains("\"fixedVideoSpeedKph\": 12") &&
                summary.Contains("\"pedalPreviewVisible\": false"),
                "Fixed video speed condition was not recorded in the summary");

            _app.SetVideoSpeedMode(VirtualRideApp.VideoSpeedMode.PedalLinked);
            _app.TogglePedalPreview();
            Check(!_app.IsVideoSpeedFixed && _app.PedalPreviewVisible, "Video speed settings could not be restored");
        }

        /// <summary>
        /// Feeds synthetic pedal motion with known cadence, frame jitter, noise and a second
        /// harmonic into the same estimator the camera uses, then records accuracy and latency.
        /// </summary>
        private void CheckCadenceEstimator()
        {
            var thresholds = new CadenceEstimator.Thresholds(1.8f, 0.38f, 1.55f, 0.78f);
            var cases = new[] { (45f, true), (60f, true), (80f, true), (100f, true), (60f, false) };
            var json = new StringBuilder("{\n  \"algorithm\": \"" + CadenceEstimator.AlgorithmVersion + "\",\n  \"cases\": [\n");
            uint seed = 20260923u;
            for (int c = 0; c < cases.Length; c++)
            {
                float targetRpm = cases[c].Item1;
                bool bothLegs = cases[c].Item2;
                const float pedalStart = 2f;
                const float pedalEnd = 10f;
                var estimator = new CadenceEstimator();
                float nextAnalysis = 0f;
                float? detectedAt = null;
                float? lostAt = null;
                float rpmSum = 0f;
                int rpmCount = 0;
                int steadyAnalyses = 0;
                float t = 0f;
                while (t < 13f)
                {
                    seed = seed * 1664525u + 1013904223u;
                    float jitter = ((seed >> 8) / 16777216f - 0.5f) * 0.008f;
                    t += 1f / 30f + jitter;
                    seed = seed * 1664525u + 1013904223u;
                    float noise = ((seed >> 8) / 16777216f - 0.5f) * 0.8f;
                    bool pedalling = t >= pedalStart && t < pedalEnd;
                    float motion;
                    if (pedalling)
                    {
                        float cycles = (t - pedalStart) * targetRpm / 60f * (bothLegs ? 2f : 1f);
                        motion = 1.2f + 5f * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * cycles)) +
                            1.2f * Mathf.Cos(4f * Mathf.PI * cycles) + noise;
                    }
                    else
                    {
                        motion = 0.5f + noise * 0.4f;
                    }

                    estimator.AddSample(t, Mathf.Max(0f, motion), pedalling ? 2.4f : 1.2f, pedalling ? 0.3f : 0.1f);
                    if (t < nextAnalysis)
                    {
                        continue;
                    }

                    nextAnalysis = t + CadenceEstimator.AnalysisInterval;
                    estimator.Analyze(t, thresholds, bothLegs);
                    if (pedalling && !detectedAt.HasValue && estimator.HasCadence)
                    {
                        detectedAt = t - pedalStart;
                    }

                    if (pedalling && t >= pedalStart + 5f)
                    {
                        steadyAnalyses++;
                        if (estimator.HasCadence)
                        {
                            rpmSum += estimator.Rpm;
                            rpmCount++;
                        }
                    }

                    if (!pedalling && t >= pedalEnd && !lostAt.HasValue && !estimator.HasCadence)
                    {
                        lostAt = t - pedalEnd;
                    }
                }

                float? meanRpm = rpmCount > 0 ? rpmSum / rpmCount : (float?)null;
                float? errorPercent = meanRpm.HasValue ? (meanRpm.Value - targetRpm) / targetRpm * 100f : (float?)null;
                float coverage = steadyAnalyses > 0 ? rpmCount / (float)steadyAnalyses : 0f;
                string label = targetRpm.ToString("0", CultureInfo.InvariantCulture) + (bothLegs ? " rpm both legs" : " rpm one leg");
                // One visible leg gives one motion period per revolution, so it needs a longer window.
                Check(detectedAt.HasValue && detectedAt.Value <= (bothLegs ? 2.2f : 3.2f), "Cadence " + label + " was detected too late");
                Check(errorPercent.HasValue && Mathf.Abs(errorPercent.Value) <= 3f, "Cadence " + label + " error exceeded 3%");
                Check(coverage >= 0.95f, "Cadence " + label + " dropped out while pedalling steadily");
                Check(lostAt.HasValue && lostAt.Value <= 1.0f, "Stopping at " + label + " was detected too late");
                json.Append("    { \"targetRpm\": ").Append(targetRpm.ToString("0", CultureInfo.InvariantCulture))
                    .Append(", \"bothLegsVisible\": ").Append(bothLegs ? "true" : "false")
                    .Append(", \"detectSeconds\": ").Append(Format(detectedAt))
                    .Append(", \"meanRpm\": ").Append(Format(meanRpm))
                    .Append(", \"errorPercent\": ").Append(Format(errorPercent))
                    .Append(", \"steadyCoverage\": ").Append(Format(coverage))
                    .Append(", \"stopSeconds\": ").Append(Format(lostAt))
                    .Append(c < cases.Length - 1 ? " },\n" : " }\n");
            }

            json.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(_outputDirectory, "cadence-estimator.json"), json.ToString());
        }

        private static string Format(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "null";
        }

        private void CheckWriterFailure()
        {
            var recorder = new ResearchSessionRecorder(Path.Combine(_outputDirectory, "regression-data"));
            Check(recorder.Start("SMOKE01", "escaping", _app), "Escaping test could not start");
            recorder.AddEventMarker("note", "=SUM(1,2)\t\"quoted\"\u0001");
            Check(recorder.Stop(_app), "Escaping test could not save");
            string escaped = ReadAllText(recorder.SummaryPath);
            Check(escaped.Contains("\\u0009") && escaped.Contains("\\u0001"), "Control characters broke summary JSON");
            Check(ReadAllText(recorder.EventsPath).Contains("'=SUM"), "Spreadsheet formula note was not protected");

            Check(recorder.Start("SMOKE01", "writefailure", _app), "Fault test could not start");
            FieldInfo field = typeof(ResearchSessionRecorder).GetField("_writer", BindingFlags.Instance | BindingFlags.NonPublic);
            ((StreamWriter)field.GetValue(recorder)).Dispose();
            var fault = new StreamWriter(new FailingStream());
            fault.Write("simulated disk error");
            field.SetValue(recorder, fault);
            Check(!recorder.Stop(_app) && !recorder.IsRecording && recorder.HasError && recorder.StopReason == "write_failed",
                "Writer error left a live session or claimed successful saving");
            using (File.Open(recorder.EventsPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        }

        private sealed class FailingStream : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count) { throw new IOException("Simulated QA disk failure"); }
            public override void Flush() { throw new IOException("Simulated QA flush failure"); }
        }

        private IEnumerator CaptureAdditionalViews()
        {
            _app.HideResearchPanel();
            _app.KeyboardInput.SetSpeed(0f);
            if (_app.IsPaused) _app.TogglePause();
            _app.ToggleMinimalHud();
            float signView = (DistanceSigns.SpacingMetres * 2f - 20f) / _app.Route.TotalLength;
            float[] progress = { .025f, .26f, .50f, .72f, .89f, signView };
            string[] names = { "forest", "lake", "village", "meadow", "hills", "distance-sign" };
            for (int i=0; i<progress.Length; i++)
            {
                _app.PreviewRouteForTesting(progress[i]);
                yield return null;
                yield return new WaitForEndOfFrame();
                string screenshot = Path.Combine(_outputDirectory, names[i]+".png");
                ScreenCapture.CaptureScreenshot(screenshot);
                yield return WaitForFile(screenshot,8f);
                Check(IsUsefulScreenshot(screenshot), names[i]+" screenshot missing");
            }
            Check(Camera.main.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null,
                "Sky material not active in the player");
            Check(Camera.main.fieldOfView == 68f && Mathf.Abs(Mathf.DeltaAngle(0, Camera.main.transform.parent.localEulerAngles.z)) < .01f,
                "Comfort view changed FOV or tilted the horizon");
            _app.ToggleMinimalHud();
            _app.ToggleResearchPanel();
            Screen.SetResolution(960,540,false);
            for (int frame=0;frame<6;frame++) yield return null;
            yield return new WaitForEndOfFrame();
            string setup = Path.Combine(_outputDirectory,"setup-small.png");
            ScreenCapture.CaptureScreenshot(setup);
            yield return WaitForFile(setup,8f);
            _frameTimes.Sort();
            float median = _frameTimes.Count > 0 ? _frameTimes[_frameTimes.Count/2] : 0;
            float p95 = _frameTimes.Count > 0 ? _frameTimes[Mathf.Min(_frameTimes.Count-1,Mathf.FloorToInt(_frameTimes.Count*.95f))] : 0;
            File.WriteAllText(Path.Combine(_outputDirectory,"performance.json"),
                "{\"samples\":"+_frameTimes.Count+",\"medianFrameMs\":"+median.ToString("0.00",CultureInfo.InvariantCulture)+
                ",\"p95FrameMs\":"+p95.ToString("0.00",CultureInfo.InvariantCulture)+"}");
        }

        private bool Validate(
            string helpScreenshot,
            string researchScreenshot,
            string rideScreenshot,
            out string reason)
        {
            int rendererCount = FindObjectsByType<Renderer>().Length;
            if (_regressions.Count > 0)
            {
                reason = string.Join("; ", _regressions);
                return false;
            }
            if (Camera.main == null)
            {
                reason = "Main camera was not created.";
                return false;
            }

            if (_app.Route == null || _app.Route.TotalLength < 800f)
            {
                reason = "Generated route is shorter than expected.";
                return false;
            }

            if (rendererCount < 250)
            {
                reason = "Procedural world did not create enough visible objects.";
                return false;
            }

            if (_rideSpeedKph < 15f)
            {
                reason = "Ride speed did not follow the simulated 18 km/h input.";
                return false;
            }

            if (_rideDistanceMetres < 5f || _rideRouteDistanceMetres < 5f)
            {
                reason = "Distance did not increase while the rider was moving.";
                return false;
            }

            if (!IsUsefulScreenshot(helpScreenshot) ||
                !IsUsefulScreenshot(researchScreenshot) ||
                !IsUsefulScreenshot(rideScreenshot))
            {
                reason = "One or more verification screenshots were not written.";
                return false;
            }

            if (!IsUsefulResearchFile(_manualCsvPath, 6) ||
                !IsUsefulResearchFile(_manualSummaryPath, 1) ||
                !IsUsefulResearchFile(_manualEventsPath, 4))
            {
                reason = "Research CSV, events file, or summary JSON was not written with enough samples.";
                return false;
            }

            if (!ValidateLockedSessionFiles(out reason))
            {
                return false;
            }

            if (!ValidateTimedSessionFiles(out reason))
            {
                return false;
            }

            reason = "Runtime, generated world, UI states, speed mapping, distance progression, input lock, event markers, summary metadata, and trial auto-stop passed.";
            return true;
        }

        private bool ValidateLockedSessionFiles(out string reason)
        {
            string csv = ReadAllText(_manualCsvPath);
            if (csv.IndexOf("event_marker", StringComparison.Ordinal) < 0 ||
                csv.IndexOf("\n2,", StringComparison.Ordinal) < 0)
            {
                reason = "Research CSV is missing schema version 2 or the event_marker column.";
                return false;
            }

            if (csv.IndexOf("カメラ計測", StringComparison.Ordinal) >= 0)
            {
                reason = "Research CSV shows the input mode changed after recording started.";
                return false;
            }

            string events = ReadAllText(_manualEventsPath);
            if (events.IndexOf("instruction", StringComparison.Ordinal) < 0 ||
                events.IndexOf("rest", StringComparison.Ordinal) < 0 ||
                events.IndexOf("note", StringComparison.Ordinal) < 0 ||
                events.IndexOf("start pedaling", StringComparison.Ordinal) < 0)
            {
                reason = "Events sidecar does not contain the timestamped markers added during the session.";
                return false;
            }

            string summary = ReadAllText(_manualSummaryPath);
            string[] required = {
                "\"schemaVersion\": \"2\"",
                "\"startingInputMode\": \"キーボード\"",
                "\"finalInputMode\": \"キーボード\"",
                "\"inputLocked\": true",
                "\"applicationVersion\":",
                "\"unityVersion\":",
                "\"measurementValidated\": false",
                "\"cameraFramesSaved\": false",
                "\"bothLegsVisible\":",
                "\"sensitivity\":",
                "\"metersPerRevolution\":",
                "\"eventCount\": 5",
                "\"markerType\": \"pause\"",
                "\"markerType\": \"resume\"",
                "\"markerType\": \"instruction\"",
                "\"eventsFile\":",
                "\"visualRevision\": \"valley-zones-2026.09.2\"", "\"speedMapping\"", "\"distanceSignsVisible\": true",
                "\"comfortMode\": true", "\"minimalHud\": false", "\"routeStartMetres\": 0"
            };

            for (int i = 0; i < required.Length; i++)
            {
                if (summary.IndexOf(required[i], StringComparison.Ordinal) < 0)
                {
                    reason = "Summary JSON is missing required research metadata: " + required[i];
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private bool ValidateTimedSessionFiles(out string reason)
        {
            if (_app.ResearchRecorder.IsRecording)
            {
                reason = "Timed trial did not auto-stop after the configured duration.";
                return false;
            }

            if (!string.Equals(
                    _timedStopReason,
                    ResearchSessionRecorder.StopReasonTrialDurationElapsed,
                    StringComparison.Ordinal))
            {
                reason = "Timed trial stop reason was not trial_duration_elapsed.";
                return false;
            }

            if (!IsUsefulResearchFile(_timedCsvPath, 2) || !IsUsefulResearchFile(_timedSummaryPath, 1))
            {
                reason = "Timed trial CSV or summary JSON was not written.";
                return false;
            }

            string summary = ReadAllText(_timedSummaryPath);
            if (summary.IndexOf("\"stopReason\": \"trial_duration_elapsed\"", StringComparison.Ordinal) < 0 ||
                summary.IndexOf("\"trialDurationSeconds\":", StringComparison.Ordinal) < 0)
            {
                reason = "Timed trial summary JSON does not record auto-stop metadata.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void WriteResult(
            bool passed,
            string reason,
            string helpScreenshot,
            string researchScreenshot,
            string rideScreenshot)
        {
            int rendererCount = FindObjectsByType<Renderer>().Length;
            string json =
                "{\n" +
                $"  \"passed\": {(passed ? "true" : "false")},\n" +
                $"  \"reason\": \"{EscapeJson(reason)}\",\n" +
                $"  \"speedKph\": {_rideSpeedKph.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"distanceMetres\": {_rideDistanceMetres.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"routeDistanceMetres\": {_rideRouteDistanceMetres.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"routeLengthMetres\": {_app.Route.TotalLength.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"rendererCount\": {rendererCount},\n" +
                $"  \"researchCsv\": \"{EscapeJson(_manualCsvPath ?? _app.ResearchRecorder.CsvPath)}\",\n" +
                $"  \"researchEvents\": \"{EscapeJson(_manualEventsPath ?? _app.ResearchRecorder.EventsPath)}\",\n" +
                $"  \"researchSummary\": \"{EscapeJson(_manualSummaryPath ?? _app.ResearchRecorder.SummaryPath)}\",\n" +
                $"  \"researchSamples\": {_app.ResearchRecorder.SampleCount},\n" +
                $"  \"timedCsv\": \"{EscapeJson(_timedCsvPath ?? string.Empty)}\",\n" +
                $"  \"timedStopReason\": \"{EscapeJson(_timedStopReason)}\",\n" +
                $"  \"helpScreenshot\": \"{EscapeJson(helpScreenshot)}\",\n" +
                $"  \"researchScreenshot\": \"{EscapeJson(researchScreenshot)}\",\n" +
                $"  \"rideScreenshot\": \"{EscapeJson(rideScreenshot)}\"\n" +
                "}\n";
            File.WriteAllText(Path.Combine(_outputDirectory, "result.json"), json);
        }

        private static IEnumerator WaitForFile(string path, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsUsefulScreenshot(path))
                {
                    yield break;
                }

                yield return null;
            }
        }

        private static bool IsUsefulScreenshot(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 4096;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool IsUsefulResearchFile(string path, int minimumLines)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }

                int lines = 0;
                using (StreamReader reader = File.OpenText(path))
                {
                    while (reader.ReadLine() != null)
                    {
                        lines++;
                    }
                }

                return lines >= minimumLines;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static string ReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        private static string ResolveOutputDirectory()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], OutputArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(arguments[i + 1]);
                }
            }

            return Path.Combine(Application.temporaryCachePath, "VirtualRideSmokeTest");
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
