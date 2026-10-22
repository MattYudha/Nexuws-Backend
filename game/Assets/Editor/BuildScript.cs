using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PongEditor
{
    /// <summary>Android build entry point used by CI (-executeMethod PongEditor.BuildScript.BuildAndroid).</summary>
    public static class BuildScript
    {
        private const string ApplicationId = "id.cds.photonpong";

        public static void BuildAndroid()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "No enabled scene in Build Settings. In the Unity editor run 'Pong > Create Main Scene', " +
                    "assign the Quantum Map, then commit Assets/Scenes and the .meta files.");

            var target = NamedBuildTarget.Android;
            PlayerSettings.productName = "Photon Pong";
            PlayerSettings.SetApplicationIdentifier(target, ApplicationId);
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowUnsafeCode = true;

            // Plain-HTTP backends (local dev) are blocked on Android unless explicitly allowed.
            if (Environment.GetCommandLineArgs().Contains("-pongAllowHttp"))
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            if (int.TryParse(ReadArg("-androidVersionCode"), out var run))
                PlayerSettings.Android.bundleVersionCode = run;

            EditorUserBuildSettings.buildAppBundle = false; // APK; flip to true for Play Store AABs
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Release;

            var outputDir = ReadArg("-customBuildPath") ?? "build/Android";
            Directory.CreateDirectory(outputDir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.Android,
                locationPathName = Path.Combine(outputDir, "PhotonPong.apk"),
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Android build failed: " + report.summary.result);
            Debug.Log($"Android build succeeded: {options.locationPathName} ({report.summary.totalSize} bytes)");
        }

        private static string ReadArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
