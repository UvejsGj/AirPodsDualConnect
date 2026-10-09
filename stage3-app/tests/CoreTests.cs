// Tests for the parts of DualConnect Tray that don't need Windows.
// Run with tests/run-tests.sh (Mono on Linux). The Windows-only parts (tray icon, hotkeys,
// Core Audio notifications, Startup shortcut) are compile-checked only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DualConnectTray;

internal static class CoreTests
{
    private static int passed, failed;
    private static string fakeTool;

    private static int Main(string[] args)
    {
        fakeTool = args.Length > 0 ? args[0] : "";
        Run("ini get/set keeps comments", IniKeepsComments);
        Run("default settings file parses cleanly", DefaultSettingsParse);
        Run("bad settings fall back with warnings", BadSettingsWarn);
        Run("hotkey parsing", HotkeyParsing);
        Run("json parsing", JsonParsing);
        Run("json rejects malformed input", JsonRejects);
        Run("result from DualConnect output", ResultFromOutput);
        Run("state from endpoints", StateFromEndpoints);
        Run("state from status exit codes", StateFromExitCodes);
        Run("result kinds from DualConnect's messages", ResultKinds);
        Run("state after a switch ignores unread endpoint lists", StateAfterAction);
        Run("failure notices", FailureNotices);
        Run("toggle verb", ToggleVerb);
        Run("tooltip never exceeds 63 characters", TooltipLimit);
        Run("outside change detector", OutsideChanges);
        Run("late finish of a failed switch", LateFinish);
        Run("argument quoting round-trips through Windows rules", QuotingRoundTrip);
        Run("csv row escaping round-trips", CsvRoundTrip);
        Run("log summary", Summary);
        Run("locate DualConnect", Locate);
        if (fakeTool.Length > 0)
        {
            Run("runner: take, status, give against fake tool", RunnerHappyPath);
            Run("runner: failure exit codes", RunnerFailures);
            Run("runner: garbage output", RunnerGarbage);
            Run("runner: chatty output and stderr", RunnerChatty);
            Run("runner: final-contract cases (two pairs, busy, stopped, lock, watchdog)", RunnerContractCases);
            Run("runner: missing tool", RunnerMissing);
            Run("runner: timeout kills the process", RunnerTimeout);
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

    // ---------------------------------------------------------------- settings

    private static void IniKeepsComments()
    {
        IniFile ini = IniFile.Parse("; comment\r\nDeviceName = AirPods\r\n\r\n# other\r\nToggleHotkey=Win+Alt+A\r\n");
        Eq("AirPods", ini.Get("devicename"), "case-insensitive get");
        Eq<string>(null, ini.Get("Missing"), "missing key");
        ini.Set("DeviceName", "AirPods Pro");
        ini.Set("DualConnectPath", @"C:\tools\DualConnectW.exe");
        string text = ini.ToText();
        True(text.StartsWith("; comment\r\nDeviceName=AirPods Pro\r\n\r\n# other\r\n"), "comment and order kept: " + text);
        True(text.EndsWith("DualConnectPath=C:\\tools\\DualConnectW.exe\r\n"), "new key appended");
        Eq("AirPods Pro", IniFile.Parse(text).Get("DeviceName"), "re-read");
    }

    private static void DefaultSettingsParse()
    {
        TraySettings s = TraySettings.FromIni(IniFile.Parse(TraySettings.DefaultFileText()));
        Eq(0, s.Warnings.Count, "warnings: " + string.Join(" | ", s.Warnings.ToArray()));
        Eq("AirPods", s.DeviceName, "device");
        Eq("Win+Alt+A", s.ToggleHotkey, "toggle");
        Eq("", s.TakeHotkey, "take");
        Eq("", s.DualConnectPath, "path");
        Eq(false, s.LeftClickToggles, "left click");
        Eq(true, s.NotifyOnOutsideChanges, "notify");
        Eq(20, s.TimeoutSeconds, "timeout matches DualConnect's default");
        Eq(60, s.SafetyPollSeconds, "poll");
        Eq("", s.ContainerId, "container");
    }

    private static void BadSettingsWarn()
    {
        TraySettings s = TraySettings.FromIni(IniFile.Parse(
            "DeviceName=\nLeftClick=sometimes\nNotifyOnOutsideChanges=maybe\nTimeoutSeconds=999\nSafetyPollSeconds=5\nDualConnectPath=\"C:\\a b\\DualConnectW.exe\"\nContainerId=my airpods\n"));
        Eq("AirPods", s.DeviceName, "device fallback");
        Eq(false, s.LeftClickToggles, "left click fallback");
        Eq(true, s.NotifyOnOutsideChanges, "notify fallback");
        Eq(20, s.TimeoutSeconds, "timeout fallback");
        Eq("", s.ContainerId, "bad container ignored");
        Eq(10, s.SafetyPollSeconds, "poll raised to 10");
        Eq(@"C:\a b\DualConnectW.exe", s.DualConnectPath, "quotes removed");
        Eq(6, s.Warnings.Count, "warnings: " + string.Join(" | ", s.Warnings.ToArray()));
        Eq(1, TraySettings.FromIni(IniFile.Parse("TimeoutSeconds=5")).Warnings.Count, "timeout below 10 rejected");
        Eq(10, TraySettings.FromIni(IniFile.Parse("TimeoutSeconds=10")).TimeoutSeconds, "10 allowed");
        TraySettings c = TraySettings.FromIni(IniFile.Parse("ContainerId=\"{6F1D2A3B-0000-1111-2222-333344445555}\""));
        Eq("6f1d2a3b-0000-1111-2222-333344445555", c.ContainerId, "container normalised as DualConnect prints it");
        Eq(0, c.Warnings.Count, "container accepted");
        Eq(0, TraySettings.FromIni(IniFile.Parse("ContainerId=")).Warnings.Count, "empty container allowed");
        Eq(0, TraySettings.FromIni(IniFile.Parse("SafetyPollSeconds=0\nLeftClick=toggle")).Warnings.Count, "0 = off is allowed");
        True(TraySettings.FromIni(IniFile.Parse("LeftClick=Toggle")).LeftClickToggles, "toggle accepted");
    }

    private static void HotkeyParsing()
    {
        Hotkey hk;
        string err;
        True(Hotkey.TryParse("Win+Alt+A", out hk, out err), "Win+Alt+A: " + err);
        Eq(Hotkey.ModWin | Hotkey.ModAlt, hk.Modifiers, "mods");
        Eq((uint)0x41, hk.VirtualKey, "vk A");
        Eq("Win+Alt+A", hk.Display, "display");

        True(Hotkey.TryParse(" control + shift + f12 ", out hk, out err), "f12: " + err);
        Eq(Hotkey.ModControl | Hotkey.ModShift, hk.Modifiers, "mods ctrl shift");
        Eq((uint)0x7B, hk.VirtualKey, "vk F12");
        Eq("Ctrl+Shift+F12", hk.Display, "display order");

        True(Hotkey.TryParse("Alt+Win+9", out hk, out err) && hk.VirtualKey == 0x39 && hk.Display == "Win+Alt+9", "digit");
        True(Hotkey.TryParse("Ctrl+Alt+Pause", out hk, out err) && hk.VirtualKey == 0x13, "pause");
        True(Hotkey.TryParse("Ctrl+Alt+F1", out hk, out err) && hk.VirtualKey == 0x70, "F1");
        True(Hotkey.TryParse("Ctrl+Alt+F24", out hk, out err) && hk.VirtualKey == 0x87, "F24");
        True(Hotkey.TryParse("Ctrl+Alt+PageDown", out hk, out err) && hk.VirtualKey == 0x22, "PageDown");

        True(Hotkey.TryParse("", out hk, out err) && hk == null, "empty = none");
        True(Hotkey.TryParse("   ", out hk, out err) && hk == null, "blank = none");

        string[] bad = { "A", "Shift+A", "Ctrl+Alt", "Ctrl++A", "Ctrl+Ctrl+A", "Ctrl+Foo", "Alt+F25", "Alt+F0", "Ctrl+A+B", "Hyper+A", "Alt+Ä" };
        foreach (string b in bad)
        {
            True(!Hotkey.TryParse(b, out hk, out err), "should reject " + b);
            True(!string.IsNullOrEmpty(err), "error text for " + b);
        }
    }

    // ---------------------------------------------------------------- json and results

    private static void JsonParsing()
    {
        var o = (Dictionary<string, object>)MiniJson.Parse(
            " {\"a\":1.5,\"b\":[true,false,null,-2e3],\"c\":{\"d\":\"Kopfh\\u00f6rer \\\"x\\\" \\\\ \\/ \\n\"},\"e\":[],\"f\":{}} ");
        Eq(1.5, (double)o["a"], "number");
        var b = (List<object>)o["b"];
        Eq(true, (bool)b[0], "true");
        Eq(false, (bool)b[1], "false");
        True(b[2] == null, "null");
        Eq(-2000.0, (double)b[3], "exponent");
        Eq("Kopfh\u00f6rer \"x\" \\ / \n", (string)((Dictionary<string, object>)o["c"])["d"], "escapes");
        Eq(0, ((List<object>)o["e"]).Count, "empty array");
        Eq(0, ((Dictionary<string, object>)o["f"]).Count, "empty object");
        Eq("\U0001F3A7", (string)MiniJson.Parse("\"\\ud83c\\udfa7\""), "surrogate pair");
    }

    private static void JsonRejects()
    {
        string[] bad = { "", "{", "{\"a\":}", "[1,]", "{\"a\":1}x", "\"abc", "{'a':1}", "tru", "{\"a\":1,}", "\"\\x\"", "\"a\nb\"", "--1" };
        foreach (string s in bad)
        {
            bool threw = false;
            try { MiniJson.Parse(s); }
            catch (FormatException) { threw = true; }
            True(threw, "should reject <" + s + ">");
        }
        var deep = new StringBuilder();
        for (int i = 0; i < 100; i++) deep.Append('[');
        bool deepThrew = false;
        try { MiniJson.Parse(deep.ToString()); } catch (FormatException) { deepThrew = true; }
        True(deepThrew, "deep nesting rejected");
    }

    private const string SampleJson =
        "{\"command\":\"take\",\"ok\":true,\"exitCode\":0,\"elapsedMs\":2380,\"message\":\"done\",\"endpoints\":[" +
        "{\"name\":\"Kopfh\\u00f6rer (AirPods Pro)\",\"deviceName\":\"AirPods Pro\",\"flow\":\"render\",\"state\":\"active\",\"isDefault\":true,\"peak\":0.12,\"id\":\"{0.0.0.00000000}.{aaaa}\",\"filterId\":\"{2}.\\\\?\\\\bthenum#x\",\"containerId\":\"6f1d2a3b-0000-1111-2222-333344445555\"}," +
        "{\"name\":\"Headset (AirPods Pro)\",\"deviceName\":\"AirPods Pro\",\"flow\":\"capture\",\"state\":\"unplugged\",\"isDefault\":false,\"peak\":-1,\"id\":\"{0.0.1.00000000}.{bbbb}\",\"filterId\":null,\"containerId\":null}]}";

    private static void ResultFromOutput()
    {
        DualConnectResult r = DualConnectResult.FromOutput("take", 0, "Some text\r\n" + SampleJson + "\r\n\r\n", "");
        True(r.JsonParsed, "parsed");
        True(r.Ok, "ok");
        True(r.Succeeded, "succeeded");
        Eq(2380L, r.ElapsedMs, "elapsed");
        Eq("done", r.Message, "message");
        Eq(2, r.Endpoints.Count, "endpoints");
        Eq("Kopfh\u00f6rer (AirPods Pro)", r.Endpoints[0].Name, "unicode name");
        Eq("AirPods Pro", r.Endpoints[0].DeviceName, "device name");
        True(r.Endpoints[0].IsRender && r.Endpoints[0].IsActive && r.Endpoints[0].IsDefault, "render active default");
        Eq(0.12, r.Endpoints[0].Peak, "peak");
        True(!r.Endpoints[1].IsActive && !r.Endpoints[1].IsRender, "capture unplugged");
        Eq("6f1d2a3b-0000-1111-2222-333344445555", r.Endpoints[0].ContainerId, "containerId");
        Eq("", r.Endpoints[1].ContainerId, "null containerId");
        True(r.Endpoints[0].IsPresent && r.Endpoints[1].IsPresent, "active and unplugged are present");

        DualConnectResult none = DualConnectResult.FromOutput("status", 0, "", "boom");
        True(!none.JsonParsed && none.ErrorText.Length > 0, "no json");
        True(none.RawOutput.Contains("boom"), "stderr kept");

        DualConnectResult arr = DualConnectResult.FromOutput("status", 0, "[1,2]", "");
        True(!arr.JsonParsed, "array is not a result");

        DualConnectResult odd = DualConnectResult.FromOutput("status", 0, "{\"ok\":\"yes\",\"endpoints\":[1,{\"state\":5}]}", "");
        True(odd.JsonParsed && !odd.Ok, "wrong types fall back");
        Eq(1, odd.Endpoints.Count, "non-object endpoints skipped");
        Eq("", odd.Endpoints[0].State, "non-string state is empty");
    }

    private static EndpointInfo Ep(string flow, string state, bool isDefault)
    {
        return new EndpointInfo { Flow = flow, State = state, IsDefault = isDefault, DeviceName = "AirPods Pro" };
    }

    private static void StateFromEndpoints()
    {
        Eq(LaptopState.Unknown, StateLogic.FromEndpoints(new List<EndpointInfo>()), "empty list is unread, not 'not paired'");
        Eq(LaptopState.OnLaptop, StateLogic.FromEndpoints(new List<EndpointInfo> { Ep("render", "active", true), Ep("capture", "active", false) }), "on");
        Eq(LaptopState.OnLaptopNotDefault, StateLogic.FromEndpoints(new List<EndpointInfo> { Ep("render", "active", false) }), "not default");
        Eq(LaptopState.OnLaptop, StateLogic.FromEndpoints(new List<EndpointInfo> { Ep("render", "active", false), Ep("render", "active", true) }), "two renders, one default");
        Eq(LaptopState.NotOnLaptop, StateLogic.FromEndpoints(new List<EndpointInfo> { Ep("render", "unplugged", true), Ep("capture", "active", true) }), "only capture active");
        Eq(LaptopState.NotOnLaptop, StateLogic.FromEndpoints(new List<EndpointInfo> { Ep("render", "state16", true) }), "unknown state is not active");
        Eq(LaptopState.NotOnLaptop, StateLogic.FromEndpoints(new List<EndpointInfo> { Ep("render", "notpresent", false), Ep("render", "disabled", false) }), "notpresent/disabled");
    }

    private static DualConnectResult Res(int exitCode, string message, params EndpointInfo[] eps)
    {
        var r = new DualConnectResult { ExitCode = exitCode, Message = message, JsonParsed = true };
        r.Endpoints.AddRange(eps);
        return r;
    }

    // Messages as stage1-tools/DualConnect.cs words them (9 Oct 2026).
    private const string MsgSeveral = "2 paired devices match the name: 'AirPods Pro' (connected, containerId 6f1d2a3b-0000-1111-2222-333344445555), 'AirPods Pro' (not connected, containerId 0a0b0c0d-9999-8888-7777-666655554444). Some have the same name, so --name can't tell them apart: add --container with the containerId of yours.";
    private const string MsgStopped = "reconnect sent to 1 filter(s). stopped because a newer DualConnect command started; asked Windows to drop the connect it had started";
    private const string MsgReplaced = "replaced by a newer DualConnect command; nothing was sent";
    private const string MsgSameWay = "an earlier DualConnect command moving the sound the same way is still running; nothing was sent";
    private const string MsgOtherWay = "another DualConnect command is still running and did not stop; nothing was sent";
    private const string MsgLock = "could not open the DualConnect lock: Access to the path is denied.; nothing was sent";
    private const string MsgWatchdog = "a Windows audio call did not return; gave up";
    private const string MsgNotConnected = "not connected at timeout; the AirPods may be in the case, out of range or busy with the iPhone";
    private const string MsgStillConnected = "still connected at timeout; nothing else was sent";

    private static void StateFromExitCodes()
    {
        Eq(LaptopState.Unknown, StateLogic.FromStatus(null), "null");
        Eq(LaptopState.ToolMissing, StateLogic.FromStatus(new DualConnectResult { LaunchFailed = true }), "launch failed");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(new DualConnectResult { TimedOut = true, JsonParsed = true }), "timeout");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(new DualConnectResult { ExitCode = 0 }), "no json");
        Eq(LaptopState.NotPaired, StateLogic.FromStatus(Res(2, "no Bluetooth audio endpoint matches 'AirPods'")), "exit 2");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(Res(3, MsgWatchdog)), "exit 3");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(Res(1, "--container must be a containerId as shown by status")), "exit 1 usage");
        Eq(LaptopState.SeveralDevices, StateLogic.FromStatus(Res(1, MsgSeveral, Ep("render", "active", true))), "exit 1, two pairs");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(Res(0, "connected")), "exit 0 with no endpoints breaks the contract");
        Eq(LaptopState.CheckFailed, StateLogic.FromStatus(Res(4, MsgReplaced, Ep("render", "active", true))), "status never returns 4");
        DualConnectResult ok = DualConnectResult.FromOutput("status", 0, SampleJson, "");
        Eq(LaptopState.OnLaptop, StateLogic.FromStatus(ok), "exit 0 with active default render");
    }

    private static void ResultKinds()
    {
        Eq(ResultKind.Done, StateLogic.Classify(Res(0, "connected")), "0");
        Eq(ResultKind.Done, StateLogic.Classify(Res(0, "already connected by the previous command")), "0 already");
        Eq(ResultKind.SeveralDevices, StateLogic.Classify(Res(1, MsgSeveral + " Nothing was sent.")), "1 several");
        Eq(ResultKind.BadArguments, StateLogic.Classify(Res(1, "--timeout must be 1 to 120 seconds")), "1 usage");
        Eq(ResultKind.NotFound, StateLogic.Classify(Res(2, "no Bluetooth audio endpoint matches 'AirPods'")), "2");
        Eq(ResultKind.LockError, StateLogic.Classify(Res(3, MsgLock)), "3 lock");
        Eq(ResultKind.NoAnswer, StateLogic.Classify(Res(3, MsgWatchdog)), "3 watchdog");
        Eq(ResultKind.Refused, StateLogic.Classify(Res(3, "Windows refused the connect")), "3 refused");
        Eq(ResultKind.Refused, StateLogic.Classify(Res(3, "COMException: boom (hr=0x80070005)")), "3 exception");
        Eq(ResultKind.Superseded, StateLogic.Classify(Res(4, MsgStopped)), "4 stopped");
        Eq(ResultKind.Superseded, StateLogic.Classify(Res(4, "stopped because a newer DualConnect command started; nothing was sent")), "4 stopped before sending");
        Eq(ResultKind.Superseded, StateLogic.Classify(Res(4, MsgReplaced)), "4 replaced");
        Eq(ResultKind.StillRunning, StateLogic.Classify(Res(4, MsgSameWay)), "4 same way still running");
        Eq(ResultKind.StillRunning, StateLogic.Classify(Res(4, MsgOtherWay)), "4 other way still running");
        Eq(ResultKind.NotConfirmed, StateLogic.Classify(Res(4, MsgNotConnected)), "4 not connected");
        Eq(ResultKind.NotConfirmed, StateLogic.Classify(Res(4, MsgStillConnected)), "4 still connected");
        Eq(ResultKind.Unreadable, StateLogic.Classify(Res(7, "x")), "unknown exit code");
        Eq(ResultKind.TrayStopped, StateLogic.Classify(new DualConnectResult { TimedOut = true, JsonParsed = true, ExitCode = 0 }), "tray stopped it");
        Eq(ResultKind.LaunchFailed, StateLogic.Classify(new DualConnectResult { LaunchFailed = true }), "launch");
        Eq(ResultKind.Unreadable, StateLogic.Classify(new DualConnectResult { ExitCode = 0 }), "no json");
    }

    private static void StateAfterAction()
    {
        LaptopState s;
        True(!StateLogic.TryStateAfterAction(Res(3, MsgWatchdog), out s), "watchdog: list not read");
        True(!StateLogic.TryStateAfterAction(Res(3, MsgLock), out s), "lock error: list not read");
        True(!StateLogic.TryStateAfterAction(Res(4, MsgReplaced), out s), "replaced, read failed: empty list");
        True(!StateLogic.TryStateAfterAction(Res(3, "NullReferenceException: x"), out s), "exception: empty list");
        True(!StateLogic.TryStateAfterAction(Res(1, "unknown command: x"), out s), "usage");
        True(!StateLogic.TryStateAfterAction(new DualConnectResult { TimedOut = true, JsonParsed = true }, out s), "tray stopped it");
        True(!StateLogic.TryStateAfterAction(new DualConnectResult { ExitCode = 0 }, out s), "no json");
        True(StateLogic.TryStateAfterAction(Res(2, "no Bluetooth audio endpoint matches 'AirPods'"), out s) && s == LaptopState.NotPaired, "exit 2 empty is not paired");
        True(StateLogic.TryStateAfterAction(Res(1, MsgSeveral, Ep("render", "active", true)), out s) && s == LaptopState.SeveralDevices, "two pairs");
        True(StateLogic.TryStateAfterAction(Res(4, MsgSameWay, Ep("render", "active", true)), out s) && s == LaptopState.OnLaptop, "still running lists endpoints");
        True(StateLogic.TryStateAfterAction(Res(4, MsgNotConnected, Ep("render", "unplugged", false)), out s) && s == LaptopState.NotOnLaptop, "not confirmed");
        True(StateLogic.TryStateAfterAction(Res(3, "Windows refused the connect", Ep("render", "active", false)), out s) && s == LaptopState.OnLaptopNotDefault, "refused, endpoints read");
        True(StateLogic.TryStateAfterAction(new DualConnectResult { LaunchFailed = true }, out s) && s == LaptopState.ToolMissing, "launch failed");
    }

    private static void FailureNotices()
    {
        Eq<string>(null, StateLogic.FailureText(ResultKind.Done, Res(0, "connected"), "AirPods"), "done");
        Eq<string>(null, StateLogic.FailureText(ResultKind.Superseded, Res(4, MsgStopped), "AirPods"), "stopped by a newer press");
        var seen = new Dictionary<string, ResultKind>();
        foreach (ResultKind k in Enum.GetValues(typeof(ResultKind)))
        {
            if (k == ResultKind.Done || k == ResultKind.Superseded) continue;
            var r = Res(4, "");
            r.ErrorText = "x";
            string text = StateLogic.FailureText(k, r, "AirPods");
            True(!string.IsNullOrEmpty(text), "text for " + k);
            True(text.Length <= 255, k + " fits a notification: " + text.Length);
            if (k != ResultKind.BadArguments && k != ResultKind.Refused && k != ResultKind.Unreadable)
            {
                True(!seen.ContainsKey(text), k + " has its own wording");
                seen[text] = k;
            }
        }
        True(StateLogic.MayFinishLate(ResultKind.NotConfirmed, Res(4, MsgNotConnected)), "timeout may finish late");
        True(StateLogic.MayFinishLate(ResultKind.NotConfirmed, Res(4, MsgStillConnected)), "the disconnect half was sent");
        True(StateLogic.MayFinishLate(ResultKind.NoAnswer, Res(3, MsgWatchdog)), "watchdog may finish late");
        True(StateLogic.MayFinishLate(ResultKind.TrayStopped, new DualConnectResult { TimedOut = true }), "tray-stopped may finish late");
        True(!StateLogic.MayFinishLate(ResultKind.StillRunning, Res(4, MsgSameWay)), "nothing was sent");
        True(!StateLogic.MayFinishLate(ResultKind.LockError, Res(3, MsgLock)), "lock: nothing was sent");
        True(!StateLogic.MayFinishLate(ResultKind.SeveralDevices, Res(1, MsgSeveral)), "two pairs: nothing was sent");
        True(!StateLogic.MayFinishLate(ResultKind.Done, Res(0, "connected")), "done");
    }

    private static void ToggleVerb()
    {
        Eq("give", StateLogic.ToggleVerb(LaptopState.OnLaptop), "on");
        Eq("give", StateLogic.ToggleVerb(LaptopState.OnLaptopNotDefault), "on not default");
        foreach (LaptopState s in new[] { LaptopState.NotOnLaptop, LaptopState.Unknown, LaptopState.NotPaired, LaptopState.SeveralDevices, LaptopState.ToolMissing, LaptopState.CheckFailed })
            Eq("take", StateLogic.ToggleVerb(s), s.ToString());
        Eq("give", StateLogic.ToggleVerb(LaptopState.NotOnLaptop, "take"), "a running take is reversed");
        Eq("take", StateLogic.ToggleVerb(LaptopState.OnLaptop, "give"), "a running give is reversed");
        Eq("give", StateLogic.ToggleVerb(LaptopState.OnLaptop, null), "nothing running");
        True(StateLogic.MovesToLaptop("take") && StateLogic.MovesToLaptop("connect"), "to laptop");
        True(!StateLogic.MovesToLaptop("give") && !StateLogic.MovesToLaptop("disconnect"), "away");
    }

    private static void TooltipLimit()
    {
        foreach (LaptopState s in Enum.GetValues(typeof(LaptopState)))
            True(StateLogic.Tooltip("DualConnect: " + StateLogic.Describe(s)).Length <= 63, s.ToString());
        foreach (string v in new[] { "take", "give", "connect", "disconnect" })
            True(StateLogic.Tooltip("DualConnect: " + StateLogic.VerbText(v) + "...").Length <= 63, v);
        Eq(63, StateLogic.Tooltip(new string('x', 200)).Length, "long text cut to 63");
        Eq("", StateLogic.Tooltip(null), "null");
    }

    private static void OutsideChanges()
    {
        var d = new OutsideChangeDetector(TimeSpan.FromSeconds(5));
        DateTime t0 = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        Eq("left", d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0), "left while idle");
        Eq("arrived", d.Classify(LaptopState.NotOnLaptop, LaptopState.OnLaptopNotDefault, t0), "arrived while idle");
        Eq<string>(null, d.Classify(LaptopState.OnLaptop, LaptopState.CheckFailed, t0), "failure is not a move");
        Eq<string>(null, d.Classify(LaptopState.NotPaired, LaptopState.OnLaptop, t0), "from not paired is not counted");
        d.BeginAction();
        Eq<string>(null, d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0), "during action");
        d.EndAction(t0);
        Eq<string>(null, d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(4)), "within grace");
        Eq("left", d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(6)), "after grace");
        d.BeginAction();
        d.BeginAction();
        d.EndAction(t0.AddSeconds(10));
        Eq<string>(null, d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(60)), "one action still running");
        d.EndAction(t0.AddSeconds(61));
        d.EndAction(t0.AddSeconds(61));   // extra end must not go negative
        Eq("left", d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(70)), "all ended");
    }

    private static void LateFinish()
    {
        var d = new OutsideChangeDetector(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
        DateTime t0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        d.BeginAction();
        d.EndAction(t0);
        Eq<string>(null, d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0), "the failed take's own result (its disconnect half)");
        d.NoteFailedSwitch("take", t0);
        Eq<string>(null, d.Classify(LaptopState.NotOnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(2)), "no change");
        Eq("late-arrived", d.Classify(LaptopState.NotOnLaptop, LaptopState.OnLaptop, t0.AddSeconds(3)), "arrival inside the grace period is the take finishing");
        Eq("take", d.LateVerb, "which switch");
        Eq<string>(null, d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(4)), "reported once; then the normal grace applies");
        Eq("left", d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t0.AddSeconds(8)), "later moves are outside changes again");

        DateTime t1 = t0.AddMinutes(1);
        d.BeginAction();
        d.EndAction(t1);
        d.NoteFailedSwitch("give", t1);
        Eq("late-left", d.Classify(LaptopState.OnLaptop, LaptopState.NotOnLaptop, t1.AddSeconds(19)), "inside 20 s");
        Eq("give", d.LateVerb, "give");

        DateTime t2 = t0.AddMinutes(2);
        d.BeginAction();
        d.EndAction(t2);
        d.NoteFailedSwitch("take", t2);
        Eq("arrived", d.Classify(LaptopState.NotOnLaptop, LaptopState.OnLaptop, t2.AddSeconds(25)), "after 20 s it's an outside change");

        DateTime t3 = t0.AddMinutes(3);
        d.BeginAction();
        d.EndAction(t3);
        d.NoteFailedSwitch("take", t3);
        d.BeginAction();
        Eq<string>(null, d.Classify(LaptopState.NotOnLaptop, LaptopState.OnLaptop, t3.AddSeconds(1)), "a newer switch is running");
        d.EndAction(t3.AddSeconds(2));
        Eq<string>(null, d.Classify(LaptopState.NotOnLaptop, LaptopState.OnLaptop, t3.AddSeconds(3)), "the newer switch cleared the failed one");
    }

    // Windows' own splitting rules (CommandLineToArgvW / MSVC runtime), used as the reference.
    private static List<string> WindowsSplit(string cmd)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false, have = false;
        int i = 0;
        while (i < cmd.Length)
        {
            char c = cmd[i];
            if (c == '\\')
            {
                int n = 0;
                while (i < cmd.Length && cmd[i] == '\\') { n++; i++; }
                if (i < cmd.Length && cmd[i] == '"')
                {
                    cur.Append('\\', n / 2);
                    if (n % 2 == 1) { cur.Append('"'); i++; }
                }
                else cur.Append('\\', n);
                have = true;
                continue;
            }
            if (c == '"')
            {
                if (inQuotes && i + 1 < cmd.Length && cmd[i + 1] == '"') { cur.Append('"'); i += 2; continue; }
                inQuotes = !inQuotes;
                have = true;
                i++;
                continue;
            }
            if ((c == ' ' || c == '\t') && !inQuotes)
            {
                if (have) { args.Add(cur.ToString()); cur.Length = 0; have = false; }
                i++;
                continue;
            }
            cur.Append(c);
            have = true;
            i++;
        }
        if (have) args.Add(cur.ToString());
        return args;
    }

    private static void QuotingRoundTrip()
    {
        string[] samples =
        {
            "AirPods", "AirPods Pro", "", "a\"b", "trailing\\", "C:\\Program Files\\x\\", "\\\\server\\share",
            "two  spaces", "quote at end\"", "\"", "\\\"", "tab\there", "Kopfh\u00f6rer (AirPods Pro)", "a\\\\\"b", "x y\\\\"
        };
        var list = new List<string> { "status", "--json", "--name" };
        foreach (string s in samples)
        {
            var args = new List<string>(list);
            args.Add(s);
            string joined = ArgQuote.Join(args);
            List<string> back = WindowsSplit(joined);
            Eq(args.Count, back.Count, "count for <" + s + "> via <" + joined + ">");
            for (int k = 0; k < args.Count; k++) Eq(args[k], back[k], "arg " + k + " for <" + s + ">");
        }
        Eq("AirPods", ArgQuote.Quote("AirPods"), "plain stays plain");
        Eq("\"\"", ArgQuote.Quote(""), "empty quoted");
        var runner = new DualConnectRunner("x", "AirPods Pro", 20);
        Eq("take --json --name \"AirPods Pro\" --timeout 20", runner.BuildArguments("take"), "take args");
        Eq("status --json --name \"AirPods Pro\"", runner.BuildArguments("status"), "status has no timeout");
        var mine = new DualConnectRunner("x", "AirPods", "6f1d2a3b-0000-1111-2222-333344445555", 20);
        Eq("status --json --name AirPods --container 6f1d2a3b-0000-1111-2222-333344445555", mine.BuildArguments("status"), "status with container");
        Eq("give --json --name AirPods --container 6f1d2a3b-0000-1111-2222-333344445555 --timeout 20", mine.BuildArguments("give"), "give with container");
        Eq(65000, runner.WaitBudgetMs("take"), "take budget");
        Eq(45000, runner.WaitBudgetMs("give"), "give budget");
        Eq(25000, runner.WaitBudgetMs("status"), "status budget");
        // INTERFACE.md: DualConnect answers by itself within 15 s for status, and within about
        // 5.5 s + --timeout + 8 s for a switch (twice --timeout for take). The tray must wait longer,
        // with at least 10 s left for starting the process.
        True(runner.WaitBudgetMs("status") >= 15000 + 10000, "status outlasts DualConnect's watchdog");
        foreach (int t in new[] { 10, 20, 60, 120 })
        {
            var rt = new DualConnectRunner("x", "AirPods", t);
            True(rt.WaitBudgetMs("take") >= (5500 + 2 * t * 1000 + 8000) + 10000, "take outlasts the watchdog at " + t);
            True(rt.WaitBudgetMs("give") >= (5500 + t * 1000 + 8000) + 10000, "give outlasts the watchdog at " + t);
            True(rt.WaitBudgetMs("give") >= 20000 + 10000, "give outlasts the lock-wait watchdog at " + t);
        }
    }

    private static void CsvRoundTrip()
    {
        DateTime when = new DateTime(2026, 10, 8, 22, 5, 3, 120, DateTimeKind.Local);
        string row = CsvLog.Row(when, "action-end", LaptopState.OnLaptop, "take", 0, 2380, "said \"hi\", then left\nline2");
        List<string> f = CsvLog.Split(row);
        Eq(7, f.Count, "fields in " + row);
        Eq("action-end", f[1], "event");
        Eq("OnLaptop", f[2], "state");
        Eq("take", f[3], "verb");
        Eq("0", f[4], "exit");
        Eq("2380", f[5], "ms");
        Eq("said \"hi\", then left\nline2", f[6], "detail");
        True(f[0].StartsWith("2026-10-08T22:05:03.120"), "time " + f[0]);
        List<string> blanks = CsvLog.Split(CsvLog.Row(when, "start", LaptopState.Unknown, null, null, null, null));
        Eq("", blanks[3], "null verb");
        Eq("", blanks[4], "null exit");
        Eq(7, CsvLog.Split(CsvLog.Header).Count, "header columns");
    }

    private static void Summary()
    {
        DateTime baseTime = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Local);
        var rows = new List<string> { CsvLog.Header };
        rows.Add(CsvLog.Row(baseTime.AddDays(-10), "action-end", LaptopState.OnLaptop, "take", 0, 99999, "too old"));
        long[] takes = { 2000, 3000, 1000, 4000 };
        for (int i = 0; i < takes.Length; i++)
            rows.Add(CsvLog.Row(baseTime.AddMinutes(i), "action-end", LaptopState.OnLaptop, "take", 0, takes[i], ""));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(10), "action-end", LaptopState.NotOnLaptop, "take", 4, 30000, "not confirmed"));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(11), "action-end", LaptopState.NotOnLaptop, "give", 0, 1500, ""));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(12), "outside-left", LaptopState.NotOnLaptop, null, null, null, ""));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(13), "outside-arrived", LaptopState.OnLaptop, null, null, null, ""));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(14), "outside-left", LaptopState.NotOnLaptop, null, null, null, ""));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(15), "busy", LaptopState.OnLaptop, "give", null, null, ""));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(16), "action-stopped", LaptopState.OnLaptop, "take", 4, 900, "stopped because a newer DualConnect command started"));
        rows.Add(CsvLog.Row(baseTime.AddMinutes(17), "late-arrived", LaptopState.OnLaptop, "take", null, null, ""));
        rows.Add("garbage line");

        string text = LogSummary.Summarize(rows, baseTime.AddDays(-7));
        True(text.Contains("Sound to laptop: 4 of 5 succeeded"), text);
        True(text.Contains("median " + (2.5).ToString("0.0", CultureInfo.CurrentCulture) + " s"), "median 2.5: " + text);
        True(text.Contains("slowest " + (4.0).ToString("0.0", CultureInfo.CurrentCulture) + " s"), "slowest 4.0: " + text);
        True(text.Contains("Give back: 1 of 1 succeeded"), text);
        True(text.Contains("without you asking: 2"), "left 2: " + text);
        True(text.Contains("joined the laptop without you asking: 1"), "arrived 1: " + text);
        True(text.Contains("ignored while a switch was running: 1"), "busy: " + text);
        True(text.Contains("stopped by a newer key press (not counted above): 1"), "stopped: " + text);
        True(text.Contains("reported as failed: 1"), "late: " + text);
        True(!text.Contains("99.9"), "old row excluded");
        Eq("No log rows in this period yet.", LogSummary.Summarize(new List<string> { CsvLog.Header }, baseTime), "empty");
        Eq(3L, LogSummary.Percentile(new List<long> { 5, 1, 3 }, 50), "odd median");
        Eq(5L, LogSummary.Percentile(new List<long> { 5, 1, 3 }, 100), "max");
        Eq(5L, LogSummary.Percentile(new List<long> { 5, 1, 3 }, 90), "p90");
    }

    private static void Locate()
    {
        string root = Path.Combine(Path.GetTempPath(), "dctray-locate-" + Guid.NewGuid().ToString("N"));
        string tray = Path.Combine(root, "stage3-app");
        string stage1 = Path.Combine(root, "stage1-tools");
        Directory.CreateDirectory(tray);
        Directory.CreateDirectory(stage1);
        try
        {
            Eq(Path.Combine(tray, "DualConnectW.exe"), DualConnectRunner.Locate("", tray), "nothing found: first candidate");
            File.WriteAllText(Path.Combine(stage1, "DualConnect.exe"), "");
            Eq(Path.Combine(stage1, "DualConnect.exe"), DualConnectRunner.Locate("", tray), "console build in stage1-tools");
            File.WriteAllText(Path.Combine(stage1, "DualConnectW.exe"), "");
            Eq(Path.Combine(stage1, "DualConnectW.exe"), DualConnectRunner.Locate("", tray), "windowless build preferred");
            File.WriteAllText(Path.Combine(tray, "DualConnect.exe"), "");
            Eq(Path.Combine(tray, "DualConnect.exe"), DualConnectRunner.Locate("", tray), "next to the tray wins");
            Eq(Path.Combine(stage1, "x.exe"), DualConnectRunner.Locate(Path.Combine("..", "stage1-tools", "x.exe"), tray), "relative configured path");
            Eq(Path.Combine(root, "y.exe"), DualConnectRunner.Locate(Path.Combine(root, "y.exe"), tray), "absolute configured path");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // ---------------------------------------------------------------- runner against the fake tool

    private static string NewStateFile(string initial)
    {
        string p = Path.Combine(Path.GetTempPath(), "dctray-state-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(p, initial);
        Environment.SetEnvironmentVariable("FAKE_STATE", p);
        return p;
    }

    private static void RunnerHappyPath()
    {
        Environment.SetEnvironmentVariable("FAKE_MODE", "");
        string stateFile = NewStateFile("off");
        try
        {
            var runner = new DualConnectRunner(fakeTool, "AirPods Pro", 15);
            DualConnectResult s1 = runner.Run("status");
            True(s1.Succeeded, "status ok: " + s1.ErrorText + " " + s1.RawOutput);
            Eq(LaptopState.NotOnLaptop, StateLogic.FromStatus(s1), "starts off");
            True(s1.Message.Contains("\"--name\", \"AirPods Pro\""), "name with a space arrives as one argument: " + s1.Message);
            True(!s1.Message.Contains("--timeout"), "status gets no timeout");
            Eq("Kopfh\u00f6rer (AirPods Pro)", s1.Endpoints[0].Name, "escaped unicode survives the pipe");

            DualConnectResult t = runner.Run(StateLogic.ToggleVerb(StateLogic.FromStatus(s1)));
            Eq("take", t.Verb, "toggle from off takes");
            True(t.Succeeded && t.Message.Contains("\"--timeout\", \"15\""), "take ok with timeout: " + t.Message);
            Eq(LaptopState.OnLaptop, StateLogic.FromEndpoints(t.Endpoints), "take result shows on");
            True(t.WallMs >= 0, "wall time measured");

            DualConnectResult s2 = runner.Run("status");
            Eq(LaptopState.OnLaptop, StateLogic.FromStatus(s2), "status after take");
            DualConnectResult g = runner.Run(StateLogic.ToggleVerb(StateLogic.FromStatus(s2)));
            Eq("give", g.Verb, "toggle from on gives");
            Eq(LaptopState.NotOnLaptop, StateLogic.FromStatus(runner.Run("status")), "status after give");
        }
        finally
        {
            File.Delete(stateFile);
        }
    }

    private static void RunnerFailures()
    {
        string stateFile = NewStateFile("on");
        try
        {
            var runner = new DualConnectRunner(fakeTool, "AirPods", 15);
            Environment.SetEnvironmentVariable("FAKE_MODE", "notfound");
            DualConnectResult nf = runner.Run("status");
            Eq(2, nf.ExitCode, "exit 2");
            Eq(LaptopState.NotPaired, StateLogic.FromStatus(nf), "not paired");

            Environment.SetEnvironmentVariable("FAKE_MODE", "refused");
            DualConnectResult rf = runner.Run("take");
            Eq(3, rf.ExitCode, "exit 3");
            True(!rf.Succeeded && rf.Message.Contains("0x80070005"), "HRESULT kept");

            Environment.SetEnvironmentVariable("FAKE_MODE", "notconfirmed");
            DualConnectResult nc = runner.Run("take");
            Eq(4, nc.ExitCode, "exit 4");
            Eq(ResultKind.NotConfirmed, StateLogic.Classify(nc), "timeout kind");
            Eq(LaptopState.NotOnLaptop, StateLogic.FromEndpoints(nc.Endpoints), "endpoints still read on exit 4");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_MODE", "");
            File.Delete(stateFile);
        }
    }

    private static void RunnerContractCases()
    {
        string stateFile = NewStateFile("on");
        try
        {
            var runner = new DualConnectRunner(fakeTool, "AirPods", 20);
            LaptopState s;

            Environment.SetEnvironmentVariable("FAKE_MODE", "several");
            DualConnectResult two = runner.Run("status");
            Eq(LaptopState.SeveralDevices, StateLogic.FromStatus(two), "two pairs: status");
            Eq(4, two.Endpoints.Count, "both pairs listed");
            DualConnectResult twoTake = runner.Run("take");
            Eq(ResultKind.SeveralDevices, StateLogic.Classify(twoTake), "two pairs: take");
            var mine = new DualConnectRunner(fakeTool, "AirPods", "6f1d2a3b-0000-1111-2222-333344445555", 20);
            DualConnectResult picked = mine.Run("status");
            Eq(LaptopState.OnLaptop, StateLogic.FromStatus(picked), "--container picks one");
            True(picked.Message.Contains("\"--container\", \"6f1d2a3b-0000-1111-2222-333344445555\""), "container passed: " + picked.Message);

            Environment.SetEnvironmentVariable("FAKE_MODE", "stillrunning");
            DualConnectResult busy = runner.Run("give");
            Eq(ResultKind.StillRunning, StateLogic.Classify(busy), "still running");
            True(StateLogic.TryStateAfterAction(busy, out s) && s == LaptopState.OnLaptop, "still running keeps reading the AirPods");

            Environment.SetEnvironmentVariable("FAKE_MODE", "replaced");
            Eq(ResultKind.Superseded, StateLogic.Classify(runner.Run("take")), "replaced");

            Environment.SetEnvironmentVariable("FAKE_MODE", "stopped");
            DualConnectResult stopped = runner.Run("take");
            Eq(ResultKind.Superseded, StateLogic.Classify(stopped), "stopped");
            Eq<string>(null, StateLogic.FailureText(StateLogic.Classify(stopped), stopped, "AirPods"), "no notice for a stopped switch");

            Environment.SetEnvironmentVariable("FAKE_MODE", "lockerror");
            DualConnectResult lockErr = runner.Run("take");
            Eq(ResultKind.LockError, StateLogic.Classify(lockErr), "lock error");
            True(!StateLogic.TryStateAfterAction(lockErr, out s), "lock error leaves the state alone");

            Environment.SetEnvironmentVariable("FAKE_MODE", "watchdog");
            DualConnectResult dog = runner.Run("take");
            Eq(ResultKind.NoAnswer, StateLogic.Classify(dog), "watchdog");
            True(!StateLogic.TryStateAfterAction(dog, out s), "watchdog leaves the state alone");
            Eq(LaptopState.CheckFailed, StateLogic.FromStatus(runner.Run("status")), "watchdog on status");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_MODE", "");
            File.Delete(stateFile);
        }
    }

    private static void RunnerGarbage()
    {
        Environment.SetEnvironmentVariable("FAKE_MODE", "garbage");
        try
        {
            DualConnectResult r = new DualConnectRunner(fakeTool, "AirPods", 15).Run("status");
            True(!r.JsonParsed, "not parsed");
            Eq(0, r.ExitCode, "exit 0 but no json");
            True(!r.Succeeded || r.JsonParsed == false, "flagged");
            Eq(LaptopState.CheckFailed, StateLogic.FromStatus(r), "check failed");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MODE", ""); }
    }

    private static void RunnerChatty()
    {
        Environment.SetEnvironmentVariable("FAKE_MODE", "chatty");
        string stateFile = NewStateFile("on");
        try
        {
            DualConnectResult r = new DualConnectRunner(fakeTool, "AirPods", 15).Run("status");
            True(r.JsonParsed, "json found after other text: " + r.RawOutput);
            True(r.RawOutput.Contains("warning: something"), "stderr captured");
            Eq(LaptopState.OnLaptop, StateLogic.FromStatus(r), "state");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_MODE", "");
            File.Delete(stateFile);
        }
    }

    private static void RunnerMissing()
    {
        DualConnectResult r = new DualConnectRunner(Path.Combine(Path.GetTempPath(), "no-such-dir", "DualConnectW.exe"), "AirPods", 15).Run("take");
        True(r.LaunchFailed, "launch failed");
        True(!r.Succeeded, "not succeeded");
        Eq(LaptopState.ToolMissing, StateLogic.FromStatus(r), "tool missing");
    }

    private static void RunnerTimeout()
    {
        Environment.SetEnvironmentVariable("FAKE_MODE", "slow");
        try
        {
            var runner = new DualConnectRunner(fakeTool, "AirPods", 15) { WaitBudgetOverrideMs = 1500 };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            DualConnectResult r = runner.Run("take");
            True(r.TimedOut, "timed out");
            True(!r.Succeeded, "not succeeded");
            True(clock.ElapsedMilliseconds < 10000, "returned promptly: " + clock.ElapsedMilliseconds + " ms");
            Eq(LaptopState.CheckFailed, StateLogic.FromStatus(r), "check failed");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MODE", ""); }
    }
}
