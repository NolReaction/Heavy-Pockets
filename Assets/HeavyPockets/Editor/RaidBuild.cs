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
            Debug.Log($"Started local {target} build. See Build/{target}-build.log.");
        }

        // Invoked only inside the protected build script's isolated Unity project.
        public static void ProtectedClient() => Build(false, true);
        public static void ProtectedServer() => Build(true, true);

        private static void Build(bool server, bool protectedBuild)
        {
            string output = "Build";
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
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
                locationPathName = Path.Combine(output, server ? "Server/HeavyPocketsServer.exe" : "Client/HeavyPockets.exe"),
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
