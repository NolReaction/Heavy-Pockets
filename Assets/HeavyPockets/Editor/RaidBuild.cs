using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HeavyPockets.Editor
{
    public static class RaidBuild
    {
        private const string Scene = "Assets/HeavyPockets/Depot.unity";

        [MenuItem("Heavy Pockets/Build Windows client")]
        private static void BuildLocalClient() => BuildFromMenu(false);
        public static void Client() => Build(false, false);

        [MenuItem("Heavy Pockets/Build Windows dedicated server")]
        private static void BuildLocalServer() => BuildFromMenu(true);
        public static void Server() => Build(true, false);

#if UNITY_EDITOR_OSX
        [MenuItem("Heavy Pockets/Build macOS client")]
        private static void BuildMacClient() => BuildMac(false);

        [MenuItem("Heavy Pockets/Build macOS dedicated server")]
        private static void BuildMacServer() => BuildMac(true);

        private static void BuildMac(bool server)
        {
            if (UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.playeveryware.eos/package.json") != null)
            {
                Debug.LogError("Remove com.playeveryware.eos from Packages/manifest.json in the local macOS checkout before building.");
                return;
            }

            var previousArchitecture = UnityEditor.OSXStandalone.UserBuildSettings.architecture;
            try
            {
                UnityEditor.OSXStandalone.UserBuildSettings.architecture =
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                        ? UnityEditor.Build.OSArchitecture.ARM64 : UnityEditor.Build.OSArchitecture.x64;
                Build(server, false, BuildTarget.StandaloneOSX);
            }
            finally
            {
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = previousArchitecture;
            }
        }
#endif

        private static void BuildFromMenu(bool server)
        {
            if (UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.playeveryware.eos/package.json") == null)
            {
                Build(server, false);
                return;
            }
            // Local play uses a temporary project so the installed EOS Editor validator
            // remains enabled in the authoring project without requiring a deployment.
            string root = Path.GetDirectoryName(Application.dataPath);
            string script = Path.Combine(root, "Tools", "Build-Raid.ps1");
            string target = server ? "Server" : "Client";
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -UnityEditor \"{EditorApplication.applicationPath}\" -Target {target}",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(start)?.Dispose();
            Debug.Log($"Started local {target} build. See Build/Development/{target}-build.log.");
        }

        // Invoked only inside the protected build script's isolated Unity project.
        public static void ProtectedClient() => Build(false, true);
        public static void ProtectedServer() => Build(true, true);

        private static void Build(bool server, bool protectedBuild, BuildTarget target = BuildTarget.StandaloneWindows64)
        {
            bool mac = target == BuildTarget.StandaloneOSX;
            string output = mac ? "Build/Development-Mac" : "Build/Development";
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-hpBuildOutput");
            if (index >= 0)
            {
                if (index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
                    throw new ArgumentException("-hpBuildOutput requires an absolute output directory.");
                output = args[index + 1];
            }
            else if (protectedBuild)
                throw new InvalidOperationException("Use Tools/Build-Raid.ps1 -Protected to build an EAC player.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            PlayerSettings.companyName = "Heavy Pockets";
            PlayerSettings.productName = "Heavy Pockets";
            PlayerSettings.defaultScreenWidth = 960;
            PlayerSettings.defaultScreenHeight = 600;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            string player = server ? "Server/HeavyPocketsServer" : "Client/HeavyPockets";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                target = target,
                subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
                locationPathName = Path.Combine(output, player + (mac ? ".app" : ".exe")),
                extraScriptingDefines = protectedBuild ? new[] { "HEAVY_POCKETS_EOS" } : Array.Empty<string>(),
                options = protectedBuild ? BuildOptions.None : BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Build failed: " + report.summary.result);
            Debug.Log("HEAVY POCKETS BUILD OK " + options.locationPathName);
        }
    }
}
