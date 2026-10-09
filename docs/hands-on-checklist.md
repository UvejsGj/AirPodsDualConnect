> **Snapshot.** Copy of the Claude Doc [Your hands-on checklist](https://claude.ai/code/artifact/6098c67b-f0e9-41c0-86b5-50b97285a0a5) (revision 12), exported on 9 Oct 2026. The live doc is the one to tick off; this copy does not update by itself.

# Your hands-on checklist: AirPods Pro 3 on iPhone and Windows

Oct 9, 2026 · @Uvejs Gjelaj

Everything Claude could build ahead of the gates is ready, and none of it has run on your iPhone, your Windows laptop or your AirPods Pro 3 (firmware 9A348) yet. Work from the top down and stop wherever a step says stop. Parts 1 to 5 change no Windows setting, driver or registry value; Part 6 is a decision to make with Claude, not something to run.

&#91;embedded content: checklist order · 6 parts, one gate\]

The gate in Part 3 picks the branch: a pass ends at Stage 1, a miss on the laptop side tries the one-key tool first, and only what that can't fix reaches Stage 2.

## Safety rules for every part

- **Pairing:** double-tap the case once and stop the moment the light flashes white. Never double-tap again while it flashes: on AirPods Pro 3 a repeated double-tap is Apple's reset sequence, which wipes the iPhone setup. If Windows doesn't find them, close the lid and start over.
- **Stop and tell Claude** in the thread if the iPhone asks to set the AirPods up again, they vanish from its list, or the case light flashes amber or faster.
- **Firmware:** note the AirPods version at the start and end of each sitting (iPhone Settings, Bluetooth, the info button beside the AirPods, Version). It should read 9A348; if it changes partway, trials before and after are separate data.
- **If Windows blocks something** (SmartScreen, Smart App Control or a PowerShell "language mode" message), leave those settings as they are and paste the exact message into the thread.
- Nothing here resets the AirPods, updates their firmware or needs admin rights.

## Part 1: Baseline and pairing

Record the versions first, so results from different sittings compare. The table to fill is in the Stage 1 plan's "Before you start" section.

- [ ] Fill the baseline table: Windows version and build (`winver`), Bluetooth adapter and driver, iOS version, AirPods firmware, the "Connect to This iPhone" value, the Windows status text, the AirPods entries in Windows sound output, and other Apple devices or Bluetooth audio nearby.
- [ ] If the AirPods aren't paired with Windows yet: Settings, Bluetooth & devices, Add device, Bluetooth. Open the case near the laptop, double-tap once, stop when the light flashes white, and select the AirPods.
- [ ] On the iPhone, check the AirPods are still listed under the same name and play with one tap. If the iPhone asks to set them up again, stop the whole checklist and tell Claude.

## Part 2: Stock trials

Switch by hand with Windows' and the iPhone's own controls, and write each trial in the plan's Trial log tab. The plan's "The tests" section has the exact steps for every scenario.

- [ ] Run A, with "Connect to This iPhone" on Automatically: scenarios S1 to S5, five trials each (S3 and S5 alternate).
- [ ] Run B, with When Last Connected to This iPhone: the same five scenarios. Set it back afterwards unless run B works better.
- [ ] Microphone check: three trials each of M1 (laptop mic opens during music) and M2 (iPhone call while the laptop mic is open).
- [ ] One ordinary day with the setting you prefer, logging every unwanted grab and every deliberate switch.
- [ ] Tell Claude in the thread once the log is filled; Claude works out the results summary from it.

**Optional logger.** It writes a timestamped CSV of what Windows sees and lets you mark what you hear with one key, so there is less to write by hand. It only reads.

1. Put the `stage1-tools` folder (from this project's files under `dualconnect`, or from your AirPodsDualConnect repo) in `Documents\DualConnect`.
2. Double-click `Build.cmd`. Expect "Built DualConnect.exe and DualConnectW.exe".
3. Open PowerShell or Windows Terminal (not PowerShell ISE) in that folder and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\AirPods-Log.ps1`. The Bypass applies to that window only and changes no setting.
4. Keys: L sound on laptop, P sound on iPhone, N no sound, G unwanted grab, S start of a trial, T note, Q quit. Click the window's title bar to focus it, not inside it.

## Part 3: The Stage 1 gate

Stage 1 passes when run A or run B meets all four of the plan's checks: switch to the iPhone with one action in under 5 seconds in at least 4 of 5 S3 trials, the same for the laptop in S5, no unwanted grabs, and no stuck states. A fixed click path such as Win+A, the Bluetooth arrow, then the AirPods counts as one action, as you decided on 8 Oct.

| What the results show | Where you go |
| --- | --- |
| The iPhone pairing was disturbed at any point | Stop and tell Claude before anything else |
| All four checks pass | Stop at Stage 1 and keep switching by hand. Parts 4 and 5 are optional comfort. |
| Only the laptop side fails: S5 is slow or takes several actions, or the laptop's sound gets stuck | Part 4 |
| The iPhone side fails (S3), or the sound gets grabbed (S1, S2 or the day log) | Part 6, if you still want both connected |

If the numbers pass but daily use still bothers you, or the reverse, your judgement wins, as the plan says.

## Part 4: One-key switching tool

The tool asks Windows' own Bluetooth audio driver to connect or disconnect the AirPods with one key press. It runs in user mode with no admin rights and changes no driver, registry value or setting. It is the plan's "reconnect hotkey" option for stopping at Stage 1.

- [ ] If you skipped the logger: put `stage1-tools` in `Documents\DualConnect` and double-click `Build.cmd`.
- [ ] Double-click `Status.cmd`. Expect your AirPods' playback and microphone entries with their state. If it says two paired devices match, follow its message (`--name`, or `--container` when both have the same name).
- [ ] Open a Command Prompt in that folder (type `cmd` in File Explorer's address bar). With laptop audio playing, run `DualConnect.exe give` and then `DualConnect.exe take`, three times each. Note whether the sound moved and what each line says.
- [ ] The stuck case: with the iPhone playing, as after S3, run `DualConnect.exe take`. It should drop Windows' old link first, then connect.
- [ ] Optional: `powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Shortcuts.ps1` adds two desktop shortcuts, Ctrl+Alt+L for sound to the laptop and Ctrl+Alt+P to give it back. The first press can take about 3 seconds.
- [ ] Re-run S5 five times with the setting you kept, using Ctrl+Alt+L (or `take`) as the one action, and log it as before.
- [ ] Paste `actions.csv` from the folder into the thread.

Stop rule: if `take` fails three times in a row, stop this part and paste those lines into the thread. If S5 now meets the gate, Stage 1 passes with the tool and Part 5 is optional. If it doesn't, go to Part 6.

## Part 5: Tray app and hotkey (optional)

It sits here because the tray only wraps Part 4's tool, needs nothing from Stage 2 and changes no Windows setting: it is the plan's Stage 1 hotkey option made comfortable. The plan's original Stage 3, an app on top of a two-link Stage 2 prototype, would need a Stage 2 yes and isn't built. Do this part only when Part 4 works by hand (`status` lists your AirPods; `take` and `give` move them).

- [ ] Copy `stage3-app` beside `stage1-tools`, in the same parent folder.
- [ ] Double-click `stage3-app\build.cmd`. Expect "Built ...\\DualConnectTray.exe". If it says "Build failed", paste the lines above it into the thread.
- [ ] Start `DualConnectTray.exe`. A new icon appears in the notification area, possibly behind the ^ arrow. If a notice says the hotkey is taken: Edit settings, change `ToggleHotkey`, save, then Reload settings.
- [ ] With laptop sound in the AirPods, hover the icon: "AirPods on this laptop" with a green tick. Choose Give AirPods back: grey ring, "AirPods not on this laptop".
- [ ] Press Win+Alt+A five times each way. Note whether the sound moved and whether the icon matches.
- [ ] With the laptop playing, start music on the iPhone and note whether the tray says "AirPods left the laptop". No notice means Windows kept its link: set `TakeHotkey=Win+Alt+Shift+A` (or another free combination), reload, and use it to bring the sound back.
- [ ] Optional: tick Start with Windows, sign out and back in, and check the icon returns. Unticking deletes one shortcut from your Startup folder; there is no registry change.
- [ ] After a week, open Switch summary (last 7 days) and paste the text into the thread.

Success means every switch in the hotkey and iPhone steps works, the median switch is under 3 seconds, a week passes with no stuck audio that needs a Bluetooth toggle, device removal or restart, and no iPhone call is interrupted by the laptop.

## Part 6: Stage 2, only if the gate sends you here

There is nothing to run yet. The Stage 2 prototype isn't built: an automatic safety check stopped that build before any code was written, and it won't be retried.

- [ ] Read the Stage 2 research doc (link below) and decide with Claude in the thread which route, if any, to try. It recommends a spare Realtek Bluetooth dongle with the Bumble Bluetooth stack on Windows, keeps route 2A (the plan's registry identity test) optional, rules out 2C, and keeps a Linux live USB as the fallback.
- [ ] Before any step that touches the registry or a driver, Claude explains exactly what it changes and how to undo it, and you decide then.

## Undo everything

- **Shortcuts:** in the `stage1-tools` folder, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Shortcuts.ps1 -Remove`.
- **Tools:** delete the `stage1-tools` folder, which also holds `actions.csv` and the logger's CSV files.
- **Tray:** untick Start with Windows, choose Exit, then delete `stage3-app` and `%LOCALAPPDATA%\DualConnectTray`.
- **iPhone:** set Connect to This iPhone back to the value you recorded in Part 1.
- **Laptop pairing, only if you want it gone:** Windows Settings, Bluetooth & devices, the AirPods, Remove device. This should remove only the laptop's pairing and leave the iPhone's alone (not tested on your setup).

## Links

- [Stage 1 measurement plan and Trial log](https://claude.ai/code/artifact/25077251-a3d3-4030-93d8-7260ab4ae7b5)
- [Stage 2 research](https://claude.ai/code/artifact/cc4861b2-f70b-4532-87b3-79ceaf80a43e)
- [Apple: How to reset your AirPods](https://support.apple.com/HT209463), only to know what the repeated double-tap does; don't follow it.
- The tools, with their README files: this project's files under `dualconnect/stage1-tools` and `dualconnect/stage3-app`, also in your AirPodsDualConnect repo on GitHub.
