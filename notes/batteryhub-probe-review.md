# BatteryHub probe: can it log AirPods connection state for Stage 1?

Read on 8 Oct 2026 from UvejsGj/BatteryHub, branch `claude/compassionate-bardeen-b0dlq4` at commit `f20d136` (the branch behind open PR #1). `main` holds only an empty README, so the probe exists only on that branch. Read only: nothing was built, run, edited or pushed.

## Verdict

Partly useful. `BatteryHub.Probe bt` can show, from Windows' side, whether Windows thinks the AirPods are connected, using only read calls that need no driver, no admin rights and no registry change. It is a one-shot snapshot with no timestamps, it cannot tell which device has the sound, and BatteryHub has no BLE advertisement scanner yet. So it can support Stage 1 action 2 (recording status on the Windows side) and does nothing for action 4 (watching the AirPods' advertisements).

## What the code shows (verified by reading, not by running)

- **Three separate connection signals per paired device**, all printed by `bt`:
  1. Windows' paired-device list (classic and LE) with `System.Devices.Aep.IsConnected`, from WinRT `DeviceInformation.FindAllAsync` with the paired-only selectors (`src/BatteryHub.Core/BluetoothProperty/WindowsBluetoothPropertySource.cs:39-63`).
  2. `DEVPKEY_Bluetooth_DeviceFlags` on each device node, decoded into "connected yes/no" and "LE connected yes/no" from bits `0x20` and `0x01000000` (`src/BatteryHub.Probe/BluetoothCommand.cs:112-121`, `src/BatteryHub.Core/BluetoothProperty/BluetoothPropertyConstants.cs:23-30`). I did not check those bit values against the WDK header.
  3. An undocumented property `{83DA6326-97A6-4088-9453-A1923F573B29} 15` that some scripts treat as "is connected" (`BluetoothPropertyConstants.cs:26`).
- **Raw device nodes.** It dumps nodes under BTHENUM, BTHLE, BTHLEDEVICE and BTHHFENUM with name, class, parent, container ID, address and the battery level Windows stores (`BluetoothCommand.cs:12`, `:70-99`). The default filter keeps device nodes, hands-free nodes and HFP audio nodes; `--all` adds every node, including the A2DP (stereo audio) service node (`BluetoothCommand.cs:107-110`).
- **Only reads.** The `bt` path calls CfgMgr32 `CM_Get_Device_ID_List`, `CM_Locate_DevNode` and `CM_Get_DevNode_Property` (`src/BatteryHub.Core/Windows/DeviceNodes.cs`) plus the WinRT paired-device query. There are no pairing calls, no SetupAPI or `DeviceIoControl` calls and no registry access in it. The only registry write in the whole repo is the tray app's "Start with Windows" option (`src/BatteryHub.Core/Windows/StartupRegistration.cs`, `HKCU\...\Run`), which the probe never references.
- **One snapshot per run.** Output goes to the console. The header prints the version and OS but not the time (`src/BatteryHub.Probe/Program.cs`, `PrintHeader`). There is no watch or loop mode and no event subscription.
- **Never run on hardware.** The README marks every reader "untested, awaiting Probe output". The AirPods reader (milestone 5, planned to use BLE advertisements) is "Not started", and the README states AirPods do not report a battery level through the property `bt` reads.
- **Build needs.** The .NET 10 SDK; the target is `net10.0-windows10.0.19041.0` (Windows 10 2004 or later). CI runs on `windows-latest`. I could not see the CI result from this session.

## Inference (untested on your iPhone, Windows laptop, AirPods Pro 3, firmware 9A348)

- AirPods paired through Windows Settings should appear as a classic device with a device node (`BTHENUM\DEV_<address>`) and a hands-free node (`BTHENUM\{0000111E-...}`), so `bt` should list them with a connected state.
- The three signals should usually agree. Where they disagree is itself worth recording.
- Paired device nodes probably stay present while the AirPods are disconnected, so a disconnect should show as the flags changing rather than the node disappearing.
- Repeated runs should not disturb the AirPods: the paired-only queries read Windows' own records and do not scan or page the device. That is the code's stated design and fits how the APIs are documented, but it was not measured.
- **The main limit, from the plan's section 2:** Windows can report "connected" while the sound plays on the iPhone. So `bt` measures "Windows holds a link", not "Windows has the audio". Which device has sound still has to be noted by ear.

## Avoid during Stage 1

- **`ble`**: if the AirPods show up as a connected LE device, it opens the device and does an over-the-air GATT read (`src/BatteryHub.Core/Ble/WinRtBleBatterySource.cs:74`, `:104`). It is a read, not a write, but it is traffic to the AirPods that the measurements do not need.
- **`all`**: runs `ble` plus the HID, Sony and XInput readers.

## How it could be used in Stage 1 (untested)

Build it on another machine with the .NET 10 SDK and copy the output, so nothing is installed on the laptop. A self-contained build such as `dotnet publish src/BatteryHub.Probe -c Release -r win-x64 --self-contained` should produce an exe that needs no .NET runtime. Then, during each trial, run it in a timestamped loop from PowerShell:

```powershell
& { while ($true) { "=== $(Get-Date -Format o)"; .\BatteryHub.Probe.exe bt; Start-Sleep -Seconds 5 } } | Tee-Object -FilePath airpods-bt-log.txt
```

Ctrl+C stops it. A snapshot every 5 seconds gives the Windows-side connected state to line up against your notes on which device had sound.

## What would close the gaps (not done, for later)

- A `bt --watch` mode that prints one timestamped line when any of the three signals changes, using a DeviceWatcher on the classic paired selector. The BLE source already uses that pattern (`WinRtBleBatterySource.cs:175-197`). This would be a small, read-only change to BatteryHub.
- A BLE advertisement watcher for the AirPods' own status broadcasts (Stage 1 action 4). That is BatteryHub milestone 5, which has no code yet.
