// Checks the tray against the real Stage 1 tool rather than the stand-in:
//  - the message phrases and default timeout the tray relies on are still in DualConnect.cs
//  - DualConnect's own JSON writer (Core.ToJson) produces lines the tray reads correctly
//  - the real DualConnect.exe, run under Mono, accepts the arguments the tray builds and
//    answers in the shape the tray expects. Its Windows audio calls can't work outside Windows,
//    so here every status, take and give ends with exit code 3 and an empty endpoint list,
//    the "not read" case of INTERFACE.md. Nothing here shows how it behaves on Windows.
// Run by tests/run-tests.sh when ../stage1-tools/DualConnect.cs exists.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DualConnectTray;

internal static class ContractTests
{
    private static int passed, failed;
    private static string source = "", realTool = "";

    private static int Main(string[] args)
    {
        source = File.ReadAllText(args[0]);
        realTool = args.Length > 1 ? args[1] : "";
        Run("DualConnect.cs still has every message phrase the tray keys on", Phrases);
        Run("the tray's default timeout is DualConnect's default", DefaultTimeout);
        Run("DualConnect's JSON writer round-trips through the tray's reader", JsonRoundTrip);
        if (realTool.Length > 0)
        {
            Run("real tool accepts the tray's arguments and answers in its shape", RealToolArguments);
            Run("real tool's unread endpoint list leaves the tray's state alone", RealToolEmptyList);
        }
        Console.WriteLine();
        Console.WriteLine(passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL  " + name + ": " + ex.Message);
        }
    }

    private static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(what + ": expected <" + expected + "> but got <" + actual + ">");
    }

    private static void True(bool cond, string what)
    {
        if (!cond) throw new Exception(what);
    }

    private static void Phrases()
    {
        foreach (string phrase in StateLogic.ContractPhrases)
            True(source.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0, "missing from DualConnect.cs: \"" + phrase + "\"");
    }

    private static void DefaultTimeout()
    {
        Match m = Regex.Match(source, @"int timeout = (\d+);");
        True(m.Success, "default timeout not found in DualConnect.cs");
        Eq(int.Parse(m.Groups[1].Value), new TraySettings().TimeoutSeconds, "default --timeout");
    }

    private static void JsonRoundTrip()
    {
        var r = new DualConnect.Result();
        r.Command = "take";
        r.ExitCode = 4;
        r.Ok = false;
        r.ElapsedMs = 23456;
        r.Message = "an earlier DualConnect command moving the sound the same way is still running; nothing was sent \"q\" ö";
        var render = new DualConnect.EndpointInfo();
        render.Name = "Kopfhörer (AirPods Pro)";
        render.DeviceName = "AirPods Pro";
        render.Flow = "render";
        render.State = "active";
        render.IsDefault = true;
        render.Peak = 0.25f;
        render.Id = "{0.0.0.00000000}.{aaaa}";
        render.FilterId = @"{2}.\\?\bthenum#x";
        render.ContainerId = "6f1d2a3b-0000-1111-2222-333344445555";
        var mic = new DualConnect.EndpointInfo();
        mic.Name = "Headset (AirPods Pro)";
        mic.DeviceName = "AirPods Pro";
        mic.Flow = "capture";
        mic.State = "state16";
        mic.Peak = -1f;
        mic.Id = "{0.0.1.00000000}.{bbbb}";
        r.Endpoints.Add(render);
        r.Endpoints.Add(mic);

        string line = DualConnect.Core.ToJson(r);
        foreach (char c in line) True(c >= 0x20 && c <= 0x7E, "plain ASCII line");
        DualConnectResult t = DualConnectResult.FromOutput("take", 4, line + "\r\n", "");
        True(t.JsonParsed, "parsed: " + t.ErrorText);
        Eq(r.Message, t.Message, "message");
        Eq(23456L, t.ElapsedMs, "elapsed");
        Eq(2, t.Endpoints.Count, "endpoints");
        Eq(render.Name, t.Endpoints[0].Name, "non-ASCII name");
        Eq("AirPods Pro", t.Endpoints[0].DeviceName, "device name");
        True(t.Endpoints[0].IsRender && t.Endpoints[0].IsActive && t.Endpoints[0].IsDefault, "render active default");
        Eq(0.25, t.Endpoints[0].Peak, "peak");
        Eq(render.Id, t.Endpoints[0].Id, "id");
        Eq(render.ContainerId, t.Endpoints[0].ContainerId, "containerId");
        Eq("", t.Endpoints[1].ContainerId, "null containerId");
        True(!t.Endpoints[1].IsActive && !t.Endpoints[1].IsPresent, "state16 is not active");
        Eq(ResultKind.StillRunning, StateLogic.Classify(t), "kind");
        LaptopState s;
        True(StateLogic.TryStateAfterAction(t, out s) && s == LaptopState.OnLaptop, "state from the listed endpoints");

        var empty = new DualConnect.Result();
        empty.Command = "take";
        empty.ExitCode = 3;
        empty.Message = "a Windows audio call did not return; gave up";
        DualConnectResult e = DualConnectResult.FromOutput("take", 3, DualConnect.Core.ToJson(empty), "");
        True(e.JsonParsed && e.Endpoints.Count == 0, "empty list parsed");
        True(!StateLogic.TryStateAfterAction(e, out s), "watchdog result leaves the state alone");
    }

    private static void RealToolArguments()
    {
        var plain = new DualConnectRunner(realTool, "AirPods", 20);
        var mine = new DualConnectRunner(realTool, "Kopfhörer AirPods", "6f1d2a3b-0000-1111-2222-333344445555", 10);
        var longest = new DualConnectRunner(realTool, "AirPods", 120);
        foreach (KeyValuePair<DualConnectRunner, string> c in new[]
        {
            new KeyValuePair<DualConnectRunner, string>(plain, "status"),
            new KeyValuePair<DualConnectRunner, string>(plain, "take"),
            new KeyValuePair<DualConnectRunner, string>(mine, "status"),
            new KeyValuePair<DualConnectRunner, string>(mine, "give"),
            new KeyValuePair<DualConnectRunner, string>(longest, "take")
        })
        {
            DualConnectResult r = c.Key.Run(c.Value);
            string what = c.Value + " " + c.Key.BuildArguments(c.Value);
            True(!r.LaunchFailed && !r.TimedOut, what + ": ran: " + r.ErrorText);
            True(r.JsonParsed, what + ": one JSON line: " + r.RawOutput);
            True(r.ExitCode != 1, what + ": arguments accepted: " + r.Message);
            Eq(3, r.ExitCode, what + ": Windows calls fail outside Windows");
        }
        DualConnectResult bad = plain.Run("bogus");
        Eq(1, bad.ExitCode, "unknown command");
        Eq(ResultKind.BadArguments, StateLogic.Classify(bad), "usage error kind");
    }

    private static void RealToolEmptyList()
    {
        var runner = new DualConnectRunner(realTool, "AirPods", 20);
        DualConnectResult status = runner.Run("status");
        Eq(0, status.Endpoints.Count, "nothing read");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(status), "a failed status check, not 'not paired'");
        DualConnectResult take = runner.Run("take");
        LaptopState s;
        True(!StateLogic.TryStateAfterAction(take, out s), "a take that read nothing keeps the old state");
        True(StateLogic.FailureText(StateLogic.Classify(take), take, "AirPods").Length > 0, "and shows a notice");
    }
}
