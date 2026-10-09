// DualConnect Tray: logic with no Windows UI or COM dependencies.
// Everything here also compiles and runs under Mono, which is how it was tested.
// Nothing in this project has been run on Windows or on the user's AirPods.
// C# 5 only, so the compiler that ships with Windows (.NET Framework 4.x csc.exe) can build it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DualConnectTray
{
    // ------------------------------------------------------------------
    // Settings file: plain key=value lines, ';' or '#' starts a comment.
    // ------------------------------------------------------------------
    public sealed class IniFile
    {
        private readonly List<string> lines = new List<string>();

        public static IniFile Parse(string text)
        {
            var ini = new IniFile();
            if (!string.IsNullOrEmpty(text))
            {
                string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
                if (normalized.EndsWith("\n")) normalized = normalized.Substring(0, normalized.Length - 1);
                ini.lines.AddRange(normalized.Split('\n'));
            }
            return ini;
        }

        public static IniFile Load(string path)
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new IniFile();
        }

        public void Save(string path)
        {
            File.WriteAllText(path, ToText(), new UTF8Encoding(false));
        }

        public string ToText()
        {
            return string.Join("\r\n", lines.ToArray()) + "\r\n";
        }

        // Returns null when the key is absent. Keys are case-insensitive; the last occurrence wins.
        public string Get(string key)
        {
            string found = null;
            foreach (string line in lines)
            {
                string k, v;
                if (TrySplit(line, out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    found = v;
            }
            return found;
        }

        // Replaces the value in place (keeping comments and order) or appends a new line.
        public void Set(string key, string value)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string k, v;
                if (TrySplit(lines[i], out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + "=" + value;
                    return;
                }
            }
            lines.Add(key + "=" + value);
        }

        private static bool TrySplit(string line, out string key, out string value)
        {
            key = null;
            value = null;
            string t = line.Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t[0] == '[') return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim();
            value = t.Substring(eq + 1).Trim();
            return key.Length > 0;
        }
    }

    public sealed class TraySettings
    {
        public const string FileName = "DualConnectTray.ini";

        public string DualConnectPath = "";      // empty: look next to the tray, then in ..\stage1-tools
        public string DeviceName = "AirPods";    // passed to DualConnect --name
        public string ContainerId = "";          // passed to DualConnect --container when set
        public string ToggleHotkey = "Win+Alt+A";
        public string TakeHotkey = "";           // empty: no hotkey
        public string GiveHotkey = "";
        public bool LeftClickToggles = false;    // false: a left click opens the menu
        public bool NotifyOnOutsideChanges = true;
        public int TimeoutSeconds = 20;          // passed to DualConnect --timeout; its own default is 20
        public int SafetyPollSeconds = 60;       // status check even when Windows reports no change; 0 = off

        public readonly List<string> Warnings = new List<string>();

        public static string DefaultFileText()
        {
            return string.Join("\r\n", new string[]
            {
                "; DualConnect Tray settings. After editing, choose \"Reload settings\" in the tray menu.",
                "; Untested on your laptop. Lines starting with ; are comments.",
                "",
                "; Path to DualConnectW.exe or DualConnect.exe from stage1-tools. Empty = look next to",
                "; the tray first, then in ..\\stage1-tools.",
                "DualConnectPath=",
                "",
                "; Text that the AirPods' audio endpoint name contains (DualConnect --name).",
                "DeviceName=AirPods",
                "",
                "; Only needed when two paired devices match DeviceName (DualConnect then sends nothing).",
                "; The containerId of yours, as stage1-tools\\Status.cmd lists it. Empty = DeviceName alone.",
                "ContainerId=",
                "",
                "; Hotkeys: modifiers Ctrl, Alt, Shift, Win joined with +, then one key (A-Z, 0-9, F1-F24,",
                "; Space, Insert, Delete, Home, End, PageUp, PageDown, Pause). Empty = no hotkey.",
                "; Toggle gives the AirPods back when Windows reports them connected, otherwise takes them.",
                "ToggleHotkey=Win+Alt+A",
                "; Take always moves the sound to the laptop (reconnects if Windows already shows them",
                "; connected). Useful if Windows stays 'connected' while your iPhone has the sound.",
                "TakeHotkey=",
                "GiveHotkey=",
                "",
                "; menu = a left click on the tray icon opens the menu; toggle = a left click switches.",
                "LeftClick=menu",
                "",
                "; Show a notification when the AirPods join or leave the laptop without you asking.",
                "NotifyOnOutsideChanges=true",
                "",
                "; Seconds DualConnect waits for each step (DualConnect --timeout, 10 to 120).",
                "; Sound to laptop can use it twice: once for the old link to drop, once to connect.",
                "TimeoutSeconds=20",
                "",
                "; Extra status check every N seconds in case Windows misses an event. 0 = off.",
                "SafetyPollSeconds=60",
                ""
            });
        }

        public static TraySettings FromIni(IniFile ini)
        {
            var s = new TraySettings();
            string v;

            v = ini.Get("DualConnectPath");
            if (v != null) s.DualConnectPath = Unquote(v);

            v = ini.Get("DeviceName");
            if (v != null)
            {
                v = Unquote(v);
                if (v.Length == 0) s.Warnings.Add("DeviceName is empty; using AirPods.");
                else s.DeviceName = v;
            }

            v = ini.Get("ContainerId");
            if (v != null)
            {
                v = Unquote(v);
                Guid id;
                if (Guid.TryParse(v, out id)) s.ContainerId = id.ToString("D");
                else if (v.Length > 0) s.Warnings.Add("ContainerId must be a containerId as Status.cmd lists it, like 6f1d2a3b-0000-1111-2222-333344445555; ignoring it.");
            }

            v = ini.Get("ToggleHotkey"); if (v != null) s.ToggleHotkey = v;
            v = ini.Get("TakeHotkey"); if (v != null) s.TakeHotkey = v;
            v = ini.Get("GiveHotkey"); if (v != null) s.GiveHotkey = v;

            v = ini.Get("LeftClick");
            if (v != null)
            {
                if (string.Equals(v, "toggle", StringComparison.OrdinalIgnoreCase)) s.LeftClickToggles = true;
                else if (string.Equals(v, "menu", StringComparison.OrdinalIgnoreCase)) s.LeftClickToggles = false;
                else s.Warnings.Add("LeftClick must be menu or toggle; using menu.");
            }

            v = ini.Get("NotifyOnOutsideChanges");
            if (v != null)
            {
                bool b;
                if (TryParseBool(v, out b)) s.NotifyOnOutsideChanges = b;
                else s.Warnings.Add("NotifyOnOutsideChanges must be true or false; using true.");
            }

            // DualConnect accepts 1 to 120; below 10 its 8-second watch after an interrupted switch
            // would use up most of a step.
            s.TimeoutSeconds = ReadInt(ini, "TimeoutSeconds", s.TimeoutSeconds, 10, 120, s.Warnings);
            s.SafetyPollSeconds = ReadInt(ini, "SafetyPollSeconds", s.SafetyPollSeconds, 0, 3600, s.Warnings);
            if (s.SafetyPollSeconds > 0 && s.SafetyPollSeconds < 10)
            {
                s.Warnings.Add("SafetyPollSeconds below 10 is too frequent; using 10.");
                s.SafetyPollSeconds = 10;
            }
            return s;
        }

        private static int ReadInt(IniFile ini, string key, int fallback, int min, int max, List<string> warnings)
        {
            string v = ini.Get(key);
            if (v == null) return fallback;
            int n;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max)
            {
                warnings.Add(key + " must be a whole number from " + min + " to " + max + "; using " + fallback + ".");
                return fallback;
            }
            return n;
        }

        private static bool TryParseBool(string v, out bool b)
        {
            string t = v.Trim().ToLowerInvariant();
            if (t == "true" || t == "yes" || t == "1" || t == "on") { b = true; return true; }
            if (t == "false" || t == "no" || t == "0" || t == "off") { b = false; return true; }
            b = false;
            return false;
        }

        private static string Unquote(string v)
        {
            string t = v.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') t = t.Substring(1, t.Length - 2);
            return t;
        }
    }

    // ------------------------------------------------------------------
    // Hotkey text such as "Win+Alt+A" -> RegisterHotKey modifiers and virtual-key code.
    // ------------------------------------------------------------------
    public sealed class Hotkey
    {
        public const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008;

        public uint Modifiers;
        public uint VirtualKey;
        public string Display;

        // Empty or whitespace text means "no hotkey": returns true with hotkey == null.
        public static bool TryParse(string text, out Hotkey hotkey, out string error)
        {
            hotkey = null;
            error = null;
            if (text == null || text.Trim().Length == 0) return true;

            string[] parts = text.Split('+');
            uint mods = 0;
            uint vk = 0;
            string keyName = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                string lower = p.ToLowerInvariant();
                bool last = i == parts.Length - 1;
                if (p.Length == 0) { error = "\"" + text + "\" has an empty part."; return false; }

                uint mod = 0;
                if (lower == "ctrl" || lower == "control") mod = ModControl;
                else if (lower == "alt") mod = ModAlt;
                else if (lower == "shift") mod = ModShift;
                else if (lower == "win" || lower == "windows") mod = ModWin;

                if (mod != 0)
                {
                    if (last) { error = "\"" + text + "\" has no key after the modifiers."; return false; }
                    if ((mods & mod) != 0) { error = "\"" + text + "\" repeats " + p + "."; return false; }
                    mods |= mod;
                    continue;
                }
                if (!last) { error = "\"" + p + "\" is not Ctrl, Alt, Shift or Win."; return false; }
                if (!TryKey(p, out vk, out keyName)) { error = "\"" + p + "\" is not a supported key."; return false; }
            }

            if (mods == 0)
            {
                error = "\"" + text + "\" needs at least one of Ctrl, Alt, Shift or Win, so it can't swallow normal typing.";
                return false;
            }
            if (mods == ModShift)
            {
                error = "\"" + text + "\" uses only Shift, which would swallow a capital letter or symbol.";
                return false;
            }

            var sb = new StringBuilder();
            if ((mods & ModControl) != 0) sb.Append("Ctrl+");
            if ((mods & ModWin) != 0) sb.Append("Win+");
            if ((mods & ModAlt) != 0) sb.Append("Alt+");
            if ((mods & ModShift) != 0) sb.Append("Shift+");
            sb.Append(keyName);
            hotkey = new Hotkey { Modifiers = mods, VirtualKey = vk, Display = sb.ToString() };
            return true;
        }

        private static bool TryKey(string name, out uint vk, out string display)
        {
            vk = 0;
            display = null;
            string u = name.ToUpperInvariant();
            if (u.Length == 1 && ((u[0] >= 'A' && u[0] <= 'Z') || (u[0] >= '0' && u[0] <= '9')))
            {
                vk = u[0];               // VK_A..VK_Z and VK_0..VK_9 equal their ASCII codes
                display = u;
                return true;
            }
            if (u.Length >= 2 && u[0] == 'F')
            {
                int n;
                if (int.TryParse(u.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 24)
                {
                    vk = (uint)(0x70 + n - 1);   // VK_F1 = 0x70
                    display = "F" + n;
                    return true;
                }
                return false;
            }
            switch (u)
            {
                case "SPACE": vk = 0x20; display = "Space"; return true;
                case "PAGEUP": case "PGUP": vk = 0x21; display = "PageUp"; return true;
                case "PAGEDOWN": case "PGDN": vk = 0x22; display = "PageDown"; return true;
                case "END": vk = 0x23; display = "End"; return true;
                case "HOME": vk = 0x24; display = "Home"; return true;
                case "INSERT": case "INS": vk = 0x2D; display = "Insert"; return true;
                case "DELETE": case "DEL": vk = 0x2E; display = "Delete"; return true;
                case "PAUSE": vk = 0x13; display = "Pause"; return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------
    // A small JSON reader for DualConnect's one-line output.
    // Objects become Dictionary<string, object>, arrays List<object>, numbers double.
    // ------------------------------------------------------------------
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("No JSON text.");
            int i = 0;
            object value = ReadValue(text, ref i, 0);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after JSON at " + i + ".");
            return value;
        }

        private static object ReadValue(string s, ref int i, int depth)
        {
            if (depth > 32) throw new FormatException("JSON nested too deeply.");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON ended early.");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i, depth);
            if (c == '[') return ReadArray(s, ref i, depth);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref i);
            throw new FormatException("Unexpected '" + c + "' at " + i + ".");
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i, int depth)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Expected a name at " + i + ".");
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at " + i + ".");
                i++;
                d[key] = ReadValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON object not closed.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("Expected ',' or '}' at " + i + ".");
            }
        }

        private static List<object> ReadArray(string s, ref int i, int depth)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON array not closed.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("Expected ',' or ']' at " + i + ".");
            }
        }

        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw new FormatException("Control character in JSON string.");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Short \\u escape.");
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("Bad \\u escape.");
                        sb.Append((char)code);   // surrogate pairs arrive as two escapes and join up naturally
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape \\" + e + ".");
                }
            }
            throw new FormatException("JSON string not closed.");
        }

        private static double ReadNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("Bad number at " + start + ".");
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + " at " + i + ".");
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }
    }

    // ------------------------------------------------------------------
    // DualConnect's result, as the tray sees it.
    // ------------------------------------------------------------------
    public sealed class EndpointInfo
    {
        public string Name = "";
        public string DeviceName = "";
        public string Flow = "";
        public string State = "";
        public bool IsDefault;
        public double Peak = -1;
        public string Id = "";
        public string ContainerId = "";   // the physical device; "" when DualConnect reports null

        public bool IsActive { get { return State == "active"; } }
        public bool IsRender { get { return Flow == "render"; } }
        // "notpresent" endpoints belong to removed or older pairings; DualConnect never acts on them.
        public bool IsPresent { get { return State == "active" || State == "unplugged"; } }
    }

    public sealed class DualConnectResult
    {
        public string Verb = "";
        public int ExitCode = -1;         // -1 when the process never finished
        public bool Ok;
        public string Message = "";
        public long ElapsedMs = -1;       // as DualConnect reported it
        public long WallMs;               // as the tray measured it, process start included
        public readonly List<EndpointInfo> Endpoints = new List<EndpointInfo>();
        public bool JsonParsed;
        public bool LaunchFailed;         // file missing or Windows refused to start it
        public bool TimedOut;             // the tray gave up waiting and killed the process
        public string RawOutput = "";
        public string ErrorText = "";     // the tray's own explanation when something went wrong

        public bool Succeeded { get { return !LaunchFailed && !TimedOut && ExitCode == 0; } }

        public static DualConnectResult FromOutput(string verb, int exitCode, string stdout, string stderr)
        {
            var r = new DualConnectResult();
            r.Verb = verb;
            r.ExitCode = exitCode;
            r.RawOutput = ((stdout ?? "") + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr)).Trim();

            string jsonLine = LastJsonLine(stdout);
            if (jsonLine == null)
            {
                r.ErrorText = "DualConnect printed no JSON line (exit code " + exitCode + ").";
                return r;
            }
            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Parse(jsonLine) as Dictionary<string, object>;
            }
            catch (FormatException ex)
            {
                r.ErrorText = "DualConnect's JSON could not be read: " + ex.Message;
                return r;
            }
            if (root == null)
            {
                r.ErrorText = "DualConnect's JSON was not an object.";
                return r;
            }

            r.JsonParsed = true;
            r.Ok = GetBool(root, "ok", false);
            r.Message = GetString(root, "message");
            r.ElapsedMs = (long)GetNumber(root, "elapsedMs", -1);
            object eps;
            if (root.TryGetValue("endpoints", out eps) && eps is List<object>)
            {
                foreach (object o in (List<object>)eps)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    r.Endpoints.Add(new EndpointInfo
                    {
                        Name = GetString(d, "name"),
                        DeviceName = GetString(d, "deviceName"),
                        Flow = GetString(d, "flow"),
                        State = GetString(d, "state"),
                        IsDefault = GetBool(d, "isDefault", false),
                        Peak = GetNumber(d, "peak", -1),
                        Id = GetString(d, "id"),
                        ContainerId = GetString(d, "containerId")
                    });
                }
            }
            return r;
        }

        private static string LastJsonLine(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return null;
            string[] lines = stdout.Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("{") && t.EndsWith("}")) return t;
            }
            return null;
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) && o is string ? (string)o : "";
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            object o;
            return d.TryGetValue(key, out o) && o is bool ? (bool)o : fallback;
        }

        private static double GetNumber(Dictionary<string, object> d, string key, double fallback)
        {
            object o;
            return d.TryGetValue(key, out o) && o is double ? (double)o : fallback;
        }
    }

    // ------------------------------------------------------------------
    // What the tray shows. "On laptop" means Windows holds the AirPods' audio link.
    // Windows can't see whether your iPhone has quietly taken the sound while the
    // laptop link stays up (plan section 2), so the tray never claims to know that.
    // ------------------------------------------------------------------
    public enum LaptopState
    {
        Unknown,
        OnLaptop,              // a playback endpoint is active and is the default output
        OnLaptopNotDefault,    // active, but Windows sends sound somewhere else by default
        NotOnLaptop,           // paired, endpoints present, none active
        NotPaired,             // DualConnect found no matching endpoint (exit code 2)
        SeveralDevices,        // more than one paired device matches; DualConnect sends nothing (exit code 1)
        ToolMissing,           // DualConnect.exe could not be started
        CheckFailed            // anything else
    }

    // What one DualConnect run came to, from its exit code and, where INTERFACE.md says
    // "the message says which", from the message text.
    public enum ResultKind
    {
        Done,                  // exit 0
        Superseded,            // exit 4: stopped or replaced by a newer DualConnect command
        StillRunning,          // exit 4: another DualConnect command still ran after 5 s; nothing was sent
        NotConfirmed,          // exit 4: Windows didn't reach the expected state before --timeout
        SeveralDevices,        // exit 1: the name matches more than one paired device; nothing was sent
        BadArguments,          // exit 1, anything else
        NotFound,              // exit 2
        LockError,             // exit 3: the one-at-a-time lock couldn't be opened; nothing was sent
        NoAnswer,              // exit 3: a Windows audio call did not return (DualConnect's watchdog)
        Refused,               // exit 3, anything else
        TrayStopped,           // the tray gave up waiting and ended the process
        LaunchFailed,          // the file is missing or Windows wouldn't start it
        Unreadable             // no JSON line, or an exit code outside 0-4
    }

    public static class StateLogic
    {
        // Phrases from DualConnect's messages (stage1-tools/DualConnect.cs). tests/CoreTests.cs
        // checks that each one still appears in that file.
        public const string PhraseSeveralDevices = "paired devices match the name";
        public const string PhraseStopped = "stopped because a newer DualConnect command started";
        public const string PhraseReplaced = "replaced by a newer DualConnect command";
        public const string PhraseStillRunning = "is still running";   // both "still running" messages; neither sends anything
        public const string PhraseLockError = "could not open the DualConnect lock";
        public const string PhraseNoAnswer = "a Windows audio call did not return";
        public const string PhraseNothingSent = "nothing was sent";

        public static readonly string[] ContractPhrases =
        {
            PhraseSeveralDevices, PhraseStopped, PhraseReplaced, PhraseStillRunning, PhraseLockError, PhraseNoAnswer, PhraseNothingSent
        };

        public static ResultKind Classify(DualConnectResult r)
        {
            if (r.LaunchFailed) return ResultKind.LaunchFailed;
            if (r.TimedOut) return ResultKind.TrayStopped;
            if (!r.JsonParsed) return ResultKind.Unreadable;
            string m = r.Message ?? "";
            switch (r.ExitCode)
            {
                case 0: return ResultKind.Done;
                case 1: return Has(m, PhraseSeveralDevices) ? ResultKind.SeveralDevices : ResultKind.BadArguments;
                case 2: return ResultKind.NotFound;
                case 3:
                    if (Has(m, PhraseLockError)) return ResultKind.LockError;
                    if (Has(m, PhraseNoAnswer)) return ResultKind.NoAnswer;
                    return ResultKind.Refused;
                case 4:
                    if (Has(m, PhraseStopped) || Has(m, PhraseReplaced)) return ResultKind.Superseded;
                    if (Has(m, PhraseStillRunning)) return ResultKind.StillRunning;
                    return ResultKind.NotConfirmed;
            }
            return ResultKind.Unreadable;
        }

        private static bool Has(string text, string phrase)
        {
            return text != null && text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static LaptopState FromStatus(DualConnectResult r)
        {
            if (r == null) return LaptopState.Unknown;
            switch (Classify(r))
            {
                case ResultKind.LaunchFailed: return LaptopState.ToolMissing;
                case ResultKind.NotFound: return LaptopState.NotPaired;
                case ResultKind.SeveralDevices: return LaptopState.SeveralDevices;
                case ResultKind.Done:
                    // status exits 0 only when it found at least one endpoint
                    return r.Endpoints.Count > 0 ? FromEndpoints(r.Endpoints) : LaptopState.CheckFailed;
                default: return LaptopState.CheckFailed;
            }
        }

        // A take or give result. Returns false when it says nothing about the AirPods: INTERFACE.md
        // says an empty endpoint list means "not read" (watchdog, Windows error, lock error),
        // not "no headset"; only exit code 2 means nothing matches.
        public static bool TryStateAfterAction(DualConnectResult r, out LaptopState state)
        {
            state = LaptopState.Unknown;
            if (r == null) return false;
            switch (Classify(r))
            {
                case ResultKind.LaunchFailed: state = LaptopState.ToolMissing; return true;
                case ResultKind.NotFound: state = LaptopState.NotPaired; return true;
                case ResultKind.SeveralDevices: state = LaptopState.SeveralDevices; return true;
                case ResultKind.TrayStopped:
                case ResultKind.Unreadable:
                case ResultKind.BadArguments:
                    return false;
            }
            if (r.Endpoints.Count == 0) return false;
            state = FromEndpoints(r.Endpoints);
            return true;
        }

        // Reads only the endpoints; callers make sure the list was actually read.
        public static LaptopState FromEndpoints(List<EndpointInfo> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0) return LaptopState.Unknown;
            bool anyActive = false, activeDefault = false;
            foreach (EndpointInfo e in endpoints)
            {
                if (!e.IsRender || !e.IsActive) continue;
                anyActive = true;
                if (e.IsDefault) activeDefault = true;
            }
            if (activeDefault) return LaptopState.OnLaptop;
            if (anyActive) return LaptopState.OnLaptopNotDefault;
            return LaptopState.NotOnLaptop;
        }

        public static bool IsOnLaptop(LaptopState s)
        {
            return s == LaptopState.OnLaptop || s == LaptopState.OnLaptopNotDefault;
        }

        // take and connect move the sound to the laptop; give and disconnect move it away.
        public static bool MovesToLaptop(string verb)
        {
            return verb == "take" || verb == "connect";
        }

        // The toggle: give back when Windows reports the link, otherwise take.
        public static string ToggleVerb(LaptopState s)
        {
            return IsOnLaptop(s) ? "give" : "take";
        }

        // While a switch runs, the toggle reverses it; DualConnect then stops the running one.
        public static string ToggleVerb(LaptopState s, string runningVerb)
        {
            if (string.IsNullOrEmpty(runningVerb)) return ToggleVerb(s);
            return MovesToLaptop(runningVerb) ? "give" : "take";
        }

        // Whether Windows may still finish the switch after DualConnect reported a failure
        // (INTERFACE.md: "a request Windows already accepted can still complete after a timeout").
        public static bool MayFinishLate(ResultKind kind, DualConnectResult r)
        {
            switch (kind)
            {
                case ResultKind.NotConfirmed:
                case ResultKind.NoAnswer:
                case ResultKind.Refused:
                case ResultKind.TrayStopped:
                case ResultKind.Unreadable:
                    return !Has(r.Message, PhraseNothingSent);
            }
            return false;
        }

        // The notice after a switch that didn't work; null when none is needed.
        public static string FailureText(ResultKind kind, DualConnectResult r, string deviceName)
        {
            switch (kind)
            {
                case ResultKind.Done:
                case ResultKind.Superseded:
                    return null;
                case ResultKind.LaunchFailed:
                    return "DualConnect could not be started. " + r.ErrorText + " Use \"Locate DualConnect.exe\" in the menu.";
                case ResultKind.TrayStopped:
                    return r.ErrorText + " Windows may still finish the switch; the icon will show it.";
                case ResultKind.StillRunning:
                    return "Another DualConnect switch was still running, so nothing was sent. Try again when it has finished.";
                case ResultKind.NotConfirmed:
                    return "Windows didn't confirm the change in time. The AirPods may be in the case, out of range or busy with the iPhone. Windows may still finish it; the icon will show it.";
                case ResultKind.SeveralDevices:
                    return "More than one paired device matches \"" + deviceName + "\", so nothing was sent. Set ContainerId in the settings to yours; Status.cmd and the log list each one.";
                case ResultKind.BadArguments:
                    return "DualConnect rejected the request: " + r.Message;
                case ResultKind.NotFound:
                    return "Windows has no audio device matching \"" + deviceName + "\". Check that the AirPods are paired with Windows and DeviceName is right.";
                case ResultKind.LockError:
                    return "DualConnect couldn't open its switch lock, so nothing was sent. A DualConnect started as administrator can cause this; close it and try again.";
                case ResultKind.NoAnswer:
                    return "A Windows audio call didn't answer, so DualConnect gave up. The switch may still finish; the icon will show it.";
                case ResultKind.Refused:
                    return "Windows refused the request. " + r.Message;
            }
            return r.ErrorText.Length > 0 ? r.ErrorText : ("DualConnect exit code " + r.ExitCode + ". " + r.Message);
        }

        public static string Describe(LaptopState s)
        {
            switch (s)
            {
                case LaptopState.OnLaptop: return "AirPods on this laptop";
                case LaptopState.OnLaptopNotDefault: return "AirPods on laptop, not the default output";
                case LaptopState.NotOnLaptop: return "AirPods not on this laptop";
                case LaptopState.NotPaired: return "AirPods not paired with Windows";
                case LaptopState.SeveralDevices: return "More than one paired device matches";
                case LaptopState.ToolMissing: return "DualConnect.exe not found";
                case LaptopState.CheckFailed: return "Last status check failed";
                default: return "Checking AirPods...";
            }
        }

        public static string VerbText(string verb)
        {
            switch (verb)
            {
                case "take": return "Sound to laptop";
                case "give": return "Give AirPods back";
                case "connect": return "Connect";
                case "disconnect": return "Disconnect";
                default: return verb;
            }
        }

        // The device name for the menu ("AirPods Pro"), preferring endpoints of the current pairing.
        public static string DeviceLabel(List<EndpointInfo> endpoints)
        {
            if (endpoints == null) return "";
            foreach (EndpointInfo e in endpoints)
                if (e.IsRender && e.IsPresent && e.DeviceName.Length > 0) return e.DeviceName;
            foreach (EndpointInfo e in endpoints)
                if (e.IsRender && e.DeviceName.Length > 0) return e.DeviceName;
            foreach (EndpointInfo e in endpoints)
                if (e.Name.Length > 0) return e.Name;
            return "";
        }

        // NotifyIcon.Text throws above 63 characters on .NET Framework.
        public static string Tooltip(string text)
        {
            if (text == null) return "";
            return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
        }
    }

    // ------------------------------------------------------------------
    // Tells apart changes the tray caused from changes that came from outside
    // (the iPhone taking the AirPods, the case closing, walking out of range).
    // ------------------------------------------------------------------
    public sealed class OutsideChangeDetector
    {
        private readonly TimeSpan grace;
        private readonly TimeSpan lateWindow;
        private int actionsRunning;
        private DateTime lastActionEndUtc = DateTime.MinValue;
        private string failedVerb;
        private DateTime failedAtUtc;

        public OutsideChangeDetector(TimeSpan grace) : this(grace, TimeSpan.Zero) { }

        // grace: changes this soon after a switch are its own. lateWindow: after a switch that
        // reported a failure, a change this soon counts as that switch finishing late.
        public OutsideChangeDetector(TimeSpan grace, TimeSpan lateWindow)
        {
            this.grace = grace;
            this.lateWindow = lateWindow;
        }

        // The failed switch the last "late-" answer belonged to.
        public string LateVerb { get; private set; }

        public void BeginAction()
        {
            actionsRunning++;
            failedVerb = null;
        }

        public void EndAction(DateTime nowUtc)
        {
            if (actionsRunning > 0) actionsRunning--;
            lastActionEndUtc = nowUtc;
        }

        // Call after the failed switch's own result has been applied.
        public void NoteFailedSwitch(string verb, DateTime nowUtc)
        {
            failedVerb = verb;
            failedAtUtc = nowUtc;
        }

        public bool IsQuietPeriod(DateTime nowUtc)
        {
            return actionsRunning == 0 && nowUtc - lastActionEndUtc > grace;
        }

        // Returns "left", "arrived", "late-left", "late-arrived" or null.
        public string Classify(LaptopState before, LaptopState after, DateTime nowUtc)
        {
            if (actionsRunning > 0) return null;
            bool wasOn = StateLogic.IsOnLaptop(before), isOn = StateLogic.IsOnLaptop(after);
            string change = null;
            if (wasOn && after == LaptopState.NotOnLaptop) change = "left";
            else if (before == LaptopState.NotOnLaptop && isOn) change = "arrived";
            if (change == null) return null;
            if (failedVerb != null && nowUtc - failedAtUtc <= lateWindow)
            {
                LateVerb = failedVerb;
                failedVerb = null;
                return "late-" + change;
            }
            if (!IsQuietPeriod(nowUtc)) return null;
            return change;
        }
    }

    // ------------------------------------------------------------------
    // Command-line quoting that matches how Windows programs split their arguments
    // (CommandLineToArgvW / the C runtime rules).
    // ------------------------------------------------------------------
    public static class ArgQuote
    {
        public static string Quote(string arg)
        {
            if (arg == null) arg = "";
            if (arg.Length > 0 && arg.IndexOfAny(new char[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string Join(IList<string> args)
        {
            var parts = new List<string>();
            foreach (string a in args) parts.Add(Quote(a));
            return string.Join(" ", parts.ToArray());
        }
    }

    // ------------------------------------------------------------------
    // The tray's own log: one CSV row per action, state change and problem.
    // ------------------------------------------------------------------
    public static class CsvLog
    {
        public const string Header = "time,event,state,verb,exitCode,elapsedMs,detail";

        public static string Row(DateTime local, string evt, LaptopState state, string verb, int? exitCode, long? elapsedMs, string detail)
        {
            return string.Join(",", new string[]
            {
                local.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
                Escape(evt),
                Escape(state.ToString()),
                Escape(verb ?? ""),
                exitCode.HasValue ? exitCode.Value.ToString(CultureInfo.InvariantCulture) : "",
                elapsedMs.HasValue ? elapsedMs.Value.ToString(CultureInfo.InvariantCulture) : "",
                Escape(detail ?? "")
            });
        }

        public static string Escape(string field)
        {
            if (field.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) < 0) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        // Splits one row written by Row(); quoted fields may contain commas and doubled quotes.
        public static List<string> Split(string row)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < row.Length; i++)
            {
                char c = row[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < row.Length && row[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }

    // ------------------------------------------------------------------
    // Summary of the tray log, for the Stage 3 checks (switch counts, failures,
    // switch time, changes that came from outside).
    // ------------------------------------------------------------------
    public static class LogSummary
    {
        public static string Summarize(IEnumerable<string> rows, DateTime sinceLocal)
        {
            var takeMs = new List<long>();
            var giveMs = new List<long>();
            int takeFailed = 0, giveFailed = 0, left = 0, arrived = 0, busy = 0, stopped = 0, late = 0, rowsRead = 0;
            DateTime first = DateTime.MaxValue, last = DateTime.MinValue;

            foreach (string row in rows)
            {
                if (string.IsNullOrEmpty(row) || row.StartsWith("time,")) continue;
                List<string> f = CsvLog.Split(row);
                if (f.Count < 7) continue;
                DateTimeOffset when;
                if (!DateTimeOffset.TryParseExact(f[0], "yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    continue;
                DateTime local = when.LocalDateTime;
                if (local < sinceLocal) continue;
                rowsRead++;
                if (local < first) first = local;
                if (local > last) last = local;

                string evt = f[1], verb = f[3];
                if (evt == "action-end")
                {
                    int code;
                    long ms;
                    bool ok = int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out code) && code == 0;
                    bool hasMs = long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
                    if (verb == "take") { if (ok && hasMs) takeMs.Add(ms); else if (!ok) takeFailed++; }
                    if (verb == "give") { if (ok && hasMs) giveMs.Add(ms); else if (!ok) giveFailed++; }
                }
                else if (evt == "outside-left") left++;
                else if (evt == "outside-arrived") arrived++;
                else if (evt == "busy") busy++;
                else if (evt == "action-stopped") stopped++;
                else if (evt == "late-left" || evt == "late-arrived") late++;
            }

            var sb = new StringBuilder();
            if (rowsRead == 0)
            {
                sb.Append("No log rows in this period yet.");
                return sb.ToString();
            }
            sb.AppendLine("From " + first.ToString("g", CultureInfo.CurrentCulture) + " to " + last.ToString("g", CultureInfo.CurrentCulture));
            sb.AppendLine();
            sb.AppendLine(Line("Sound to laptop", takeMs, takeFailed));
            sb.AppendLine(Line("Give back", giveMs, giveFailed));
            sb.AppendLine();
            sb.AppendLine("AirPods left the laptop without you asking: " + left);
            sb.AppendLine("AirPods joined the laptop without you asking: " + arrived);
            if (busy > 0) sb.AppendLine("Key presses ignored while a switch was running: " + busy);
            if (stopped > 0) sb.AppendLine("Switches stopped by a newer key press (not counted above): " + stopped);
            if (late > 0) sb.AppendLine("Switches Windows finished shortly after they were reported as failed: " + late);
            sb.AppendLine();
            sb.Append("Times are measured by the tray from key press to DualConnect's answer.");
            return sb.ToString();
        }

        private static string Line(string label, List<long> ms, int failed)
        {
            int total = ms.Count + failed;
            if (total == 0) return label + ": none";
            string s = label + ": " + ms.Count + " of " + total + " succeeded";
            if (ms.Count > 0)
                s += ", median " + Seconds(Percentile(ms, 50)) + ", slowest " + Seconds(Percentile(ms, 100));
            return s;
        }

        public static long Percentile(List<long> values, int pct)
        {
            var sorted = new List<long>(values);
            sorted.Sort();
            if (sorted.Count == 0) return 0;
            if (pct >= 100) return sorted[sorted.Count - 1];
            if (pct == 50)
            {
                int mid = sorted.Count / 2;
                return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
            }
            int idx = (int)Math.Ceiling(pct / 100.0 * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, idx))];
        }

        private static string Seconds(long ms)
        {
            return (ms / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " s";
        }
    }
}
