// Runs the Stage 1 tool (DualConnectW.exe or DualConnect.exe) and reads its one-line JSON answer.
// The tray never loads the tool into its own process, so a crash in one can't take down the other.
// Untested on Windows. C# 5.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DualConnectTray
{
    public sealed class DualConnectRunner
    {
        public readonly string ExePath;
        public readonly string DeviceName;
        public readonly int TimeoutSeconds;
        public int WaitBudgetOverrideMs;   // tests only; 0 = use WaitBudgetMs

        public DualConnectRunner(string exePath, string deviceName, int timeoutSeconds)
        {
            ExePath = exePath;
            DeviceName = deviceName;
            TimeoutSeconds = timeoutSeconds;
        }

        // Where to look when the settings file leaves DualConnectPath empty.
        // DualConnectW.exe is the no-console build; DualConnect.exe works too because
        // the tray starts it with CreateNoWindow.
        public static string Locate(string configuredPath, string trayFolder)
        {
            if (!string.IsNullOrEmpty(configuredPath))
                return Path.GetFullPath(Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(trayFolder, configuredPath));

            string stage1 = Path.Combine(Path.Combine(trayFolder, ".."), "stage1-tools");
            string[] candidates =
            {
                Path.Combine(trayFolder, "DualConnectW.exe"),
                Path.Combine(trayFolder, "DualConnect.exe"),
                Path.Combine(stage1, "DualConnectW.exe"),
                Path.Combine(stage1, "DualConnect.exe")
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return Path.GetFullPath(c);
            return Path.GetFullPath(candidates[0]);
        }

        public string BuildArguments(string verb)
        {
            var args = new List<string> { verb, "--json", "--name", DeviceName };
            if (verb != "status")
            {
                args.Add("--timeout");
                args.Add(TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return ArgQuote.Join(args);
        }

        // How long the tray waits before giving up. "take" may disconnect and then connect,
        // and each half can use the full --timeout, so allow for both plus process start.
        public int WaitBudgetMs(string verb)
        {
            if (verb == "status") return 20000;
            return (TimeoutSeconds * 2 + 15) * 1000;
        }

        // Blocking: call it from a background thread, never the UI thread.
        public DualConnectResult Run(string verb)
        {
            var clock = Stopwatch.StartNew();
            DualConnectResult result;

            if (!File.Exists(ExePath))
            {
                result = new DualConnectResult { Verb = verb, LaunchFailed = true, ErrorText = "Not found: " + ExePath };
                result.WallMs = clock.ElapsedMilliseconds;
                return result;
            }

            var psi = new ProcessStartInfo(ExePath, BuildArguments(verb))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,     // the tool never reads it; closed below so nothing can wait on it
                WorkingDirectory = Path.GetDirectoryName(ExePath),
                // DualConnect's --json line is plain ASCII, so any of these decodes it.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
                try
                {
                    p.Start();
                }
                catch (Exception ex)
                {
                    result = new DualConnectResult { Verb = verb, LaunchFailed = true, ErrorText = "Could not start " + ExePath + ": " + ex.Message };
                    result.WallMs = clock.ElapsedMilliseconds;
                    return result;
                }
                try { p.StandardInput.Close(); } catch (Exception) { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                int budget = WaitBudgetOverrideMs > 0 ? WaitBudgetOverrideMs : WaitBudgetMs(verb);
                if (!p.WaitForExit(budget))
                {
                    try { p.Kill(); } catch (Exception) { }
                    try { p.WaitForExit(2000); } catch (Exception) { }
                    string so, se;
                    lock (stdout) so = stdout.ToString();
                    lock (stderr) se = stderr.ToString();
                    result = DualConnectResult.FromOutput(verb, -1, so, se);
                    result.TimedOut = true;
                    result.ErrorText = "DualConnect did not finish within " + (budget / 1000) + " s and was stopped.";
                    result.WallMs = clock.ElapsedMilliseconds;
                    return result;
                }
                p.WaitForExit();   // lets the asynchronous readers deliver the last lines
                string outText, errText;
                lock (stdout) outText = stdout.ToString();
                lock (stderr) errText = stderr.ToString();
                result = DualConnectResult.FromOutput(verb, p.ExitCode, outText, errText);
            }
            result.WallMs = clock.ElapsedMilliseconds;
            return result;
        }
    }
}
