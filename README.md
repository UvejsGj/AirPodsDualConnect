# AirPods Dual-Connect (iPhone + Windows)

The goal: keep AirPods Pro 3 connected to an iPhone and an everyday Windows laptop at the same time, with sound coming from one device at a time and a quick way to switch on demand.

The project follows a staged plan with gates. Stage 1 measures what the AirPods do on a stock setup, with no system changes. Stage 2 is a proof of concept, and Stage 3 builds the real tool. A stage only starts if the gate before it says so, and if quick manual switching turns out to be good enough in Stage 1, the project stops there.

> **Nothing in this repository has been run on the real setup** (iPhone, Windows laptop, AirPods Pro 3 on firmware 9A348). The code was compiled and tested in a Linux container only. Treat every statement about how it behaves on your devices as inference until your own trials confirm it.

> **Start here:** [docs/hands-on-checklist.md](docs/hands-on-checklist.md) is the single ordered list of every step for you to do on the laptop and iPhone, Stage 1 trials first. Tick it off in the [live doc](https://claude.ai/code/artifact/6098c67b-f0e9-41c0-86b5-50b97285a0a5).

> **Pairing safety.** If any step asks you to pair the AirPods with Windows, double-tap the front of the open case **once** and stop as soon as the light flashes white. A second double-tap while it flashes is the start of Apple's reset sequence, which wipes the iPhone setup ([Apple: reset your AirPods](https://support.apple.com/en-us/118531)). Nothing in this project needs a reset; if anything asks for one, stop.

## Where each stage stands (9 Oct 2026)

| Stage | What it is | Status | Where |
| --- | --- | --- | --- |
| 1. Measure | Timed trials of switching by hand between iPhone and laptop, two iPhone settings, a microphone check and one ordinary day, then a pass/fail gate. Also a one-key Windows connect/disconnect tool (DualConnect) and a read-only trial logger. | Plan ready, trials not run yet. DualConnect and the logger are final; compiled and simulated in a container, never run on Windows. | [docs/stage1-measurement-plan.md](docs/stage1-measurement-plan.md), [docs/stage1-trial-log.md](docs/stage1-trial-log.md), [stage1-tools/](stage1-tools/) |
| 2. Proof of concept | Desk research on how to test a second live link and a "takeover" without Test Mode or a kernel driver. Recommends a spare Realtek USB dongle driven by Google's Bumble; a Linux live USB is the fallback. | Research only. The dongle prototype was stopped by an automatic safety check before any code was written, so nothing is built. Only needed if Stage 1 fails its gate. | [docs/stage2-research.md](docs/stage2-research.md) |
| 3. Develop | DualConnect Tray: a notification-area icon plus a Win+Alt+A hotkey that moves the AirPods to or from the laptop by running the Stage 1 tool. It uses Windows' own Bluetooth connect/disconnect, so it does not depend on Stage 2. | Code written; compiles and passes 22 tests in a Linux container. Never run on Windows. | [stage3-app/](stage3-app/) |

The full reasoning, evidence and risks behind the stages are in [docs/feasibility-and-staged-plan.md](docs/feasibility-and-staged-plan.md).

## What is verified and what is inference

**Verified in sources** (read on 8 Oct 2026, cited in the docs):

- AirPods work with non-Apple devices as a normal Bluetooth headset, and pairing mode is one double-tap on the open case ([Apple pairing guide](https://support.apple.com/guide/airpods/pair-airpods-with-a-non-apple-device-dev499c9718b/web)).
- Apple documents automatic switching only between Apple devices, and AirPods firmware updates cannot be declined or rolled back.
- Windows exposes a documented request that asks the Bluetooth audio driver to reconnect or disconnect a headset ([KSPROPERTY_ONESHOT_RECONNECT](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect)). DualConnect and the tray are built on it.
- None of the Windows projects reviewed implements AirPods switching between hosts; LibrePods does it on Android and Linux only (read in their code, not run).

**Verified in a Linux container only** (by the sessions that wrote the code, per each folder's README):

- [DualConnect](stage1-tools/README.md) compiles as C# 5 against .NET Framework reference assemblies, and its switching logic passed a simulation with fake audio endpoints (20 single-command cases and 12 cases of quick key presses, a killed command and a slow driver). The PowerShell scripts parse and pass a Windows PowerShell 5.1 compatibility check.
- [The tray app](stage3-app/README.md#how-it-was-tested) compiles as C# 5 against .NET Framework 4.8 reference assemblies, and 22 tests of its non-Windows logic pass against a stand-in for DualConnect.

**Inference, untested on your setup:**

- Whether Windows keeps its link while the iPhone plays, and how long a manual switch takes. Stage 1 measures this.
- Whether DualConnect's reconnect works while the iPhone holds the AirPods, and whether it needs admin rights.
- Everything about Stage 2's dongle route working with AirPods Pro 3 on firmware 9A348.

## What each folder holds

| Path | Contents |
| --- | --- |
| [docs/](docs/) | Your hands-on checklist, the feasibility and staged plan, and snapshots of the Stage 1 measurement plan, its trial log and the Stage 2 research. The checklist, Stage 1 and Stage 2 files are copies of live Claude Docs; each links back to the live version, which is the one to edit. |
| [stage1-tools/](stage1-tools/) | DualConnect: C# source (`DualConnect.cs`), `Build.cmd` (builds `DualConnect.exe` and the windowless `DualConnectW.exe` with the compiler that ships with Windows), `Status.cmd`, the read-only trial logger `AirPods-Log.ps1`, optional desktop shortcuts (`Install-Shortcuts.ps1`, Ctrl+Alt+L and Ctrl+Alt+P), its [README](stage1-tools/README.md) and the command-line contract the tray relies on ([INTERFACE.md](stage1-tools/INTERFACE.md)). |
| [stage3-app/](stage3-app/) | DualConnect Tray: C# source (`src/`), `build.cmd` (builds with the compiler that ships with Windows, no install), Linux tests (`tests/`), the icon sheet (`docs/`), its own [README](stage3-app/README.md) and [hands-on steps](stage3-app/HANDS-ON-STEPS.md). |
| [notes/](notes/) | Review of whether the BatteryHub probe can log the AirPods' Windows-side connection state during Stage 1 trials. |

## Ground rules this project keeps

- No Test Mode, no Secure Boot changes and no kernel driver on the laptop.
- The spare-dongle, user-mode route is preferred whenever new Bluetooth software is needed.
- Never reset the AirPods or touch their firmware.
- Any registry or driver change is explained, with its exact undo, before it is suggested. Neither DualConnect nor the tray writes to the registry, installs a driver or needs admin rights. The tray's "Start with Windows" is a shortcut in your Startup folder.

## What to do first

Follow the [hands-on checklist](docs/hands-on-checklist.md) from the top: baseline and pairing, then the Stage 1 trials from the [measurement plan](docs/stage1-measurement-plan.md), logged in the live doc's Trial log tab. The checklist's gate decides whether the one-key tool, the tray app or Stage 2 comes next.

## Contributing, conduct and license

See [CONTRIBUTING.md](CONTRIBUTING.md) for the ground rules, how to build and test, and what to include in a report. The project follows the [Code of Conduct](CODE_OF_CONDUCT.md), security problems go through the [security policy](SECURITY.md), and the code is under the [MIT License](LICENSE).
