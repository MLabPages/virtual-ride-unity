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

            _app.EndResearchSession("smoke_test_completed");

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

            if (_app.DisplaySpeedKph < 15f)
            {
                reason = "Ride speed did not follow the simulated 18 km/h input.";
                return false;
            }

            if (_app.Session.DistanceMetres < 5f || _app.RouteDistance < 5f)
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

            if (!IsUsefulResearchFile(_app.ResearchRecorder.CsvPath, 6) ||
                !IsUsefulResearchFile(_app.ResearchRecorder.SummaryPath, 1))
            {
                reason = "Research CSV or summary JSON was not written with enough samples.";
                return false;
            }

            reason = "Runtime, generated world, UI states, speed mapping, distance progression, and research logging passed.";
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
                $"  \"speedKph\": {_app.DisplaySpeedKph.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"distanceMetres\": {_app.Session.DistanceMetres.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"routeDistanceMetres\": {_app.RouteDistance.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"routeLengthMetres\": {_app.Route.TotalLength.ToString("0.00", CultureInfo.InvariantCulture)},\n" +
                $"  \"rendererCount\": {rendererCount},\n" +
                $"  \"researchCsv\": \"{EscapeJson(_app.ResearchRecorder.CsvPath)}\",\n" +
                $"  \"researchSummary\": \"{EscapeJson(_app.ResearchRecorder.SummaryPath)}\",\n" +
                $"  \"researchSamples\": {_app.ResearchRecorder.SampleCount},\n" +
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
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
