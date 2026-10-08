> **Snapshot.** Copy of the Claude Doc [Stage 1 Measurement Plan](https://claude.ai/code/artifact/25077251-a3d3-4030-93d8-7260ab4ae7b5) (revision 18), exported on 8 Oct 2026. The live doc is the one to edit; this copy does not update by itself.

# Stage 1 Measurement Plan: AirPods Pro 3 on iPhone + Windows

Oct 8, 2026 · @Uvejs Gjelaj

## Purpose and ground rules

Stage 1 answers one question with counts: is switching the AirPods by hand between your iPhone and laptop quick and predictable enough that no new software is needed? If the gate at the end says yes, the project stops here.

It measures four things on your stock setup:

- Whether Windows stays connected while the iPhone plays, and the reverse.
- What happens on each device when the other one takes the audio.
- How many seconds and actions a manual switch takes in each direction.
- How audio quality and the microphone profile change, mostly on the Windows side.

Ground rules for every test:

- No system changes: no registry edits, drivers, Test Mode, Secure Boot changes or extra Bluetooth software.
- Never reset the AirPods or try to change their firmware. If a screen or guide asks for a reset, stop and write down what happened.
- The only setting this stage changes is the iPhone's "Connect to This iPhone" option, for run B. To undo it, set it back to the value you record in the baseline.
- Use the laptop's built-in Bluetooth adapter only. The spare dongle belongs to Stage 2 and stays unplugged.
- Keep important calls away from the test sessions.

Budget about 4 hours of trials over two sittings, plus one ordinary working day with a short log open. The feasibility plan estimated 3 to 5 hours for Stages 0 and 1.

## What we know, what we expect, what is untested

Nothing below has been run on your iPhone, laptop and AirPods Pro 3 (firmware 9A348). Stage 1 exists to replace the expectations with your own counts.

| Claim | Status | Basis |
| --- | --- | --- |
| AirPods Pro 3 work as a normal Bluetooth headset with a non-Apple device. Pairing mode is a double-tap on the front of the open case until the light flashes white. | Verified in Apple's docs, checked today | [Pair AirPods with a non-Apple device](https://support.apple.com/guide/airpods/pair-airpods-with-a-non-apple-device-dev499c9718b/web) |
| The iPhone setting is Settings, your AirPods, Audio & Routing (if shown), Connect to This iPhone, with options Automatically and When Last Connected to This iPhone. | Verified in Apple's docs, checked today | [Switch your AirPods to another device](https://support.apple.com/HT212204) |
| Manual switching on the iPhone is the AirPlay button on Now Playing, the Lock Screen or Control Center. | Verified in Apple's docs, checked today | [Switch your AirPods to another device](https://support.apple.com/HT212204) |
| Apple documents automatic switching only between Apple devices. It says nothing about Windows. | Verified in Apple's docs, checked today | [Switch your AirPods to another device](https://support.apple.com/HT212204) |
| Firmware updates install on their own while charging near the iPhone, with no way to decline or roll back. | Verified by the feasibility research on 8 Oct 2026, not re-checked | [About firmware updates for AirPods](https://support.apple.com/en-us/106340) |
| One developer saw the AirPods list an iPhone and a Windows PC as hosts at once, with sound moving to the phone while Windows still looked healthy. | Verified by the feasibility research as that developer's own log, not re-checked; AirPods model not stated | [NTPods commit 4f9e3c7](https://github.com/arctumn/ntpods/commit/4f9e3c7acf6c7a040403c87ad5e0a5e3a3ff2d7e) |
| The same developer also saw the opposite: Windows lost the link when the iPhone took the AirPods, and toggling Windows audio services was the only reliable way to get sound back. | Verified by the feasibility research as that developer's own log, not re-checked | [NTPods commit 1306b3b](https://github.com/arctumn/ntpods/commit/1306b3be35577df4c505c5a3062caebb16b0b650) |
| The iPhone takes the AirPods when it starts playing, and often on notifications. | Inference | Feasibility plan, Stage 1 expected results |
| Windows either drops to "Paired" or stays "Connected" with no sound. | Inference | Feasibility plan, plus the two NTPods logs above |
| "When Last Connected to This iPhone" reduces unwanted grabs by the iPhone. | Inference | Apple presents it as turning automatic switching off; its effect against a Windows host is unknown |
| While a Windows app uses the AirPods microphone, Windows moves them to the hands-free profile and music sounds mono and duller until the mic closes. | Verified for Bluetooth headsets in general; untested with AirPods on your laptop | [Logitech support](https://support.logi.com/hc/en-ca/articles/34961953160599-Why-does-my-headset-appear-as-more-than-one-device-on-Windows): the hands-free profile is "mono instead of stereo", and Windows 11 should show one device and switch profiles itself |
| The Windows menu paths and status words quoted in this plan ("Connected voice, music", "Paired") | Untested on your build | Windows 11 wording from general knowledge; yours may differ slightly |

If your Windows build words a status differently, write down what it actually says. The exact text is part of the result.

## Before you start

Record the baseline first, because results only compare across sittings when the versions are known. Check the firmware again at the end of each sitting: if it changed, the trials before and after are separate data sets.

| Item | Where to look | Your value |
| --- | --- | --- |
| Windows edition, version and build | Run `winver` |  |
| Bluetooth adapter model and driver version | Device Manager, Bluetooth |  |
| iOS version | iPhone Settings, General, About |  |
| AirPods firmware at start of sitting | iPhone Settings, Bluetooth, info button beside the AirPods, Version |  |
| AirPods firmware at end of sitting | Same place |  |
| "Connect to This iPhone" value today | iPhone Settings, your AirPods, Audio & Routing |  |
| Windows status text for the AirPods when idle and connected | Windows Settings, Bluetooth & devices |  |
| AirPods entries in Windows sound output (one, or a separate Stereo and Hands-Free) | Windows Settings, System, Sound |  |
| Other Apple devices on your Apple Account within range (Mac, iPad, Watch) | Your own list |  |
| Other Bluetooth audio the iPhone or laptop may auto-connect to (car, speaker) | Bluetooth lists on both |  |

Secure Boot, BitLocker and Memory Integrity matter only for Stage 2C, so they can wait.

**Pair with Windows, if not already paired.** This adds a second pairing and leaves the iPhone's alone.

1. On the laptop, open Settings, Bluetooth & devices, Add device, Bluetooth.
2. Open the case with both AirPods inside, hold it near the laptop and double-tap the front of the case. Double-tap once only, and stop the moment the light flashes white. **Never double-tap again while it flashes:** on AirPods Pro 3, repeating the double-tap is Apple's reset sequence ([How to reset your AirPods](https://support.apple.com/HT209463)), which wipes the iPhone setup. If Windows doesn't find them, close the lid and start over rather than tapping again. If the light flashes faster or amber, stop and write down what happened.
3. Select the AirPods in the Windows list.
4. On the iPhone, check the AirPods are still listed under the same name and play from the iPhone with one tap. If the iPhone asks you to set them up again, or they are gone, stop the whole stage and write down what you saw.

**During every trial**

- Wear both AirPods, so ear detection does not pause anything.
- Keep other Apple devices on your Apple Account out of range or with Bluetooth off, so automatic switching between Apple devices cannot interfere. Note it if you can't.
- Use a stopwatch outside the two devices under test, such as a watch or a kitchen timer.
- Read the status lists without tapping or clicking the AirPods entry, because a tap can start a connection.
- For calls (scenario S4) you need someone who can ring your iPhone. Without a caller, skip S4 and mark it not run.

## The tests

You run the same five scenarios twice, five trials each: run A with "Connect to This iPhone" on Automatically, run B with When Last Connected to This iPhone. Then a short microphone check, and one ordinary day with a log. Results go in the Trial log tab.

**Switching by hand.** Use the same method in every trial so the times compare.

- To the iPhone: the AirPlay button in Control Center, Now Playing or the Lock Screen, then the AirPods. S3 first tests whether pressing play alone is enough.
- To the laptop: Win+A, the arrow beside Bluetooth, then the AirPods. Settings, Bluetooth & devices, Connect also works. This is the Windows 11 path and untested on your build.
- Count every tap, click or key press as one action, including opening a menu.
- Turn the laptop's own speakers down, so audio that falls back to them between switches stays quiet. Windows keeps a separate volume per output.

**The five scenarios**

1. **S1. Laptop plays, iPhone idle.** Play audio on the laptop through the AirPods and leave the iPhone locked for 5 minutes. At 5 minutes, record where the sound is and both statuses. If the sound left the laptop, time getting it back. One 25-minute stretch with a check every 5 minutes counts as five trials.
2. **S2. Laptop plays, iPhone notification.** With laptop audio playing, trigger an iPhone notification, such as a Reminder set one minute ahead. Record whether the sound moved to the iPhone, whether it came back by itself, and both statuses one minute later. If it stayed away, time getting it back.
3. **S3. Laptop plays, you start music on the iPhone (switch to iPhone).** Press play in an iPhone music app and start the stopwatch. Stop it when you hear the iPhone. If nothing happens within 10 seconds, use the AirPlay button and count those taps too. Let the iPhone play for 5 minutes, then record both statuses. Windows still showing Connected at 5 minutes is the sign that the AirPods held both links.
4. **S4. Laptop plays, a call arrives on the iPhone.** Have someone ring you. Record where the ringtone plays, answer, talk for 30 seconds and hang up. Record whether laptop audio returns by itself; if not, time getting it back.
5. **S5. iPhone plays, you connect from Windows (switch to laptop).** Run it straight after S3, with the iPhone still playing and laptop audio still running. Start the stopwatch, connect from Windows, and stop it when you hear the laptop in the AirPods. Note what the iPhone does. If Windows still shows Connected while the AirPods stay silent, there is no Connect button: pause the iPhone first, then disconnect and reconnect in Windows if needed, and count every step. Let the laptop play for 5 minutes, then record both statuses. The iPhone still showing Connected at 5 minutes means both links held in this direction.

S3 ends with the iPhone playing and S5 ends with the laptop playing, so they alternate: S3, S5, S3, S5 and so on.

**Run B.** On the iPhone, open Settings, your AirPods, Audio & Routing, Connect to This iPhone, and choose When Last Connected to This iPhone. Repeat S1 to S5. Afterwards, set it back to the baseline value unless run B works better and you want to keep it.

**Microphone check.** Three trials of each, with the setting you plan to keep.

- **M1. Laptop mic opens during music.** Play music on the laptop, then open the mic: Windows Settings, System, Sound, Test your microphone, or start a recording in Sound Recorder. Record whether music turns mono or duller, the Windows status text while the mic is open, and how many seconds music takes to sound normal after the mic closes.
- **M2. iPhone call while the laptop mic is open.** Keep a laptop recording or test call running and have someone ring the iPhone. Record where the call goes, what happens to the laptop audio, and the seconds and actions to get the laptop back afterwards.

**One ordinary day.** With the setting you prefer, log every time the sound goes somewhere you did not want, and every deliberate switch, in the day log.

Left out on purpose: the feasibility plan's optional Bluetooth LE scan and Microsoft's Bluetooth Virtual Sniffer. Both need extra software, I have not checked whether the sniffer installs a driver, and neither changes the gate. Its suggested questions to NTPods' maintainer need no devices and can go out any time.

## Results summary

Every trial goes in the Trial log tab, and this table condenses it for the gate. Tell me in the thread once the log is filled and I'll work the summary out from it.

| Measure | Run A | Run B | Gate needs |
| --- | --- | --- | --- |
| S1: sound left the laptop with the iPhone idle (of 5) |  |  | 0 |
| S2: sound left the laptop on a notification (of 5) |  |  | 0 |
| S3: switches to iPhone under 5 s with one action (of 5) |  |  | 4 or more |
| S5: switches to laptop under 5 s with one action (of 5) |  |  | 4 or more |
| Median switch time to iPhone / to laptop (s) |  |  | Under 5 each |
| Stuck states across all trials (count) |  |  | 0 |
| S3: Windows still Connected at 5 minutes (of 5) |  |  | Shapes Stage 2 only |
| S5: iPhone still Connected at 5 minutes (of 5) |  |  | Shapes Stage 2 only |
| S4: laptop audio returned by itself after the call (of 5) |  |  | Information |

Ordinary day (setting used: ): unwanted grabs , deliberate switches . The gate needs zero unwanted grabs.

Microphone: music turned mono or duller in  of 3 M1 trials; laptop audio recovered by itself after the iPhone call in  of 3 M2 trials.

## Gate: stop here or go to Stage 2

Stage 1 passes, and the project stops with manual switching, when run A or run B meets all four checks below. The thresholds are the feasibility plan's Stage 1 success criteria. If the numbers pass but daily use still bothers you, or the reverse, your judgement wins.

1. **Switch to iPhone:** one action and under 5 seconds in at least 4 of 5 S3 trials.
2. **Switch to laptop:** one action and under 5 seconds in at least 4 of 5 S5 trials.
3. **No unwanted grabs:** the sound never left the laptop in S1 or S2, and the ordinary day logs none.
4. **No stuck states:** after every switch to the laptop, its sound came back in stereo without a Bluetooth toggle, device removal or restart.

One action means pressing play, or one fixed menu path that works every time, such as Win+A, the Bluetooth arrow, then the AirPods. You chose on 8 Oct 2026 to count a fixed path as one action, because a reconnect hotkey could replace it, as the feasibility plan suggests if you stop here. That hotkey is untested on your setup and would only come after the gate.

&#91;embedded content: Stage 1 gate · 3 checks, 4 outcomes\]

Read it top down: every path ends in a stop except a yes at the last check, which leads to Stage 2.

Two results shape what comes next without deciding the gate. The S3 count of Windows still Connected at 5 minutes sets the starting point for Stage 2: 3 or more of 5 means the AirPods already hold both links on stock settings, and 0 means they drop Windows whenever the iPhone plays. Microphone results count toward the gate only through stuck states, since the hands-free limits apply whichever device holds the AirPods (inference).

If the AirPods firmware changes partway through, treat trials before and after as separate data and repeat the run that straddled the update. Stage 2A changes a registry value, so before you make it I'll explain exactly what it changes and how to undo it.
