# Contributing

Thanks for helping. This project runs on someone's everyday laptop and their own AirPods, so its ground rules come before any feature. Please read the [README](README.md) and the [staged plan](docs/feasibility-and-staged-plan.md) first: work happens in stages, and a stage only starts when the gate before it says so.

## Ground rules

A change that breaks one of these won't be merged, however useful it is.

- **No kernel code on the laptop.** No kernel drivers, no Test Mode, no Secure Boot changes, and no third-party virtual audio drivers.
- **No AirPods reset and no firmware changes**, ever. Nothing in this project needs either.
- **Pairing instructions say "double-tap once".** Any step that pairs the AirPods must say to double-tap the front of the open case once and stop as soon as the light flashes white. A second double-tap while it flashes starts Apple's reset ([Apple](https://support.apple.com/en-us/118531)).
- **Registry and driver changes come with their undo.** If a change writes to the registry or installs or rebinds a driver, the docs must say exactly what it changes and how to undo it, before the step that makes it. The current tools write nothing to the registry, and new code should keep it that way where it can.
- **No AirPods protocol or takeover code for now.** The Stage 2 prototype is on hold, so contributions stay on Windows' own Bluetooth controls (the Stage 1 tool and the Stage 3 tray).
- **Prefer the spare-dongle, user-mode route** if new Bluetooth software is ever needed.

## Say what was tested, and where

Most of this project has never run on the real setup. Keep that visible:

- Separate **verified** facts (checked in a source, or run) from **inference**.
- Label anything that hasn't run on real hardware as untested, and name where it did run (a Linux container, a Windows VM, a real laptop).
- In issues and pull requests, give your Windows edition and build, iOS version, AirPods model and firmware, and Bluetooth adapter.

## Building and testing the tray app

- **Windows:** double-click `stage3-app/build.cmd`. It uses the C# compiler that ships with Windows, so nothing needs installing.
- **Linux:** run `stage3-app/tests/run-tests.sh` (needs `mono-devel` and `python3`; `dotnet-sdk-8.0` adds a Roslyn C# 5 check). These tests cover the logic that doesn't need Windows.
- Keep the source to C# 5 and .NET Framework 4.x, so it still builds with Windows' own compiler.

## Privacy in logs

DualConnect's output and Windows device IDs can include your AirPods' Bluetooth address. Replace it with `XX:XX:XX:XX:XX:XX` before pasting logs into an issue.

## Licensing

Contributions are accepted under the [MIT License](LICENSE). Please don't copy code from GPL-licensed projects such as LibrePods or NTPods (both GPL-3.0) into this repository; link to them instead.

## Conduct

This project follows the [Code of Conduct](CODE_OF_CONDUCT.md). Security problems go through the [security policy](SECURITY.md), not public issues.
