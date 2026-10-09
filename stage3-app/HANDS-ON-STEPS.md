# Stage 3 hands-on steps: DualConnect Tray

For the Stage 1 thread to merge into the final checklist. Everything here is untested on the user's laptop, iPhone and AirPods Pro 3 (firmware 9A348).

**Where it fits.** The tray needs nothing from Stage 2: it wraps the Stage 1 tool and changes no Windows setting. It is the "hotkey that reconnects the AirPods" the feasibility plan names as Stage 1's stop option, so it can follow the Stage 1 tool checks whatever the gate says. The checklist owner decides the final position.

**Before you start.** The Stage 1 tool works by hand: `DualConnect.exe status` lists your AirPods, and `take` and `give` move them. If it doesn't, stop here; the tray can't do better than the tool.

**Safety.** Nothing here resets the AirPods or needs pairing. If anything asks you to reset them, stop. If you ever have to pair them with Windows again, double-tap the case once and stop as soon as the light flashes white; a second double-tap while it flashes starts a reset.

## Steps

1. **Copy the folder.** Put `stage3-app` beside `stage1-tools` on the laptop, in the same parent folder.
2. **Build.** Double-click `stage3-app\build.cmd`. No admin rights needed. Expect "Built ...\DualConnectTray.exe". If it says "Build failed", paste the lines above it into the project.
3. **Start it.** Double-click `DualConnectTray.exe`. Expect a new icon in the notification area (it may be behind the ^ arrow) and no error notice. If a notice says the hotkey is taken, choose *Edit settings*, change `ToggleHotkey`, save, then *Reload settings*. If it says more than one device matches, run `stage1-tools\Status.cmd`, copy your pair's containerId into `ContainerId` the same way, and reload.
4. **Check the icon against reality.** With the AirPods playing laptop sound, hover the icon: "AirPods on this laptop" with a green tick. Choose *Give AirPods back*: grey ring, "AirPods not on this laptop".
5. **Switch with the hotkey, 5 times each way.** Press Win+Alt+A to bring the sound to the laptop, then again to give it back. After each press, note whether the sound moved and whether the icon matches. Once, press Win+Alt+A, wait about a second, and press it again while the icon is still blue: the tray should stop that switch and go back. Skip this if the switch finishes too quickly to try.
6. **Let the iPhone take them.** With the laptop playing, start music on the iPhone. Note whether the tray says "AirPods left the laptop". No notice while the iPhone plays means Windows kept its link (the plan's level 2); if so, set `TakeHotkey=Win+Alt+Shift+A` (or another free combination), reload, and use it to bring the sound back.
7. **Optional: start with Windows.** Tick *Start with Windows*, sign out and back in, and check the icon returns. To undo, untick it (this deletes one shortcut from your Startup folder; no registry change).
8. **Use it for a week.** Then open *Switch summary (last 7 days)* and paste the text into the project. It shows switches that worked, median and slowest switch time, and how often the AirPods left or joined the laptop without you asking.

## What counts as success

Adapted from the plan's Stage 3 criteria, read from the summary and your notes:

- Switches succeed every time in steps 5 and 6 (the plan's long target is 50 in a row each way).
- Median switch under 3 seconds.
- A week of daily use with no stuck audio that needs a Bluetooth toggle, a device removal or a restart.
- No iPhone call interrupted by the laptop.

## Undo

Choose *Exit* in the tray menu. Untick *Start with Windows* first if you ticked it. Delete the `stage3-app` folder and `%LOCALAPPDATA%\DualConnectTray`.
