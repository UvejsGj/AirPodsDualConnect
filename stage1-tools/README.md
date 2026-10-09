# Stage 1 tools: one-key AirPods switching and a read-only logger

**Untested on Windows.** Written on 8 and 9 Oct 2026 without a Windows machine. What was checked: the C# file compiles as C# 5 against .NET Framework reference assemblies, its switching logic passed a simulation with fake audio endpoints (20 single-command cases and 12 cases of quick key presses, a killed command and a slow driver), and the scripts parse and pass a Windows PowerShell 5.1 compatibility check. Nothing here has run on your laptop or against real AirPods. Your final checklist says when to run each part.

## What is here

| File | What it is |
| --- | --- |
| `DualConnect.cs` | Source of the switching tool (C# 5). |
| `Build.cmd` | Builds `DualConnect.exe` (for typing commands) and `DualConnectW.exe` (same tool, opens no window) with the C# compiler that ships with Windows. |
| `Status.cmd` | Shows what Windows sees for the AirPods. Changes nothing. |
| `AirPods-Log.ps1` | Read-only logger for the Stage 1 trials: a timestamped CSV of the connection state, plus one-key marks for what you hear. |
| `Install-Shortcuts.ps1` | Optional: two desktop shortcuts with keys, Ctrl+Alt+L (sound to laptop) and Ctrl+Alt+P (give back to iPhone). `-Remove` deletes them. |
| `INTERFACE.md` | The command-line contract the Stage 3 tray app relies on. |

## What it changes on the laptop

- Nothing in Windows itself: no registry edit, no driver, no service, no startup entry, no admin rights, no setting.
- Files in this folder only: the two `.exe` files from `Build.cmd`, `actions.csv` (one row per switch) and `airpods-log-*.csv` from the logger.
- `Install-Shortcuts.ps1` adds two `.lnk` files to your desktop, nothing else.

## How it switches

The tool asks Windows' own Bluetooth audio driver to connect or disconnect the AirPods' audio. It uses the method of the open-source [ToothTray](https://github.com/m2jean/ToothTray) app (`KSPROPERTY_ONESHOT_RECONNECT` and `KSPROPERTY_ONESHOT_DISCONNECT` sent through `IKsControl`), whose author says it is based on how the Windows 10 Settings app connects audio devices. The request goes to every audio endpoint of the AirPods (stereo and hands-free), because Windows only fully disconnects a headset when all of them are disconnected. The pairing stays. It cannot pair, unpair, reset or update the AirPods, and it sends nothing to the iPhone.

| Command | What it does |
| --- | --- |
| `DualConnect.exe status` | Lists the AirPods' playback and microphone endpoints and their state. |
| `DualConnect.exe connect` | Connects the AirPods to the laptop and waits until Windows can play sound to them. |
| `DualConnect.exe disconnect` | Disconnects them from the laptop; the pairing stays. |
| `DualConnect.exe take` | Sound to the laptop. If Windows already shows them connected (it can, while the iPhone plays), it disconnects first, waits for the link to drop, then connects. |
| `DualConnect.exe give` | Give them back: same as `disconnect`, so the iPhone can take them. |

Options: `--name <text>` to pick the device (default "AirPods"); `--container <id>` to pick one of two paired devices with the same name; `--timeout <seconds>` for each wait (default 20); `--json` for one line of JSON; `--no-log` to skip `actions.csv`. Exit codes are in `INTERFACE.md`.

Two safeguards:

- **Two pairs of AirPods.** If more than one paired device matches the name, the tool sends nothing and lists each one with its name, whether it is connected, and its containerId. Run it again with `--name` and text that only yours contains, or, if both have the same name, with `--container` and your pair's containerId.
- **Pressing keys quickly.** Switches never overlap, and the last key pressed wins: a press that moves the sound the other way stops the running switch and any waiting one. A second press of the same key lets the first one finish. After a stopped or failed switch, the next one keeps watching before it reports, in case the stopped one's request still lands: `give` for its whole timeout (20 seconds by default), `take` for 8 seconds. A second press of the same key during that watch reports "still running" after 5 seconds and sends nothing; the first press still finishes.

## Running it

1. Put this folder somewhere in your user folder, for example `Documents\DualConnect\stage1-tools`.
2. Double-click `Build.cmd`. It should end with "Built DualConnect.exe and DualConnectW.exe" and wait for a key.
3. Double-click `Status.cmd` to see what Windows sees.
4. For the other commands, open a Command Prompt in this folder (type `cmd` in File Explorer's address bar and press Enter) and type, for example, `DualConnect.exe take`.

The logger, from a PowerShell or Windows Terminal window in the same folder (not PowerShell ISE):

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\AirPods-Log.ps1
```

`-ExecutionPolicy Bypass` applies to that one PowerShell window only and changes no setting. Keys while it runs: L sound on laptop, P sound on iPhone, N no sound, G unwanted grab, S start of a trial, T note, Q quit. To bring its window to the front, click the title bar, not inside the window: a click inside starts a text selection that pauses the logger until you press Esc. If you open the CSV in Excel while logging, rows are kept and written once Excel closes it. Quit with Q (Ctrl+C works too); closing the window while Excel has the CSV open loses the rows not yet written.

## If Windows blocks something

Windows may refuse to run a program or script it hasn't seen before, through SmartScreen, Smart App Control, or a PowerShell "language mode" message. **Leave those settings as they are.** Copy the exact message into the project thread. On a PC where Smart App Control is on, a program built on the PC itself may not be allowed to run at all; Claude will then say what, if anything, can still be done within your rules.

## Known limits (from other projects' reports, not measured here)

- The first connect after a disconnect is sometimes ignored, so the tool repeats each request every 4 seconds while it waits. It stops repeating 4 seconds before the timeout, apart from the watch after an interrupted switch described above. A request Windows already accepted can still complete after a timeout is reported, so the AirPods may connect or drop a few seconds later; run `Status.cmd` to check.
- A connect can take more than 15 seconds when the AirPods are asleep or busy with the iPhone.
- If you switched Windows' output to the laptop speakers by hand while the AirPods were away, Windows may keep using the speakers after they reconnect (ToothTray issue #15). The same report says Windows does return to the AirPods when they were the selected output, so picking them once in the sound menu should fix it (inferred, not tested).
- Desktop shortcut keys can take about 3 seconds to react the first time.
- "Connected" means Windows holds the audio link. Whether you hear the laptop is for your ears and the logger's L and P keys to say.

## Undo

- If you made the shortcuts: `powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Shortcuts.ps1 -Remove`.
- Delete this folder.

Nothing else needs undoing, because nothing else was changed.
