# DualConnect.exe command-line contract

Status: final for now, 9 Oct 2026. Written by the Stage 1 thread without a Windows machine: the code compiles as C# 5 and its switching logic passed a simulation with fake endpoints, but nothing has run on the user's laptop or against real AirPods yet. If the real tool behaves differently, this file and the tool change together.

## Commands

`DualConnect.exe <command> [--name <text>] [--container <id>] [--json] [--timeout <seconds>] [--no-log]`

| Command | What it does |
| --- | --- |
| `status` | Lists the Bluetooth audio endpoints whose name contains `--name` (default `AirPods`), with state and whether Windows is sending sound. Changes nothing. |
| `connect` | Asks Windows' Bluetooth audio driver to connect the headset (KSPROPERTY_ONESHOT_RECONNECT, the ToothTray method). Waits up to `--timeout` for a playback endpoint to become active. |
| `disconnect` | Asks the driver to disconnect the headset (KSPROPERTY_ONESHOT_DISCONNECT). The pairing stays. Waits up to `--timeout` until no endpoint of the headset is active. |
| `take` | "Sound to the laptop". If any endpoint of the headset is active (Windows can hold the link while the iPhone plays), it disconnects first and waits up to `--timeout` for the drop. Then it connects and waits up to `--timeout` again, counted from the drop. It never sends the connect while the old link is still up. |
| `give` | "Give the AirPods back": same as `disconnect`. |

`--timeout` is 1 to 120 seconds, default **20**. While waiting, the tool repeats its request every 4 seconds and stops repeating 4 seconds before the timeout, except for the repeat after an interrupted switch described under "One switch at a time". A request Windows already accepted can still complete after a timeout is reported, so the headset may connect or drop a few seconds later; run `status` to check.

## Which device it acts on

Endpoints are matched when their name or device name contains `--name`, ignoring case. An empty `--name` is rejected. `--container <id>` keeps only the endpoints of the device with that `containerId` (a GUID, as `status --json` shows it). The matched endpoints are grouped by physical device (`containerId`). Only endpoints that are `active` or `unplugged` count; `notpresent` ones belong to removed or older pairings and are never sent anything.

If more than one physical device matches, no command sends anything: it exits 1 and the message lists each device's name, whether it is connected and its `containerId`. When the names differ, a `--name` that only one of them contains picks it; when they are the same, `--container` does.

## One switch at a time

`connect`, `disconnect`, `take` and `give` never run at the same time, even from separate processes, and the newest key press wins. `connect` and `take` move the sound to the laptop; `disconnect` and `give` move it away.

- A new command stops every running or waiting command that moves the sound the other way. A running one that is stopped exits 4 with `... stopped because a newer DualConnect command started`, even when the stop arrives just as its own switch completes; if it had already asked Windows to connect, it first asks Windows to drop that connect again. A waiting one exits 4 with `replaced by a newer DualConnect command; nothing was sent`.
- A new command waits up to 5 seconds for a running one to finish or stop. If it doesn't get the lock in time, it exits 4 and sends nothing (`... is still running; nothing was sent`).
- If the command it waited for connected the laptop successfully, a `take` or `connect` that finds the sound connected exits 0 with `already connected by the previous command` instead of dropping the link and connecting again.
- If the command it waited for was stopped or failed after Windows accepted one of its requests, or was killed, that request can still land. A `give` or `disconnect` then watches until its `--timeout` ends, even when the headset is already disconnected, drops the link again whenever it comes back, and reports only at the end. A `connect` or `take` watches for 8 seconds from the start of its connect step and asks to connect once more if the link drops in that time. It doesn't keep trying after that, so it never fights the iPhone for long (an incoming call, for example).
- A second press of the same key while the first command is still watching exits 4 after 5 seconds with `... is still running; nothing was sent`. The first command still finishes the switch.

`status` never waits and never stops anything. The lock and events live in the user's own session: `Local\DualConnect.Switch`, `Local\DualConnect.Arrive`, `Local\DualConnect.StopToLaptop`, `Local\DualConnect.StopToPhone`, `Local\DualConnect.AimToLaptop`, `Local\DualConnect.LastToLaptopOk` and `Local\DualConnect.LastUnsettled`.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done; the headset reached the expected state, or was already in it ("already connected", "already disconnected") |
| 1 | Bad arguments, or `--name` matches more than one paired device (nothing was sent) |
| 2 | No matching Bluetooth audio endpoint found |
| 3 | Windows refused the request (the HRESULTs are in `message`), a Windows audio call threw an error, a call did not return (see below), or the one-at-a-time lock could not be opened (`could not open the DualConnect lock: ...; nothing was sent`, for example while a DualConnect started as administrator holds it) |
| 4 | The headset did not reach the expected state before `--timeout`; or the command was stopped or replaced by a newer one; or another command was still running after 5 seconds and nothing was sent. The message says which. |

If a Windows call never returns, a watchdog prints one result with exit code 3 and the message `a Windows audio call did not return; gave up`, and the process exits. It fires 15 seconds after the start for `status`. For the other commands it first fires 20 seconds after the start if the command is still waiting for the lock or reading the headset's state; once the command holds the lock it is reset to `--timeout` + 8 seconds for `connect`, `disconnect` and `give`, and twice `--timeout` + 8 seconds for `take`. So the longest a `take` can run is about 5.5 + 2 × timeout + 8 seconds (up to half a second to register, 5 seconds of lock wait), and a command that never gets the lock reports within 20 seconds.

## Output

Human-readable text by default. With `--json` (anywhere on the command line), exactly one line of JSON on stdout, including for argument errors:

```json
{"command":"take","ok":true,"exitCode":0,"elapsedMs":2380,"message":"...","endpoints":[{"name":"Headphones (AirPods Pro)","deviceName":"AirPods Pro","flow":"render","state":"active","isDefault":true,"peak":0.12,"id":"{0.0.0.00000000}.{...}","filterId":"{2}.\\?\\bthenum#...","containerId":"6f1d...-..."}]}
```

- The JSON line is plain ASCII: every character outside printable ASCII (for example the "ö" in "Kopfhörer (AirPods Pro)") is written as a `\uXXXX` escape, so a redirected stdout in any code page carries it intact. Text output (without `--json`) uses the console's own encoding.
- Argument errors have `"command":"usage"`.
- `elapsedMs` is the whole run, including any wait for another command.
- `state` is one of `active`, `unplugged`, `notpresent` or `disabled` (Windows' DEVICE_STATE_ACTIVE, _UNPLUGGED, _NOTPRESENT, _DISABLED). A value outside those four appears as `state<number>`, for example `state16`, and should be treated as not active.
- `flow` is `render` (playback) or `capture` (microphone).
- `peak` is the level Windows is sending to a playback endpoint, 0 to 1. It is -1 when the endpoint is not active or the level can't be read, and always -1 for microphones: the tool deliberately never opens anything on a microphone endpoint.
- `filterId` is the first Bluetooth kernel-streaming filter behind the endpoint; `containerId` identifies the physical device. Either can be null.
- `endpoints`:
  - `status`, exit 1 (several devices match), exit 2 (nothing matches) and the exit-4 results that sent nothing (`replaced`, `still running`) list every matching endpoint, read just before the tool exits. The exit-4 ones leave the list empty if that read fails.
  - Every other result of `connect`, `disconnect`, `take` and `give` lists the chosen device's endpoints as last read.
  - The watchdog result, a Windows error (exit 3 with an exception name) and the lock error have an empty list, and so do argument errors.
  - An empty list means "not read", not "no headset": only exit 2 says that nothing matches. Don't infer the headset's state from an empty list.

`status` exits 0 when at least one matching endpoint is found, whatever its state; its message is `connected` when a playback endpoint of the device is active, otherwise `not connected`. It exits 1 when several devices match, 2 when nothing matches, and 3 only on an error. It never returns 4.

Unless `--no-log` is given, each `connect`, `disconnect`, `take` and `give` appends one row to `actions.csv` next to the exe: `time,command,result,exitCode,elapsedMs,renderState,captureState,message`. If the row can't be written (for example the file is open in Excel), the command still runs and its message ends with `(actions.csv not written: <reason>)`.

## No prompts, no admin, no window

- **Never prompts.** The tool never reads from the keyboard or stdin and shows no dialogs. Every outcome is an exit code plus one output line.
- **No admin rights expected.** It uses only user-level Core Audio and IKsControl calls, the same ones the ToothTray app makes without elevation, plus the session-local lock and events listed above. It writes nothing except `actions.csv` beside itself. Whether Windows ever demands elevation for the request is not yet confirmed on real hardware; if it does, the tool returns exit code 3 with the HRESULT rather than prompting.
- **No console window.** The build produces two files from the same source: `DualConnect.exe` (console program, for typing commands) and `DualConnectW.exe` (built with `/target:winexe`, so Windows opens no console window). A wrapper should start `DualConnectW.exe` with stdout redirected; it writes the same output to a redirected stdout. Starting `DualConnect.exe` with `CreateNoWindow = true` and `UseShellExecute = false` also works.

## Library use

The same file is a .NET Framework 4 assembly. Load it with `[System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes("<full path>\DualConnect.exe"))`. `Add-Type -Path` does not accept an .exe, and `Assembly.LoadFrom` works but locks the file, so `Build.cmd` can't rebuild it while the host runs. `[DualConnect.Core]` then exposes `Status`, `Connect`, `Disconnect`, `Take` and `Give` as public static methods returning a `Result`, plus the read-only `FindEndpoints(name, resolveFilters)` and `Observe(name)`, which the logger uses. Running the exe as a process, as the tray does, is the supported way: in-process callers get no watchdog, no one-at-a-time lock and no `actions.csv` row.
