using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

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
        private float _rideSpeedKph;
        private float _rideDistanceMetres;
        private float _rideRouteDistanceMetres;

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
            Directory.CreateDirectory(_outputDirectory);
            yield return null;
            yield return new WaitForEndOfFrame();

            string helpScreenshot = Path.Combine(_outputDirectory, "help.png");
            ScreenCapture.CaptureScreenshot(helpScreenshot);
            yield return WaitForFile(helpScreenshot, 8f);

            _app.HideHelp();
            _app.UseKeyboardInput();
            _app.ResearchRecorder.SetDataDirectoryForTesting(Path.Combine(_outputDirectory, "research-data"));
            if (!_app.BeginResearchSession("SMOKE01", "automated"))
            {
                WriteResult(false, _app.ResearchRecorder.LastMessage, helpScreenshot, string.Empty, string.Empty);
                Debug.LogError("VIRTUAL_RIDE_SMOKE_TEST: FAIL - " + _app.ResearchRecorder.LastMessage);
                Application.Quit(1);
                yield break;
            }

            _app.UseCameraInput();
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
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            string rideScreenshot = Path.Combine(_outputDirectory, "ride.png");
            ScreenCapture.CaptureScreenshot(rideScreenshot);
            yield return WaitForFile(rideScreenshot, 8f);

            _rideSpeedKph = _app.DisplaySpeedKph;
            _rideDistanceMetres = _app.Session.DistanceMetres;
            _rideRouteDistanceMetres = _app.RouteDistance;

            _app.EndResearchSession("smoke_test_completed");
            _manualCsvPath = _app.ResearchRecorder.CsvPath;
            _manualEventsPath = _app.ResearchRecorder.EventsPath;
            _manualSummaryPath = _app.ResearchRecorder.SummaryPath;

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

        private bool Validate(
            string helpScreenshot,
            string researchScreenshot,
            string rideScreenshot,
            out string reason)
        {
            int rendererCount = FindObjectsByType<Renderer>().Length;
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
                "\"eventCount\": 3",
                "\"markerType\": \"instruction\"",
                "\"eventsFile\":"
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
                    _app.ResearchRecorder.StopReason,
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
                $"  \"timedStopReason\": \"{EscapeJson(_app.ResearchRecorder.StopReason)}\",\n" +
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
