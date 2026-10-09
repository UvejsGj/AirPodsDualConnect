// DualConnect Tray: a notification-area icon and hotkeys around the Stage 1 tool.
//   Toggle hotkey (default Win+Alt+A): give the AirPods back if Windows reports them connected,
//   otherwise move the sound to the laptop. The icon shows what Windows reports.
// The tray itself changes nothing in Windows' Bluetooth or audio setup; DualConnect does the
// connecting and disconnecting. Untested on Windows and on the user's AirPods. C# 5.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DualConnectTray
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DualConnectTray", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "DualConnect Tray is already running. Its icon is in the notification area (you may need the ^ arrow).",
                        "DualConnect Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                try { NativeMethods.SetProcessDPIAware(); } catch (Exception) { }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new TrayApplication())
                {
                    Application.ThreadException += app.OnThreadException;
                    AppDomain.CurrentDomain.UnhandledException += app.OnUnhandledException;
                    Application.Run(app);
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    internal sealed class TrayApplication : ApplicationContext
    {
        private const int HotkeyToggle = 1, HotkeyTake = 2, HotkeyGive = 3;
        private const long MaxLogBytes = 5 * 1024 * 1024;

        private readonly string trayFolder;
        private readonly string iniPath;
        private readonly string logFolder;
        private readonly string logPath;

        private TraySettings settings;
        private DualConnectRunner runner;
        private readonly Dictionary<int, Hotkey> hotkeys = new Dictionary<int, Hotkey>();

        private readonly Control invoker;
        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip menu;
        private ToolStripMenuItem headerItem, detailItem, takeItem, giveItem, startupItem, locateItem;
        private Font regularFont, boldFont;
        private readonly HotkeyWindow hotkeyWindow;
        private AudioEndpointWatcher watcher;
        private bool watcherRunning;
        private readonly System.Windows.Forms.Timer debounceTimer;
        private readonly System.Windows.Forms.Timer pollTimer;
        private readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>();

        private LaptopState state = LaptopState.Unknown;
        private LaptopState lastSettled = LaptopState.Unknown;   // last on/off answer, skipping failed checks
        private DualConnectResult lastResult;
        private DateTime lastStatusUtc = DateTime.MinValue;
        private string lastActionNote = "";
        // Switches this tray started that haven't answered yet, oldest first (generation -> verb).
        // Normally one. A press that moves the sound the other way starts a second, and DualConnect
        // stops the first (INTERFACE.md, "One switch at a time": the newest key press wins).
        private readonly SortedDictionary<int, string> running = new SortedDictionary<int, string>();
        private const int MaxRunning = 3;
        private DateTime lastActionStartUtc = DateTime.MinValue;
        private bool statusRunning, refreshQueued;
        private int actionGeneration;
        // 5 s: changes that soon after a switch are its own. 20 s: after a switch reported as failed,
        // Windows may still finish it (INTERFACE.md), so a change that soon is that switch, not the iPhone.
        private readonly OutsideChangeDetector detector = new OutsideChangeDetector(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
        private int logFailures;

        private bool ActionRunning { get { return running.Count > 0; } }

        // The newest running switch's verb, or null.
        private string RunningVerb
        {
            get
            {
                string verb = null;
                foreach (KeyValuePair<int, string> kv in running) verb = kv.Value;
                return verb;
            }
        }

        public TrayApplication()
        {
            trayFolder = Path.GetDirectoryName(Application.ExecutablePath);
            iniPath = Path.Combine(trayFolder, TraySettings.FileName);
            logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualConnectTray");
            logPath = Path.Combine(logFolder, "events.csv");

            // A window handle on this thread, so Windows' worker-thread callbacks can be passed back here.
            invoker = new Control();
            IntPtr forceHandle = invoker.Handle;
            GC.KeepAlive(forceHandle);
            // Creating a Control normally installs this already; awaits below rely on it.
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            menu = BuildMenu();
            notifyIcon = new NotifyIcon { ContextMenuStrip = menu, Visible = false };
            notifyIcon.MouseClick += OnTrayMouseClick;

            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.HotkeyPressed += OnHotkey;

            debounceTimer = new System.Windows.Forms.Timer();
            debounceTimer.Tick += delegate { debounceTimer.Stop(); RefreshStatus("change"); };
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Tick += delegate { RefreshStatus("poll"); };

            var warnings = new List<string>();
            EnsureIniExists(warnings);
            LoadSettings(warnings);

            watcher = new AudioEndpointWatcher();
            watcher.EndpointChanged += OnEndpointChangedFromWindows;
            string watchError = watcher.Start();
            watcherRunning = watchError == null;
            if (!watcherRunning) warnings.Add(watchError + " Checking every 15 s instead.");
            ConfigurePolling();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            UpdateIcon();
            notifyIcon.Visible = true;

            Log("start", null, null, null, "tray " + Application.ExecutablePath + "; DualConnect " + runner.ExePath);
            foreach (string w in warnings) Log("warning", null, null, null, w);
            if (warnings.Count > 0)
                Balloon("DualConnect Tray started with a problem", warnings[0] + (warnings.Count > 1 ? " (more in the log)" : ""), ToolTipIcon.Warning);

            RefreshStatus("start");
        }

        // ---------------------------------------------------------------- settings

        private void EnsureIniExists(List<string> warnings)
        {
            if (File.Exists(iniPath)) return;
            try
            {
                File.WriteAllText(iniPath, TraySettings.DefaultFileText(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                warnings.Add("Could not create " + iniPath + " (" + ex.Message + "); using built-in settings.");
            }
        }

        private void LoadSettings(List<string> warnings)
        {
            IniFile ini;
            try { ini = IniFile.Load(iniPath); }
            catch (Exception ex)
            {
                warnings.Add("Could not read " + iniPath + " (" + ex.Message + "); using built-in settings.");
                ini = new IniFile();
            }
            settings = TraySettings.FromIni(ini);
            warnings.AddRange(settings.Warnings);
            runner = new DualConnectRunner(DualConnectRunner.Locate(settings.DualConnectPath, trayFolder),
                settings.DeviceName, settings.ContainerId, settings.TimeoutSeconds);
            RegisterHotkeys(warnings);
        }

        private void RegisterHotkeys(List<string> warnings)
        {
            hotkeyWindow.UnregisterAll();
            hotkeys.Clear();
            TryRegister(HotkeyToggle, "ToggleHotkey", settings.ToggleHotkey, warnings);
            TryRegister(HotkeyTake, "TakeHotkey", settings.TakeHotkey, warnings);
            TryRegister(HotkeyGive, "GiveHotkey", settings.GiveHotkey, warnings);
        }

        private void TryRegister(int id, string settingName, string text, List<string> warnings)
        {
            Hotkey hk;
            string error;
            if (!Hotkey.TryParse(text, out hk, out error))
            {
                warnings.Add(settingName + ": " + error);
                return;
            }
            if (hk == null) return;   // left empty on purpose
            foreach (Hotkey other in hotkeys.Values)
            {
                if (other.Modifiers == hk.Modifiers && other.VirtualKey == hk.VirtualKey)
                {
                    warnings.Add(settingName + ": " + hk.Display + " is already set for another action.");
                    return;
                }
            }
            string failure = hotkeyWindow.Register(id, hk);
            if (failure != null)
            {
                warnings.Add(settingName + ": " + failure + " Pick another in " + TraySettings.FileName + ".");
                return;
            }
            hotkeys[id] = hk;
        }

        private void ConfigurePolling()
        {
            int seconds = settings.SafetyPollSeconds;
            if (!watcherRunning) seconds = seconds == 0 ? 15 : Math.Min(seconds, 15);
            pollTimer.Stop();
            if (seconds > 0)
            {
                pollTimer.Interval = seconds * 1000;
                pollTimer.Start();
            }
        }

        private void ReloadSettings()
        {
            var warnings = new List<string>();
            EnsureIniExists(warnings);
            LoadSettings(warnings);
            ConfigurePolling();
            Log("reload", null, null, null, "DualConnect " + runner.ExePath);
            foreach (string w in warnings) Log("warning", null, null, null, w);
            if (warnings.Count > 0)
                Balloon("Settings reloaded with a problem", warnings[0], ToolTipIcon.Warning);
            else
                Balloon("Settings reloaded", HotkeySummary(), ToolTipIcon.Info);
            UpdateIcon();
            RefreshStatus("reload");
        }

        private string HotkeySummary()
        {
            var parts = new List<string>();
            Hotkey hk;
            if (hotkeys.TryGetValue(HotkeyToggle, out hk)) parts.Add(hk.Display + " switches");
            if (hotkeys.TryGetValue(HotkeyTake, out hk)) parts.Add(hk.Display + " takes");
            if (hotkeys.TryGetValue(HotkeyGive, out hk)) parts.Add(hk.Display + " gives back");
            return parts.Count == 0 ? "No hotkeys set." : string.Join(", ", parts.ToArray()) + ".";
        }

        // ---------------------------------------------------------------- actions

        private void OnHotkey(int id)
        {
            Hotkey hk;
            string trigger = "hotkey " + (hotkeys.TryGetValue(id, out hk) ? hk.Display : id.ToString());
            if (id == HotkeyToggle) Toggle(trigger);
            else if (id == HotkeyTake) RunAction("take", trigger);
            else if (id == HotkeyGive) RunAction("give", trigger);
        }

        // The toggle hotkey or a toggle click. While a switch runs, the toggle reverses it, except
        // within a second of its start, which is taken as an accidental double press.
        private void Toggle(string trigger)
        {
            string current = RunningVerb;
            string verb = StateLogic.ToggleVerb(state, current);
            if (current != null && DateTime.UtcNow - lastActionStartUtc < TimeSpan.FromSeconds(1))
            {
                Log("busy", verb, null, null, trigger + " ignored: pressed again within a second");
                return;
            }
            RunAction(verb, trigger);
        }

        private async void RunAction(string verb, string trigger)
        {
            // A second press the same way would only wait and then report "still running", so it is
            // ignored. A press the other way goes ahead: DualConnect stops the running switch for it.
            string reversing = RunningVerb;
            if (reversing != null)
            {
                string why = null;
                if (StateLogic.MovesToLaptop(verb) == StateLogic.MovesToLaptop(reversing))
                    why = StateLogic.VerbText(reversing) + " still running";
                else if (running.Count >= MaxRunning)
                    why = running.Count + " switches still running";
                if (why != null)
                {
                    Log("busy", verb, null, null, trigger + " ignored: " + why);
                    return;
                }
            }
            int generation = ++actionGeneration;
            running[generation] = verb;
            lastActionStartUtc = DateTime.UtcNow;
            detector.BeginAction();
            UpdateIcon();
            Log("action-start", verb, null, null, trigger + (reversing != null ? "; stops " + StateLogic.VerbText(reversing) : ""));

            DualConnectResult r;
            DualConnectRunner current = runner;
            try
            {
                r = await Task.Run(() => current.Run(verb));
            }
            catch (Exception ex)
            {
                r = new DualConnectResult { Verb = verb, ErrorText = "The tray could not run DualConnect: " + ex.Message };
            }

            DateTime endedUtc = DateTime.UtcNow;
            detector.EndAction(endedUtc);
            running.Remove(generation);
            bool newest = generation == actionGeneration;
            ResultKind kind = StateLogic.Classify(r);
            string detail = OneLine(r.ErrorText.Length > 0 ? r.ErrorText : r.Message);

            if (!newest || kind == ResultKind.Superseded)
            {
                // A newer press took over: from this tray (its own result counts), or from another
                // DualConnect such as the Stage 1 desktop shortcuts. Not a failure, so no notice.
                Log("action-stopped", verb, r.ExitCode, r.WallMs, detail);
                if (newest)
                {
                    lastActionNote = StateLogic.VerbText(verb) + " stopped by another DualConnect at " + DateTime.Now.ToString("t");
                    ApplyActionResult(r);
                }
                else UpdateIcon();
                if (!ActionRunning) ScheduleRefresh(1500);
                return;
            }

            Log("action-end", verb, r.ExitCode, r.WallMs, detail);
            lastActionNote = StateLogic.VerbText(verb) + (r.Succeeded ? " done in " + Seconds(r.WallMs) : " failed") + " at " + DateTime.Now.ToString("t");
            ApplyActionResult(r);

            string failure = StateLogic.FailureText(kind, r, settings.DeviceName);
            if (failure != null) Balloon(StateLogic.VerbText(verb) + " didn't finish", failure, ToolTipIcon.Warning);
            // After its own result is shown, so only a later change counts as finishing late.
            if (StateLogic.MayFinishLate(kind, r)) detector.NoteFailedSwitch(verb, endedUtc);
            if (!ActionRunning) ScheduleRefresh(1500);   // confirm with a fresh status a moment later
        }

        // Results that didn't read the AirPods (an empty endpoint list) leave the state as it was.
        private void ApplyActionResult(DualConnectResult r)
        {
            LaptopState s;
            if (StateLogic.TryStateAfterAction(r, out s)) ApplyState(s, r);
            else UpdateIcon();
        }

        // ---------------------------------------------------------------- status

        private void ScheduleRefresh(int delayMs)
        {
            debounceTimer.Stop();
            debounceTimer.Interval = Math.Max(1, delayMs);
            debounceTimer.Start();
        }

        private async void RefreshStatus(string reason)
        {
            if (statusRunning || ActionRunning)
            {
                refreshQueued = !ActionRunning;   // an action schedules its own check when it ends
                return;
            }
            statusRunning = true;
            int generation = actionGeneration;
            DualConnectRunner current = runner;
            DualConnectResult r;
            try
            {
                r = await Task.Run(() => current.Run("status"));
            }
            catch (Exception ex)
            {
                r = new DualConnectResult { Verb = "status", ErrorText = "The tray could not run DualConnect: " + ex.Message };
            }
            statusRunning = false;
            lastStatusUtc = DateTime.UtcNow;

            // A switch that started while this check ran makes its answer stale.
            if (generation == actionGeneration && !ActionRunning)
            {
                LaptopState s = StateLogic.FromStatus(r);
                if (s == LaptopState.CheckFailed)
                    Log("check-failed", "status", r.ExitCode, r.WallMs, OneLine(r.ErrorText.Length > 0 ? r.ErrorText : r.Message));
                ApplyState(s, r);
            }
            if (refreshQueued)
            {
                refreshQueued = false;
                ScheduleRefresh(300);
            }
        }

        private void ApplyState(LaptopState newState, DualConnectResult r)
        {
            LaptopState old = state;
            state = newState;
            lastResult = r;

            bool settled = StateLogic.IsOnLaptop(newState) || newState == LaptopState.NotOnLaptop;
            string outside = null;
            if (settled)
            {
                // Compared with the last on/off answer, so a failed check in between doesn't hide a move.
                if (lastSettled != LaptopState.Unknown) outside = detector.Classify(lastSettled, newState, DateTime.UtcNow);
                lastSettled = newState;
            }

            if (old != newState)
            {
                Log("state", r.Verb, null, null, StateLogic.Describe(old) + " -> " + StateLogic.Describe(newState));
                if (newState == LaptopState.ToolMissing)
                    Balloon("DualConnect.exe not found", "Looked for " + runner.ExePath + ". Use \"Locate DualConnect.exe\" in the menu.", ToolTipIcon.Warning);
                else if (newState == LaptopState.NotPaired)
                    Balloon("No AirPods audio device in Windows", "Nothing matches \"" + settings.DeviceName + "\". Check the pairing in Windows and DeviceName in settings.", ToolTipIcon.Warning);
                else if (newState == LaptopState.SeveralDevices)
                {
                    // DualConnect's message lists each device's name, connection and containerId.
                    Log("several-devices", r.Verb, r.ExitCode, null, OneLine(r.Message));
                    Balloon("More than one device matches \"" + settings.DeviceName + "\"",
                        "DualConnect won't guess which pair is yours, so it sends nothing. Set ContainerId in the settings to yours; Status.cmd and the tray's log list each one.",
                        ToolTipIcon.Warning);
                }
            }

            if (outside != null && outside.StartsWith("late-"))
            {
                // Windows finished a switch after DualConnect had reported it as failed.
                string lateVerb = detector.LateVerb;
                bool arrived = outside == "late-arrived";
                Log(outside, lateVerb, null, null, (arrived ? "Windows connected" : "Windows disconnected")
                    + " the AirPods shortly after " + StateLogic.VerbText(lateVerb) + " was reported as failed");
                if (arrived == StateLogic.MovesToLaptop(lateVerb))
                    Balloon(arrived ? "AirPods reached the laptop after all" : "AirPods left the laptop after all",
                        "Windows finished the switch shortly after DualConnect reported it as failed.", ToolTipIcon.Info);
            }
            else if (outside != null)
            {
                Log("outside-" + outside, null, null, null, outside == "left"
                    ? "Windows lost the AirPods without a switch from this tray"
                    : "Windows got the AirPods without a switch from this tray");
                if (settings.NotifyOnOutsideChanges)
                {
                    if (outside == "left")
                        Balloon("AirPods left the laptop", "Another device, the case, or distance took them. " + TakeHint(), ToolTipIcon.Info);
                    else
                        Balloon("AirPods joined the laptop", "Windows connected them without a switch from this tray.", ToolTipIcon.Info);
                }
            }
            UpdateIcon();
        }

        private string TakeHint()
        {
            Hotkey hk;
            if (hotkeys.TryGetValue(HotkeyTake, out hk) || hotkeys.TryGetValue(HotkeyToggle, out hk))
                return "Press " + hk.Display + " to bring them back.";
            return "Use \"Sound to laptop\" in the menu to bring them back.";
        }

        private void OnEndpointChangedFromWindows(string what, string deviceId)
        {
            // Windows' worker thread: hand over to the UI thread and do nothing else here.
            try
            {
                invoker.BeginInvoke(new Action(delegate { OnEndpointChanged(what, deviceId); }));
            }
            catch (Exception) { }   // shutting down
        }

        private void OnEndpointChanged(string what, string deviceId)
        {
            bool relevant = what == "default"
                || state == LaptopState.Unknown || state == LaptopState.NotPaired || state == LaptopState.CheckFailed
                || IsKnownEndpoint(deviceId);
            if (relevant) ScheduleRefresh(700);
        }

        private bool IsKnownEndpoint(string deviceId)
        {
            if (lastResult == null || string.IsNullOrEmpty(deviceId)) return false;
            foreach (EndpointInfo e in lastResult.Endpoints)
                if (string.Equals(e.Id, deviceId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            try
            {
                invoker.BeginInvoke(new Action(delegate
                {
                    Log("resume", null, null, null, "woke from sleep");
                    ScheduleRefresh(3000);
                }));
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- tray icon and menu

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            regularFont = new Font(m.Font, FontStyle.Regular);
            boldFont = new Font(m.Font, FontStyle.Bold);
            headerItem = new ToolStripMenuItem("Checking AirPods...") { Enabled = false };
            headerItem.Font = boldFont;
            detailItem = new ToolStripMenuItem("") { Enabled = false };
            takeItem = new ToolStripMenuItem("Sound to laptop", null, delegate { RunAction("take", "menu"); });
            giveItem = new ToolStripMenuItem("Give AirPods back", null, delegate { RunAction("give", "menu"); });
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            locateItem = new ToolStripMenuItem("Locate DualConnect.exe...", null, delegate { LocateDualConnect(); });

            m.Items.Add(headerItem);
            m.Items.Add(detailItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(takeItem);
            m.Items.Add(giveItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Check now", null, delegate { RefreshStatus("menu"); }));
            m.Items.Add(new ToolStripMenuItem("Sound settings", null, delegate { OpenUri("ms-settings:sound"); }));
            m.Items.Add(new ToolStripMenuItem("Bluetooth settings", null, delegate { OpenUri("ms-settings:bluetooth"); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Switch summary (last 7 days)...", null, delegate { ShowSummary(); }));
            m.Items.Add(new ToolStripMenuItem("Open log folder", null, delegate { OpenFolder(logFolder); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(startupItem);
            m.Items.Add(new ToolStripMenuItem("Edit settings", null, delegate { EditSettings(); }));
            m.Items.Add(new ToolStripMenuItem("Reload settings", null, delegate { ReloadSettings(); }));
            m.Items.Add(locateItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitTray(); }));
            m.Opening += delegate { RefreshMenu(); };
            return m;
        }

        private void RefreshMenu()
        {
            string current = RunningVerb;
            headerItem.Text = current != null ? StateLogic.VerbText(current) + "..." : StateLogic.Describe(state);

            string device = lastResult == null ? "" : StateLogic.DeviceLabel(lastResult.Endpoints);
            string checkedAt = lastStatusUtc == DateTime.MinValue ? "not checked yet" : "checked " + lastStatusUtc.ToLocalTime().ToString("t");
            detailItem.Text = (device.Length > 0 ? device + ", " : "") + checkedAt + (lastActionNote.Length > 0 ? "; " + lastActionNote : "");

            // While a switch runs, only the opposite one is offered: it stops the running one.
            bool on = StateLogic.ToggleVerb(state, current) == "give";   // what the toggle does next
            takeItem.Enabled = current == null || !StateLogic.MovesToLaptop(current);
            giveItem.Enabled = current == null || StateLogic.MovesToLaptop(current);
            takeItem.Font = on ? regularFont : boldFont;     // bold = what the toggle hotkey does next
            giveItem.Font = on ? boldFont : regularFont;
            takeItem.ShortcutKeyDisplayString = HotkeyText(on ? HotkeyTake : HotkeyToggle, HotkeyTake);
            giveItem.ShortcutKeyDisplayString = HotkeyText(on ? HotkeyToggle : HotkeyGive, HotkeyGive);
            takeItem.ShowShortcutKeys = takeItem.ShortcutKeyDisplayString.Length > 0;
            giveItem.ShowShortcutKeys = giveItem.ShortcutKeyDisplayString.Length > 0;

            startupItem.Checked = StartupPointsHere();
            locateItem.Visible = state == LaptopState.ToolMissing;

            if (!ActionRunning && DateTime.UtcNow - lastStatusUtc > TimeSpan.FromSeconds(15)) RefreshStatus("menu");
        }

        private string HotkeyText(int preferredId, int fallbackId)
        {
            Hotkey hk;
            if (hotkeys.TryGetValue(preferredId, out hk) || hotkeys.TryGetValue(fallbackId, out hk)) return hk.Display;
            return "";
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (settings.LeftClickToggles)
            {
                Toggle("tray click");
                return;
            }
            // NotifyIcon opens its menu only on right click; this private method is the one it uses.
            MethodInfo show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (show != null) show.Invoke(notifyIcon, null);
            else menu.Show(Cursor.Position);
        }

        private void UpdateIcon()
        {
            string key;
            string text;
            if (ActionRunning)
            {
                key = "busy";
                text = "DualConnect: " + StateLogic.VerbText(RunningVerb) + "...";
            }
            else
            {
                switch (state)
                {
                    case LaptopState.OnLaptop: key = "on"; break;
                    case LaptopState.OnLaptopNotDefault: key = "on-not-default"; break;
                    case LaptopState.NotOnLaptop: key = "off"; break;
                    case LaptopState.Unknown: key = "busy"; break;
                    default: key = "problem"; break;
                }
                text = "DualConnect: " + StateLogic.Describe(state);
            }
            notifyIcon.Icon = GetIcon(key);
            notifyIcon.Text = StateLogic.Tooltip(text);
        }

        private Icon GetIcon(string key)
        {
            Icon icon;
            if (icons.TryGetValue(key, out icon)) return icon;
            int size = Math.Max(16, Math.Min(64, SystemInformation.SmallIconSize.Width));
            icon = TrayIcons.Draw(key, size);
            icons[key] = icon;
            return icon;
        }

        private void Balloon(string title, string text, ToolTipIcon kind)
        {
            try
            {
                notifyIcon.ShowBalloonTip(6000, Truncate(title, 63), Truncate(text, 255), kind);
            }
            catch (Exception ex)
            {
                Log("notify-failed", null, null, null, ex.Message);
            }
        }

        // ---------------------------------------------------------------- menu commands

        private bool StartupPointsHere()
        {
            if (!StartupShortcut.Exists) return false;
            string target = StartupShortcut.Target();
            return target != null && string.Equals(Path.GetFullPath(target), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleStartup()
        {
            try
            {
                if (StartupPointsHere())
                {
                    StartupShortcut.Delete();
                    Log("startup-off", null, null, null, "deleted " + StartupShortcut.ShortcutPath);
                    Balloon("Won't start with Windows", "Removed the shortcut from your Startup folder.", ToolTipIcon.Info);
                }
                else
                {
                    StartupShortcut.Create(Application.ExecutablePath);
                    Log("startup-on", null, null, null, "created " + StartupShortcut.ShortcutPath);
                    Balloon("Will start with Windows", "Added a shortcut to your Startup folder. Untick this item to remove it.", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Log("error", null, null, null, "Start with Windows: " + ex.Message);
                Balloon("Start with Windows didn't change", ex.Message, ToolTipIcon.Warning);
            }
        }

        private void LocateDualConnect()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Find DualConnectW.exe or DualConnect.exe";
                dialog.Filter = "DualConnect|DualConnectW.exe;DualConnect.exe|Programs (*.exe)|*.exe";
                dialog.InitialDirectory = Directory.Exists(Path.GetDirectoryName(runner.ExePath)) ? Path.GetDirectoryName(runner.ExePath) : trayFolder;
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    IniFile ini = IniFile.Load(iniPath);
                    ini.Set("DualConnectPath", dialog.FileName);
                    ini.Save(iniPath);
                }
                catch (Exception ex)
                {
                    Balloon("Couldn't save the path", ex.Message, ToolTipIcon.Warning);
                    return;
                }
                ReloadSettings();
            }
        }

        private void EditSettings()
        {
            var warnings = new List<string>();
            EnsureIniExists(warnings);
            try
            {
                Process.Start("notepad.exe", ArgQuote.Quote(iniPath));
            }
            catch (Exception ex)
            {
                Balloon("Couldn't open settings", ex.Message, ToolTipIcon.Warning);
            }
        }

        private void ShowSummary()
        {
            string text;
            try
            {
                var rows = new List<string>();
                if (File.Exists(logPath))
                {
                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) rows.Add(line);
                    }
                }
                text = LogSummary.Summarize(rows, DateTime.Now.AddDays(-7));
            }
            catch (Exception ex)
            {
                text = "Couldn't read the log: " + ex.Message;
            }
            MessageBox.Show(text, "DualConnect Tray: last 7 days", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex) { Balloon("Couldn't open " + uri, ex.Message, ToolTipIcon.Warning); }
        }

        private void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", ArgQuote.Quote(folder));
            }
            catch (Exception ex)
            {
                Balloon("Couldn't open the folder", ex.Message, ToolTipIcon.Warning);
            }
        }

        private void ExitTray()
        {
            Log("exit", null, null, null, "");
            ExitThread();
        }

        // ---------------------------------------------------------------- log and errors

        private void Log(string evt, string verb, int? exitCode, long? elapsedMs, string detail)
        {
            try
            {
                Directory.CreateDirectory(logFolder);
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    string old = Path.Combine(logFolder, "events.old.csv");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(logPath, old);
                }
                string row = CsvLog.Row(DateTime.Now, evt, state, verb, exitCode, elapsedMs, detail);
                string text = (File.Exists(logPath) ? "" : CsvLog.Header + "\r\n") + row + "\r\n";
                File.AppendAllText(logPath, text, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                logFailures++;   // the log is best effort; never let it stop a switch
            }
        }

        internal void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log("error", null, null, null, OneLine(e.Exception.ToString()));
            Balloon("DualConnect Tray hit an error", e.Exception.Message + " Details are in the log.", ToolTipIcon.Error);
        }

        internal void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("error", null, null, null, OneLine(Convert.ToString(e.ExceptionObject)));
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 1000 ? t.Substring(0, 1000) + "..." : t;
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
        }

        private static string Seconds(long ms)
        {
            return (ms / 1000.0).ToString("0.0") + " s";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                debounceTimer.Dispose();
                pollTimer.Dispose();
                if (watcher != null) watcher.Dispose();
                hotkeyWindow.Dispose();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                menu.Dispose();
                regularFont.Dispose();
                boldFont.Dispose();
                foreach (Icon icon in icons.Values) icon.Dispose();
                icons.Clear();
                invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    // Tray icons drawn at run time, so the project needs no image files.
    // Shape and colour both change, so the state reads without relying on colour alone.
    // ------------------------------------------------------------------
    internal static class TrayIcons
    {
        public static Icon Draw(string key, int size)
        {
            using (Bitmap bmp = Render(key, size))
            {
                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                        return (Icon)temp.Clone();   // the clone owns its own copy of the handle
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        // Separate from Draw so the pictures can be checked without Windows (tests/RenderIcons.cs).
        internal static Bitmap Render(string key, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float pad = Math.Max(1f, size * 0.08f);
                var disc = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
                float stroke = Math.Max(1.5f, size / 8f);

                switch (key)
                {
                    case "on":               // green disc with a white tick: Windows holds the AirPods
                        FillDisc(g, disc, Color.FromArgb(46, 160, 67));
                        DrawTick(g, disc, stroke, Color.White);
                        break;
                    case "on-not-default":   // amber ring with a tick: connected, but not the default output
                        var outline = RectangleF.Inflate(disc, -stroke / 2, -stroke / 2);
                        using (var pen = new Pen(Color.FromArgb(214, 140, 0), stroke)) g.DrawEllipse(pen, outline);
                        DrawTick(g, disc, stroke, Color.FromArgb(214, 140, 0));
                        break;
                    case "off":              // grey ring: AirPods are not on this laptop
                        var ring = RectangleF.Inflate(disc, -stroke / 2, -stroke / 2);
                        using (var pen = new Pen(Color.FromArgb(140, 140, 140), stroke)) g.DrawEllipse(pen, ring);
                        break;
                    case "busy":             // blue open ring: switching or checking
                        var arc = RectangleF.Inflate(disc, -stroke / 2, -stroke / 2);
                        using (var pen = new Pen(Color.FromArgb(30, 120, 220), stroke)) g.DrawArc(pen, arc, -60, 300);
                        break;
                    default:                 // red disc with "!": something needs attention
                        FillDisc(g, disc, Color.FromArgb(205, 45, 45));
                        DrawBang(g, disc);
                        break;
                }
            }
            return bmp;
        }

        private static void FillDisc(Graphics g, RectangleF r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1f)) g.DrawEllipse(p, r);
        }

        private static void DrawTick(Graphics g, RectangleF r, float stroke, Color color)
        {
            var pts = new PointF[]
            {
                new PointF(r.Left + r.Width * 0.27f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.44f, r.Top + r.Height * 0.68f),
                new PointF(r.Left + r.Width * 0.74f, r.Top + r.Height * 0.34f)
            };
            using (var p = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, pts);
        }

        private static void DrawBang(Graphics g, RectangleF r)
        {
            float w = Math.Max(2f, r.Width * 0.16f);
            float cx = r.Left + r.Width / 2;
            using (var b = new SolidBrush(Color.White))
            {
                g.FillRectangle(b, cx - w / 2, r.Top + r.Height * 0.2f, w, r.Height * 0.38f);
                g.FillEllipse(b, cx - w / 2, r.Top + r.Height * 0.66f, w, w);
            }
        }
    }
}
