#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace VirtualRide.Editor
{
    public static class VirtualRideBuild
    {
        private const string ScenePath = "Assets/Scenes/VirtualRide.unity";
        private const string BuildPath = "Builds/Windows/VirtualRide.exe";
        private const string QuestBuildPath = "Builds/Quest/VirtualRide.apk";
        private const string PendingQuestBuild = "VirtualRide.PendingQuestBuild";

        [MenuItem("Tools/Virtual Ride/Build Quest Android APK")]
        public static void BuildQuestAndroidFromMenu()
        {
            RequireAndroidSupport();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                // A platform switch reloads assemblies. Resume only after that reload.
                SessionState.SetBool(PendingQuestBuild, true);
                if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    SessionState.EraseBool(PendingQuestBuild);
                    throw new BuildFailedException("Could not switch to Android. Switch platform in Build Profiles and retry.");
                }
                return;
            }
            BuildQuestAndroid();
        }

        [InitializeOnLoadMethod]
        private static void ResumeQuestBuildAfterPlatformSwitch()
        {
            if (!SessionState.GetBool(PendingQuestBuild, false)) return;
            EditorApplication.delayCall += () =>
            {
                SessionState.EraseBool(PendingQuestBuild);
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                    BuildQuestAndroid();
                else
                    Debug.LogError("Quest build was cancelled: Android platform switch did not complete.");
            };
        }

        // Unity.exe -batchmode -quit -projectPath <repo> -buildTarget Android
        //   -executeMethod VirtualRide.Editor.VirtualRideBuild.BuildQuestAndroidBatch -logFile <path>
        public static void BuildQuestAndroidBatch()
        {
            try
            {
                RequireAndroidSupport();
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    throw new BuildFailedException("Quest batch build requires -buildTarget Android on the Unity command line.");
                BuildReport report = BuildQuestAndroid();
                if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/Virtual Ride/Configure Quest Android XR")]
        public static void ConfigureQuestAndroid()
        {
            RequireAndroidSupport();
            // Unity 6000.5 released versions: OpenXR 1.17.1 / XR Management 4.6.1.
            // https://docs.unity3d.com/6000.5/Documentation/Manual/com.unity.xr.openxr.html
            // https://docs.unity3d.com/6000.5/Documentation/Manual/com.unity.xr.management.html
            // OpenXR's required Input System is pinned to Unity 6.5's released 1.20.0:
            // https://docs.unity3d.com/6000.5/Documentation/Manual/com.unity.inputsystem.html
            // Meta's current Quest manifest recommendations: minimum 32 / target 34.
            // https://developers.meta.com/horizon/resources/publish-mobile-manifest/
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.mlabpages.virtualride.quest");
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.optimizedFramePacing = true;
            // Required by OpenXR Meta Quest Support on Unity 6.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.Android.splitApplicationBinary = false;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Both keeps the existing Windows Input/OnGUI paths working while enabling
            // the Input System required by OpenXR. This is also set in ProjectSettings.
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            playerSettings.FindProperty("activeInputHandler").intValue = 2;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();

            // XR settings must be Unity assets. Generate the standard Assets/XR assets
            // and EditorBuildSettings config references on demand, never by hand-written GUIDs.
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                const string settingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
                if (perTarget == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, settingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            XRGeneralSettings android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart = true;
            android.Manager.automaticLoading = true;
            android.Manager.automaticRunning = true;
            if (!XRPackageMetadataStore.AssignLoader(android.Manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android))
                throw new BuildFailedException("Failed to assign the Android OpenXR loader.");
            if (android.Manager.activeLoaders.Any(loader => !(loader is OpenXRLoader)))
                throw new BuildFailedException("Android has another XR loader. Select only OpenXR for the Quest build.");

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXR == null) throw new BuildFailedException("Android OpenXR settings could not be created.");
            openXR.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            openXR.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
            openXR.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            MetaQuestFeature quest = openXR.GetFeature<MetaQuestFeature>();
            OculusTouchControllerProfile touch = openXR.GetFeature<OculusTouchControllerProfile>();
            if (quest == null || touch == null)
                throw new BuildFailedException("OpenXR Meta Quest Support / Oculus Touch profile is unavailable.");
            quest.enabled = true;
            touch.enabled = true;
            // Quest 3 ("eureka") and Quest 3S; these are serialized fields in pinned OpenXR 1.17.1.
            var questSettings = new SerializedObject(quest);
            SerializedProperty devices = questSettings.FindProperty("targetDevices");
            bool foundQuest3 = false;
            for (int i = 0; i < devices.arraySize; i++)
            {
                SerializedProperty device = devices.GetArrayElementAtIndex(i);
                string manifestName = device.FindPropertyRelative("manifestName").stringValue;
                bool supported = manifestName == "eureka" || manifestName == "quest3s";
                device.FindPropertyRelative("enabled").boolValue = supported;
                foundQuest3 |= manifestName == "eureka";
            }
            if (!foundQuest3) throw new BuildFailedException("OpenXR Meta Quest Support does not contain Quest 3.");
            questSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(android);
            EditorUtility.SetDirty(android.Manager);
            EditorUtility.SetDirty(openXR);
            EditorUtility.SetDirty(quest);
            EditorUtility.SetDirty(touch);
            AssetDatabase.SaveAssets();
            Debug.Log("Quest configuration ready: Android ARM64 / IL2CPP / Vulkan / OpenXR / Quest 3 / SDK 32-34. Windows XR settings are unchanged.");
        }

        private static void RequireAndroidSupport()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Unity " + Application.unityVersion +
                    " needs Android Build Support (SDK, NDK and OpenJDK) installed via Unity Hub before configuring/building Quest.");
        }

        private static BuildReport BuildQuestAndroid()
        {
            ConfigureQuestAndroid();
            var failures = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(failures, BuildTargetGroup.Android);
            foreach (OpenXRFeature.ValidationRule issue in failures)
            {
                if (issue.error) throw new BuildFailedException("OpenXR validation: " + issue.message);
                Debug.LogWarning("OpenXR validation: " + issue.message);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(QuestBuildPath));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = QuestBuildPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });
            Debug.Log($"Virtual Ride Quest APK build: {report.summary.result} ({report.summary.totalSize} bytes)");
            return report;
        }

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
