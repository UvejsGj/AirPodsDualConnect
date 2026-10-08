# DualConnect.exe command-line contract (draft, being built)

Status: being written by the Stage 1 thread on 8 Oct 2026. Not run on Windows yet. This file fixes the interface early so other tools can wrap it; the behaviour behind it is untested on the user's laptop.

## Commands

`DualConnect.exe <command> [--name <text>] [--json] [--timeout <seconds>] [--no-log]`

| Command | What it does |
| --- | --- |
| `status` | Lists the Bluetooth audio endpoints whose name contains `--name` (default `AirPods`), with state and whether Windows is sending sound. Changes nothing. |
| `connect` | Asks Windows' Bluetooth audio driver to reconnect the headset (KSPROPERTY_ONESHOT_RECONNECT, the ToothTray method). Waits for the playback endpoint to become active. |
| `disconnect` | Asks the driver to disconnect the headset (KSPROPERTY_ONESHOT_DISCONNECT). The pairing stays. Waits for the endpoint to go inactive. |
| `take` | "Sound to the laptop": if the playback endpoint is already active, disconnect first, then connect. Otherwise just connect. |
| `give` | "Give the AirPods back": same as `disconnect`. |

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done; the endpoint reached the expected state |
| 1 | Bad arguments |
| 2 | No matching Bluetooth audio endpoint found |
| 3 | Windows refused the request (HRESULT in the output) |
| 4 | Request sent, but the endpoint did not reach the expected state before `--timeout` (default 15 s) |

## Output

Human-readable text by default. With `--json`, exactly one line of JSON on stdout:

```json
{"command":"take","ok":true,"exitCode":0,"elapsedMs":2380,"message":"...","endpoints":[{"name":"Headphones (AirPods Pro)","deviceName":"AirPods Pro","flow":"render","state":"active","isDefault":true,"peak":0.12,"id":"{0.0.0.00000000}.{...}","filterId":"{2}.\\?\\bthenum#..."}]}
```

The JSON line is plain ASCII: every character outside printable ASCII (for example the "ö" in "Kopfhörer (AirPods Pro)") is written as a `\uXXXX` escape, so a redirected stdout in any code page carries it intact. Text output (without `--json`) uses the console's own encoding.

`state` is always one of `active`, `unplugged`, `notpresent` or `disabled` (Windows' DEVICE_STATE_ACTIVE, _UNPLUGGED, _NOTPRESENT, _DISABLED). If Windows ever reports a value outside those four, it appears as `state<number>`, for example `state16`, and should be treated as not active. `flow` is `render` (playback) or `capture` (microphone). `peak` is the level Windows is sending or receiving, 0 to 1, or -1 when the endpoint is not active.

`status` exits 0 when at least one matching endpoint is found, whatever its state, and 2 when nothing matches. It returns 3 only if a Windows audio call throws an error, with the error in `message`; it never returns 4.

Unless `--no-log` is given, each `connect`, `disconnect`, `take` and `give` appends one row to `actions.csv` next to the exe.

## No prompts, no admin, no window

- **Never prompts.** The tool never reads from the keyboard or stdin and shows no dialogs. Every outcome is an exit code plus one output line.
- **No admin rights expected.** It uses only user-level Core Audio and IKsControl calls, the same ones the ToothTray app makes without elevation. It writes nothing except `actions.csv` beside itself. Whether Windows ever demands elevation for the reconnect request is not yet confirmed on real hardware; if it does, the tool returns exit code 3 with the HRESULT rather than prompting.
- **No console window.** The build produces two files from the same source: `DualConnect.exe` (console program, for typing commands) and `DualConnectW.exe` (built with `/target:winexe`, so Windows opens no console window). A wrapper should start `DualConnectW.exe` with stdout redirected; it writes the same output to a redirected stdout. Starting `DualConnect.exe` with `CreateNoWindow = true` and `UseShellExecute = false` also works, but `DualConnectW.exe` avoids a flash when started from a shortcut or hotkey.

## Library use

The same file is a .NET Framework 4 assembly. `[DualConnect.Core]` exposes the same operations as public static methods, so a PowerShell or C# host can load it with `Add-Type -Path DualConnect.exe` instead of starting a process.
