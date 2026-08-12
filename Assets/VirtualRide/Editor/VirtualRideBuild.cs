#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VirtualRide.Editor
{
    public static class VirtualRideBuild
    {
        private const string ScenePath = "Assets/Scenes/VirtualRide.unity";
        private const string BuildPath = "Builds/Windows/VirtualRide.exe";

        [MenuItem("Tools/Virtual Ride/Build Windows")]
        public static void BuildWindowsFromMenu()
        {
            BuildWindows();
        }

        public static void BuildWindowsBatch()
        {
            BuildReport report = BuildWindows();
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        private static BuildReport BuildWindows()
        {
            string directory = Path.GetDirectoryName(BuildPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Virtual Ride Windows build: {report.summary.result} ({report.summary.totalSize} bytes)");
            return report;
        }
    }
}
#endif
