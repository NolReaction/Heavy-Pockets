#if UNITY_EDITOR_WIN
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HeavyPockets.Editor
{
    [InitializeOnLoad]
    internal static class TwoPlayerRaid
    {
        private const string LaunchMenu = "Heavy Pockets/Тест на двух игроков/Запустить два окна";
        private const string StopMenu = "Heavy Pockets/Тест на двух игроков/Остановить";
        private const string Pending = "HeavyPockets.LaunchTwoPlayers";
        private static readonly ConcurrentQueue<string> output = new ConcurrentQueue<string>();
        private static Process launcher;
        private static bool stopping;
        private static int progress = -1;

        static TwoPlayerRaid()
        {
            EditorApplication.update += Update;
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        [MenuItem(LaunchMenu, false, 1)]
        private static void RequestLaunch()
        {
            SessionState.SetBool(Pending, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
        }

        [MenuItem(LaunchMenu, true)]
        private static bool CanLaunch() => launcher == null && !SessionState.GetBool(Pending, false) &&
            !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer;

        [MenuItem(StopMenu, false, 2)]
        private static void Stop()
        {
            if (launcher == null || stopping) return;
            stopping = true;
            try { launcher.StandardInput.Close(); } catch (IOException) { }
        }

        [MenuItem(StopMenu, true)]
        private static bool CanStop() => launcher != null && !stopping;

        private static void Launch()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string script = Path.Combine(project, "Tools", "Start-Raid.ps1");
            launcher = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    WorkingDirectory = project,
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -UnityEditor \"{EditorApplication.applicationPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            launcher.OutputDataReceived += QueueLine;
            launcher.ErrorDataReceived += QueueLine;
            launcher.Start();
            launcher.BeginOutputReadLine();
            launcher.BeginErrorReadLine();
            progress = Progress.Start("Рейд на двух игроков", "Проверка локальных сборок…");
            Debug.Log("Запуск рейда на двух игроков. При первом запуске Unity подготовит клиент и сервер.");
        }

        private static void QueueLine(object sender, DataReceivedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) output.Enqueue(args.Data);
        }

        private static void Update()
        {
            if (launcher == null)
            {
                if (!SessionState.GetBool(Pending, false) || EditorApplication.isPlayingOrWillChangePlaymode ||
                    EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                SessionState.EraseBool(Pending);
                try { Launch(); }
                catch (Exception exception)
                {
                    launcher?.Dispose();
                    launcher = null;
                    Debug.LogException(exception);
                }
                return;
            }

            bool exited = launcher.HasExited;
            if (exited) launcher.WaitForExit(); // Flush both async log streams.
            while (output.TryDequeue(out string line))
            {
                Debug.Log("[Рейд на двух игроков] " + line);
                if (line.StartsWith("Both players connected", StringComparison.Ordinal))
                    FinishProgress(Progress.Status.Succeeded);
                else if (progress >= 0)
                    Progress.Report(progress, -1f, line);
            }
            if (!exited) return;
            bool failed = launcher.ExitCode != 0 && !stopping;
            FinishProgress(failed ? Progress.Status.Failed : Progress.Status.Succeeded);
            if (failed) Debug.LogError("Не удалось запустить или продолжить рейд. Подробности и путь к журналу приведены выше.");
            launcher.Dispose();
            launcher = null;
            stopping = false;
        }

        private static void FinishProgress(Progress.Status status)
        {
            if (progress < 0) return;
            Progress.Finish(progress, status);
            progress = -1;
        }
    }
}
#endif
