#if UNITY_EDITOR_WIN && !HEAVY_POCKETS_EOS && !UNITY_SERVER && !HEAVY_POCKETS_SERVER
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class EditorRaidSession : IDisposable
    {
        [Serializable]
        private sealed class ReadyMessage
        {
            public int port = 0;
            public string ca = null;
            public string ticket = null;
        }

        private readonly Process helper;
        private readonly Task<string> errors;
        private Task<string> readyLine;
        private bool disposed;

        internal EditorRaidSession()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = Path.Combine(project, "Tools", "Start-EditorRaid.ps1");
            if (!File.Exists(script))
                throw new FileNotFoundException("The editor raid launcher is missing.", script);

            helper = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"",
                WorkingDirectory = project,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Could not start the editor raid launcher.");

            try
            {
                errors = helper.StandardError.ReadToEndAsync();
                readyLine = helper.StandardOutput.ReadLineAsync();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal IEnumerator WaitUntilReady(Action<ushort, string, string> connected, Action<Exception> failed)
        {
            var timer = Stopwatch.StartNew();
            while (!disposed && !readyLine.IsCompleted && timer.Elapsed.TotalSeconds < 50)
                yield return null;
            if (disposed)
                yield break;
            if (!readyLine.IsCompleted)
            {
                failed(new TimeoutException("Timed out waiting for the local raid server to start."));
                yield break;
            }

            string line = null;
            try { line = readyLine.GetAwaiter().GetResult(); }
            catch (IOException) { }
            readyLine = null;
            if (string.IsNullOrWhiteSpace(line) || helper.HasExited)
            {
                while (!disposed && !errors.IsCompleted && timer.Elapsed.TotalSeconds < 50)
                    yield return null;
                if (!disposed)
                    failed(new InvalidOperationException(StartupError()));
                yield break;
            }

            ReadyMessage ready = ParseReady(line);
            if (ready == null)
            {
                failed(new InvalidDataException("The local raid launcher returned invalid connection information."));
                yield break;
            }
            try { connected((ushort)ready.port, ready.ca, ready.ticket); }
            catch (Exception exception) { failed(exception); }
        }

        private static ReadyMessage ParseReady(string line)
        {
            try
            {
                if (line.Length > 16384)
                    return null;
                var ready = JsonUtility.FromJson<ReadyMessage>(line);
                if (ready == null || ready.port < 1 || ready.port > ushort.MaxValue ||
                    string.IsNullOrEmpty(ready.ca) || !ready.ca.StartsWith("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal) ||
                    !ready.ca.TrimEnd().EndsWith("-----END CERTIFICATE-----", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(ready.ticket) || Convert.FromBase64String(ready.ticket).Length != 32)
                    return null;
                return ready;
            }
            catch (ArgumentException) { return null; }
            catch (FormatException) { return null; }
        }

        private string StartupError()
        {
            // PowerShell can include command text in an error. Only return known, non-sensitive diagnoses.
            string message = errors.Status == TaskStatus.RanToCompletion ? errors.Result.Trim() : "";
            if (message.StartsWith("Missing Build/Server/HeavyPocketsServer.exe", StringComparison.Ordinal))
                return "Missing Build/Server/HeavyPocketsServer.exe. Build the Windows dedicated server from the Heavy Pockets menu.";
            if (message == "Could not prepare the encrypted local server session.")
                return message;
            var logName = Regex.Match(message, @"HeavyPockets-Editor-[a-fA-F0-9]{32}\.log$");
            if (logName.Success)
                return "The local raid server could not start. See " + Path.Combine(Path.GetTempPath(), logName.Value);
            return helper.HasExited
                ? $"The local raid launcher exited with code {helper.ExitCode} before the server was ready."
                : "The local raid launcher closed its output before the server was ready.";
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            // Closing the parent pipe tells this launcher to stop its own child server.
            try { helper.StandardInput.Close(); }
            catch (IOException) { }
            finally { helper.Dispose(); }
        }
    }
}
#endif
