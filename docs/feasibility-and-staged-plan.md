# AirPods Pro 3 on iPhone + Windows: feasibility and staged plan

Oct 8, 2026 · @Uvejs Gjelaj

## 1. Verdict

Keeping AirPods Pro 3 actively connected to an iPhone and a Windows laptop, with audio from one at a time, is plausible but unproven. No existing project delivers it on Windows today, and the deciding factor is AirPods firmware, which you do not control.

| Level | Status today | Confidence (my judgement) | Main basis |
| --- | --- | --- | --- |
| 1. Both devices remember the pairing | Works with no extra software | About 99% | [Apple's non-Apple pairing guide](https://support.apple.com/guide/airpods/pair-airpods-with-a-non-apple-device-dev499c9718b/web) |
| 2. Both hold a live Bluetooth link at once | Observed with an iPhone and a Windows PC, but intermittent and not controllable | About 70% that it occurs on your setup; about 45% that it can be held reliably on firmware 9A348 | [NTPods](https://github.com/arctumn/ntpods) commit logs |
| 3. Audio moves between the two live links on demand | Implemented for Android and Linux with Apple Vendor ID spoofing; not implemented on Windows by anyone I found | About 40% for reliable daily use; about 60% for use with occasional manual recovery | [LibrePods](https://github.com/librepods-org/librepods) source, [issue #782](https://github.com/librepods-org/librepods/issues/782) |
| 4. Audio from both devices mixed | Not supported by the AirPods | About 90% that it cannot be done through the AirPods | The protocol tracks one audio owner |

Level 3 is the goal you described. Reaching it on Windows needs Apple's proprietary protocol, a way to open its channel that Windows does not offer to normal apps, and firmware that keeps cooperating.

The fallback already works: level 1 plus a fast manual reconnect. Stage 1 below measures whether that is good enough before any system change.

I ran nothing on hardware. Everything here comes from source code, commit logs and vendor documentation read on 8 October 2026.

## 2. The four levels, and how to tell them apart

The four levels are separate technical states, and most confusion comes from treating level 1 as level 2.

| Level | What it means technically | How you would observe it |
| --- | --- | --- |
| 1. Paired on both | Each device stores a link key for the AirPods. Only one needs a radio link at a time. | AirPods appear in both Bluetooth lists. Windows shows "Paired" while the iPhone shows "Connected", or the reverse. |
| 2. Connected to both | The AirPods hold two Bluetooth Classic links at once. Audio profiles may be open on one or both. | Both devices show "Connected" at the same moment for minutes. The AirPods' own host list names two addresses. |
| 3. Switching between live links | The AirPods keep both links and move the audio stream when one host asks for it or starts playing. | Sound moves in a second or two and neither device drops to "Paired". LibrePods reports the iPhone showing its usual handoff banner. |
| 4. Mixing | Two audio streams decoded and played together. | A PC sound and an iPhone sound audible at the same instant. |

Windows cannot see level 2 from its own side. During a handoff its endpoint, its Bluetooth device and its link can all report healthy while the sound plays on the phone, as [NTPods' developer measured](https://github.com/arctumn/ntpods/commit/4f9e3c7acf6c7a040403c87ad5e0a5e3a3ff2d7e).

Level 4 is not something to build toward here. If you ever want both sounds at once, the realistic design is different: the iPhone streams to the PC and the PC mixes, then sends one stream to the AirPods.

## 3. Evidence

The strongest evidence is one developer's logs of an iPhone and a Windows PC sharing AirPods, and it shows sharing happening without showing it under control.

### Verified in primary sources

**Apple**

- Automatic switching is documented only between Apple devices signed in to the same Apple Account. The [switching article](https://support.apple.com/HT212204) (published 7 October 2026) says nothing about non-Apple devices.
- AirPods work as "a Bluetooth headset with a non-Apple device". AirPods Pro 3 enter pairing mode by double-tapping the front of the open case until the light flashes white ([guide](https://support.apple.com/guide/airpods/pair-airpods-with-a-non-apple-device-dev499c9718b/web)).
- The current AirPods Pro 3 firmware is 9A348. Updates install automatically while charging near an iPhone, and the [firmware page](https://support.apple.com/en-us/106340) (17 September 2026) describes no way to decline or roll back.

**LibrePods (Android and Linux)**

- The [README](https://github.com/librepods-org/librepods) marks multi-device connectivity as "Needs VendorID spoofing; use at your own risk" on both platforms. It has no Windows column and sends Windows users to MagicPods.
- The Android code does implement handoff. [AACPManager.kt](https://github.com/librepods-org/librepods/blob/main/android/app/src/main/java/me/kavishdevar/librepods/bluetooth/AACPManager.kt) defines control command `0x06` "Owns connection", opcode `0x0E` audio source, `0x2E` connected devices, and `0x10` smart-routing messages that carry a "Hijackv2" request to the other host.
- [AirPodsService.kt](https://github.com/librepods-org/librepods/blob/main/android/app/src/main/java/me/kavishdevar/librepods/services/AirPodsService.kt) refuses to take over when the vendor hook is off, logging "not taking over, vendorid is probably not set to apple". When ownership is lost it disconnects local A2DP and keeps the link.
- The Linux app on the main branch contains no ownership or hijack code. That logic exists only in the Rust rewrite on the [linux/rust branch](https://github.com/librepods-org/librepods/tree/linux/rust), last updated 15 May 2026.
- The [Linux README](https://github.com/librepods-org/librepods/blob/main/linux/README.md) warns that with the Apple Device ID set, AirPods "may disconnect after a short period" because not everything an Apple device does is implemented. It advises changing the Device ID back afterwards, and notes a re-pair may be needed because the AirPods cache it.
- [Issue #782](https://github.com/librepods-org/librepods/issues/782) reports AirPods Pro 3 on firmware 9A348 dropping the link 36 to 40 seconds after each protocol session when "Act as an Apple device" is on. Turning it off stops the drops and loses multipoint. A second user reports the same on AirPods Pro 2. I saw no maintainer reply.
- The maintainer's own statements are thin on iPhone testing. In [PR #202](https://github.com/librepods-org/librepods/pull/202) he notes random disconnects and a quirk, "connect your apple device before connecting your non-apple device". In [discussion #400](https://github.com/librepods-org/librepods/discussions/400) he says two Android phones both need the app and the Apple setting.

**Windows**

- Microsoft documents `BTHPROTO_RFCOMM` as the supported protocol for [Bluetooth sockets](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-socket). Apple's protocol runs on L2CAP PSM `0x1001`, and [NTPods' driver README](https://github.com/arctumn/ntpods/blob/main/windows/drivers/aap/README.md) records a user-mode L2CAP connect failing with `WSAENETDOWN`.
- Windows does expose its Device ID record. Microsoft documents `DIDVendorIDSource`, `DIDVendorID`, `DIDProductID` and `DIDVersion` under `BTHPORT\Parameters`, default vendor `0x06` Microsoft ([Bluetooth host radio support](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-host-radio-support)). Vendor ID spoofing on Windows is therefore a registry change, not a driver.
- Test-signed drivers need `TESTSIGNING` on. Microsoft's [page](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option) says to disable Secure Boot if the setting is refused, warns BitLocker may interfere, and notes the "Test Mode" watermark.

**NTPods (Windows)**

- [NTPods](https://github.com/arctumn/ntpods) ships a kernel driver that opens the protocol channel, a Rust daemon and a WinUI app. Its [Windows README](https://github.com/arctumn/ntpods/blob/main/windows/README.md) requires Test Mode with Secure Boot off, and states heart rate was verified on AirPods Pro 3.
- Its [commit of 31 August 2026](https://github.com/arctumn/ntpods/commit/4f9e3c7acf6c7a040403c87ad5e0a5e3a3ff2d7e) includes a captured `0x2E` packet listing two hosts, a phone and the PC. It records the audio moving to the phone during a Teams call while the Windows endpoint, link and protocol channel all stayed healthy.
- A [companion commit](https://github.com/arctumn/ntpods/commit/1306b3be35577df4c505c5a3062caebb16b0b650) records the opposite case too: the Windows link dropping when an iPhone took the AirPods. It finds that toggling the Windows audio services is the only reliable way to get sound back, and that the call fails with error 87 while the AirPods are away.
- NTPods sends no ownership or hijack messages. It detects the loss, shows a "Shared with" list, and rebuilds the audio route.
- The developer set three Apple Device ID records through the registry, including an iPhone's exact `004C:7805:1A50`, rebooted each time, and saw no change for heart rate ([commit](https://github.com/arctumn/librepods/commit/7b6bcd1)).

### Inference

- Multipoint is decided by the AirPods. Windows needs no multipoint feature of its own, only a link, an audio profile and a way to speak Apple's protocol.
- The two-host capture shows the firmware will hold an iPhone and a Windows PC together at least sometimes. Whether an Apple Device ID was still set in that PC's registry is not recorded, so the capture does not settle whether spoofing is required.
- The 9A348 drops probably affect any host that claims an Apple vendor ID without completing newer session setup. The report comes from Android only.
- Mixing is not available. The protocol reports a single audio source with one address and a type of none, call or media.

### Not verified

- **The exact combination.** I found no report of switching working with an iPhone, Windows and AirPods Pro 3 together. NTPods' developer is the closest, and the AirPods model in the 31 August captures is not stated.
- **Spoofing and multipoint on Windows.** Nobody has documented whether the registry Device ID changes multipoint behaviour.
- **iPhone cooperation.** Whether an iPhone on current iOS honours a takeover request from a spoofed Windows host is untested.
- **Independent confirmation.** NTPods' measurements are one developer's self-reports. Its issue tracker shows no open issues and nothing on this topic.
- **Issue coverage.** GitHub's issue search was blocked to my tools. I read #700, #782, PR #202 and discussion #400 individually, so other relevant reports may exist.

## 4. What the approach depends on

The LibrePods-style approach depends on all five things you listed, and firmware is the one with no workaround.

| Dependency | Required? | Why | Status |
| --- | --- | --- | --- |
| AirPods firmware behaviour | Yes, entirely | The AirPods decide whether to keep two links and whose audio plays. A host can only ask. | Firmware 9A348 is reported to drop Apple-vendor hosts after about 40 seconds on Android ([#782](https://github.com/librepods-org/librepods/issues/782)). No downgrade path. |
| Apple Vendor ID spoofing | Yes according to LibrePods; unconfirmed on Windows | LibrePods' docs and code gate takeover on the host's Device ID vendor being Apple (`0x004C`). | Possible on Windows through documented registry values. Effect on multipoint untested. |
| Apple's proprietary protocol (AAP) | Yes for controlled switching | Ownership, the host list and takeover requests travel only on this channel. | Reverse-engineered and documented by LibrePods and [apple-wireshark](https://github.com/pabloaul/apple-wireshark). No Apple documentation. |
| Windows Bluetooth limitations | Yes, they shape the design | Normal apps cannot open the protocol's L2CAP channel. Windows cannot see multipoint. The audio route does not rebuild itself after a handoff. | Each has a known workaround in NTPods except takeover itself. |
| A custom driver | Yes on the built-in Windows Bluetooth stack | Only a kernel profile driver can open L2CAP PSM `0x1001` there. | NTPods' driver is test-signed, so it needs Test Mode and Secure Boot off. |

There is one way to avoid a kernel driver of your own: a user-mode Bluetooth stack that owns a dedicated USB dongle through Microsoft's WinUSB driver. [airlow](https://github.com/jkryspin/airlow) shows this can carry both audio and Apple's protocol to AirPods Pro 2. It costs a spare radio and means reimplementing the audio path.

## 5. Existing projects, judged by their code

None of the Windows projects implements switching, and two of the ports that advertise multi-device support show no sign of working on Windows at all.

| Project | What it is | Last activity | What the source shows | Use for this goal |
| --- | --- | --- | --- | --- |
| [LibrePods](https://github.com/librepods-org/librepods) | Android and Linux apps, the protocol reference | 6 October 2026, tag v1.0.1-rc1 | Complete handoff logic on Android; a Rust port on a side branch; nothing for Windows on main | Reference for the switching logic |
| [NTPods](https://github.com/arctumn/ntpods) | Windows driver, daemon and app; formerly the LibrePods Windows port | 1 October 2026, version 0.1.12 nightly | Working protocol channel; reads the AirPods' host list; recovers audio after a phone takes it; no takeover | Best base and observation tool |
| [airlow](https://github.com/jkryspin/airlow) | User-mode Bluetooth stack over WinUSB for low-latency audio | 1 October 2026, all 10 commits that day | Audio and Apple's protocol to AirPods Pro 2 on one MediaTek controller; no multipoint | Proof that a driverless design is possible |
| [MagicPods](https://github.com/steam3d/MagicPods-Windows) | Paid closed-source Windows app with its own driver | README updated 22 September 2026 | "Enhanced Multipoint Connection" is listed only for counterfeit Airoha AirPods; the [driver](https://magicpods.app/magicaap/) needs Test Mode | None for genuine AirPods multipoint |
| [Tblob18/librepods-windows](https://github.com/Tblob18/librepods-windows) | Port of the Linux Qt app | 21 November 2025, all Windows commits that day, mostly by an automated coding agent | Opens the channel with a Qt L2CAP socket; "handoff" is a socket to a phone, not AirPods multipoint; no hardware test recorded | Not usable as evidence |
| [brianpht/librepods-rs](https://github.com/brianpht/librepods-rs) | Rust rewrite with a Windows crate | 13 September 2026 | Windows crate added in one commit, using a Winsock L2CAP socket; its own audit lists the ownership command as missing | Not usable as evidence |
| [WinPods](https://github.com/changcheng967/WinPods) | C# app with driver source | 5 March 2026 | README says the driver "has not yet been verified on real hardware" | None |

The Tblob18 and brianpht ports both rely on user-mode L2CAP. That conflicts with Microsoft's socket documentation and with NTPods' recorded failure, so I would expect neither to connect on a stock Windows stack. I did not run them.

Upstream LibrePods has a `windows/hearing-aid` branch from late 2025. It holds Python scripts that use Google's Bumble stack over WinUSB, not a Windows app.

## 6. Most promising approach

The most promising route combines two projects: NTPods for reaching the AirPods from Windows, and LibrePods' handoff logic for deciding who owns the audio. Each half exists and works on its own platform; nobody has joined them.

**What the evidence supports**

- **The channel works on Windows.** NTPods' driver binds to the service every AirPod advertises and opens PSM `0x1001`. Its README calls it "working and in daily use", and heart rate on AirPods Pro 3 needed a specific fix to that channel ([commit](https://github.com/arctumn/ntpods/commit/65e7acd)).
- **The AirPods tell a Windows host who else is connected.** NTPods parses the `0x2E` host list and saw a phone appear 3.2 seconds before Windows noticed any change.
- **Windows can release and reclaim the audio profile.** NTPods does this with `BluetoothSetServiceState`, serialised and only while the device is connected.
- **The handoff messages are known.** LibrePods sends "Owns connection", media information and a hijack request, then reconnects A2DP. The same sequence exists in Kotlin and in Rust.

**What the evidence does not support**

- That the handoff sequence is accepted from a Windows host. Nobody has sent it from Windows.
- That it still works on firmware 9A348, given the drops in issue #782.
- That NTPods is stable beyond its author's machine. It is about eight weeks old, has 15 stars, and ships nightly builds only.

**The alternative worth keeping in view** is a user-mode stack on a dedicated dongle, as airlow does. It avoids Test Mode completely, and it gives full control of the Device ID record and channel setup. It is more code, because the audio path must be reimplemented, and the dongle stops being a normal Windows Bluetooth radio.

## 7. Staged plan

The plan has three stages and two gates, and it is built to stop early: nothing touches a driver, a boot setting or the AirPods' pairing until cheaper tests say it is worth it.

&#91;embedded content: staged plan · 3 stages, 2 gates\]

Read it left to right. Either gate can end the project with a working fallback, and only a yes at the second gate leads to driver or radio work.

### Stage 0: record the baseline (30 minutes, no changes)

Write down the facts in section 11 before testing anything. Later results are only comparable if the Windows build, iOS version and AirPods firmware are known.

### Stage 1: lowest-risk checks with existing tools

No driver, no Test Mode, no registry edit, no AirPods reset.

**Prerequisites**

- AirPods Pro 3 set up on the iPhone as usual.
- AirPods paired to Windows through Settings. Pairing mode adds a second pairing and leaves the iPhone's alone. Stop as soon as the light flashes white.
- A notebook or spreadsheet for results.

**Actions**

1. Run a behaviour matrix, five trials each: PC playing while the iPhone sits idle; PC playing when the iPhone gets a notification; PC playing when you start music on the iPhone; PC playing when a call arrives; iPhone playing when you click Connect in Windows.
2. For each trial record the status text on both devices, which one has sound, and how many seconds and clicks it takes to get sound back.
3. On the iPhone, change the AirPods setting "Connect to This iPhone" to "When Last Connected to This iPhone" and repeat the matrix. Apple documents this [setting](https://support.apple.com/HT212204).
4. Watch the AirPods' Bluetooth LE advertisements with a user-mode scanner, including your own tooling. They carry a connection state and need no driver.
5. Optional: capture Bluetooth traffic with Microsoft's Bluetooth Virtual Sniffer, the tool NTPods' developer used. It shows link drops and their reason codes. I did not verify its current download or requirements.
6. Ask NTPods' maintainer three questions in a GitHub issue: which AirPods model and firmware the 31 August captures used, whether an Apple Device ID was set in the registry then, and what happens on 9A348.

**Expected results (inference)**

- The iPhone takes the AirPods on playback and often on notifications.
- Windows either drops to "Paired" or stays "Connected" with no sound.
- The iPhone setting reduces unwanted grabs.

**Success criteria**

- You can state, with counts, how often Windows stays connected while the iPhone has the audio. Three or more of five trials lasting five minutes means level 2 occurs naturally on your setup.
- You know whether manual switching meets your need: one action and under five seconds each way, with no unwanted grabs across a normal day.

**Stop conditions**

- Manual switching is good enough. Stop here, perhaps adding a hotkey that reconnects the AirPods.
- Pairing with Windows disturbs the iPhone pairing. Not expected; stop and investigate before anything else.

### Stage 2: proof of concept

Stage 2 answers three yes-or-no questions on your devices. Does the firmware keep both links for ten minutes? Does a takeover request move the audio? Does the link survive the 40-second drop reported on 9A348?

The three steps run from least to most invasive. Each one is explained before it is suggested.

**2A. Registry identity test (about one hour)**

- **What it is.** Setting `DIDVendorID` to `0x4C` under `BTHPORT\Parameters`, the documented way to change the vendor Windows announces.
- **Why it is needed.** It is the only driverless test of whether the AirPods treat an Apple-vendor Windows host differently.
- **What it changes.** A system-wide identity value. Every Bluetooth device will see the PC as an Apple-vendor host. It needs administrator rights and a reboot, and the AirPods may need removing and re-pairing in Windows because they cache the value. Re-pairing in Windows is not an AirPods reset.
- **Prerequisite.** Export the registry key first. The values may be absent, which means defaults.
- **Action.** Repeat the Stage 1 matrix.
- **Expected result.** Possibly no visible change. NTPods' developer saw none for heart rate.
- **Success.** Windows stays connected alongside the iPhone clearly more often than in Stage 1.
- **Stop.** The AirPods start disconnecting on a cycle. Delete the values, reboot, and record it.

**2B. Full handoff test away from the Windows stack (one or two weekends)**

- **What it is.** Running LibrePods' existing handoff code against your iPhone and AirPods from a Linux live USB with a spare Bluetooth dongle. The Device ID is set in BlueZ's `main.conf`.
- **Why it is needed.** It tests the firmware question with code that already exists, and it changes nothing in Windows.
- **What it changes.** Nothing persistent on the laptop. Use the spare dongle: pairing from Linux through the built-in adapter would overwrite the link key Windows uses, forcing a re-pair in Windows.
- **Prerequisites.** A USB stick, a dongle of about 10 euros that Linux supports, and a build of the `linux/rust` branch.
- **Action.** Connect the iPhone first, then Linux. Log the host list, audio source and ownership messages. Trigger 20 handoffs each way over two hours.
- **Expected result.** One of two outcomes: a stable two-host list with working handoff, or a disconnect roughly every 40 seconds as in issue #782.
- **Success.** All three questions answered yes, with at most one unexpected drop.
- **Stop.** Repeatable 36 to 40 second drops with no workaround. The spoofing route is then blocked on this firmware. Return to level 1, or study unspoofed sharing in 2C.

A variant is Google's [Bumble](https://google.github.io/bumble/platforms/windows.html) on Windows with the same dongle bound to WinUSB by Zadig. That keeps you in Windows without Test Mode and is reversible in Device Manager. It gives you the protocol channel but no audio stream unless you build one, so it answers the first and third questions only.

**2C. Observation on Windows with NTPods (half a day plus risk management)**

- **What it is.** Installing NTPods' test-signed kernel driver to read the AirPods' host list and routing messages from Windows.
- **Why it is needed.** On the built-in Windows stack, nothing else can open the protocol channel. Run it only if 2B succeeded, or to study sharing without spoofing.
- **What it changes.** Secure Boot off in firmware settings, `TESTSIGNING` on, a self-signed certificate trusted for kernel code, and a third-party kernel driver loaded. Section 9 lists the consequences.
- **Prerequisites.** BitLocker recovery key saved, a restore point, and a check that the games and apps you rely on tolerate Test Mode. This is your everyday laptop, so do not run 2C on it directly. A Windows virtual machine with the dongle passed through would confine Test Mode to the VM; I have not verified that this works. Without that or a spare PC, skip 2C.
- **Action.** Install the nightly build, repeat the matrix, and read the daemon log's `multipoint:` and `route:` lines. Write no code yet.
- **Expected result.** The log shows when a second host appears and which message announces a handoff.
- **Success.** Both hosts listed for ten minutes or more, and the handoff message identified.
- **Stop.** A blue screen, a device stuck in error code 38 more than once, or software you need refusing to run. Uninstall, turn test-signing off, re-enable Secure Boot.

### Stage 3: development for a reliable solution

Start only if Stage 2 showed both links holding and one takeover working on a path that Windows can use.

**Prerequisites**

- Stage 2 logs, including the exact message sequence that moved the audio.
- A decision between the two transports in section 8.
- For the driver route: Visual Studio, the Windows Driver Kit, and a machine you accept running in Test Mode.

**Actions**

1. Port the routing state machine from LibrePods' Kotlin or Rust: ownership, host list, audio source, takeover and give-back.
2. Build audio route control: release and reconnect the A2DP and hands-free services, serialised, debounced, and only while connected.
3. Add activity detection and policy: media sessions, active render streams, microphone use, and per-situation rules like LibrePods' takeover settings.
4. Close the gap that causes the 9A348 drops, if it reproduced. Issue #782 lists the newer handshake and the attribute queries macOS sends. This is research with no guaranteed end.
5. Manage identity: set and restore the Device ID record, with clear re-pair guidance.
6. Add a tray control with "take" and "give back", the host list, and a hotkey.
7. Soak test across sleep, resume, case open and close, calls and notifications.

**Expected results**

- Audio follows whichever device you are using, or moves on one click.
- Neither device drops to "Paired" during normal use.

**Success criteria**

- 50 consecutive handoffs in each direction without a dropped link.
- Median switch time under three seconds.
- One week of daily use with no stuck audio that needs a manual toggle.
- No iPhone call interrupted by the PC.

**Stop conditions**

- The drop problem is not solved after two weeks of focused work.
- A firmware update breaks the path twice within a month.
- The cost of running in Test Mode proves unacceptable and the dongle route is not worth its extra work.

## 8. Likely architecture

If new software is needed, the new part is one user-mode routing service. Everything beneath it already exists in some form, and only the transport forces a choice.

&#91;embedded content: architecture · design A, kernel profile driver\]

The picture shows design A. In design B the shaded kernel band is replaced by a user-mode stack that owns a spare dongle, and audio route control moves into that stack.

| Component | Job | Exists today |
| --- | --- | --- |
| Routing service | Tracks ownership, the host list and the audio source; decides when to take or give back; sends the takeover messages | LibrePods on Android and Linux. New for Windows. |
| AAP session | Handshake, feature flags, notification subscription, keep-alive probes | NTPods, LibrePods, and your own implementation |
| Audio route control | Releases and reconnects the A2DP and hands-free services | NTPods, through `BluetoothSetServiceState` |
| Activity detector | Knows whether the PC is playing, in a call or idle | NTPods, using media sessions and Core Audio render sessions |
| Host identity | Sets and restores the Device ID record | Documented registry values; no project automates it |
| Transport | Carries the protocol channel to the AirPods | Two designs, below |
| Tray app | Status, "take" and "give back", hotkey, logs | NTPods' WinUI app over named-pipe IPC |

**The transport choice**

|  | Design A: kernel profile driver | Design B: user-mode stack on a dedicated dongle |
| --- | --- | --- |
| Reference | NTPods (GPL-3.0) | airlow (LGPL-2.1 or later), Bumble for prototyping |
| Test Mode and Secure Boot off | Required | Not required |
| Radio | Built-in adapter, shared with Windows | A spare USB dongle that Windows can no longer use |
| Audio path | Windows' own A2DP and hands-free, microphone included | Reimplemented: capture, SBC or AAC encoding, streaming. airlow has no call microphone. |
| Control over session setup | Limited by the Microsoft stack | Full, including the Device ID record |
| New code | Routing service only | Routing service plus stack upkeep |

Design A is less code and keeps normal Windows audio. Design B is safer for the system and gives more control over the details that may matter for the 9A348 drops. On an everyday laptop that makes design B the better fit, unless a spare machine turns up.

The user-mode parts of design A can be written in C# with P/Invoke. The driver speaks plain `DeviceIoControl` with five control codes for connect, disconnect, send, receive and status.

## 9. Risks and tradeoffs

The heaviest cost sits in design A: it means running your laptop in Test Mode with Secure Boot off for as long as you use it, because a personal project cannot realistically get a kernel driver signed.

| Item | Where it arises | What it means | Mitigation or undo |
| --- | --- | --- | --- |
| Third-party kernel driver | Stage 2C, design A | Code with kernel privilege from a young single-developer project. A fault can blue-screen. NTPods records its device node getting stuck in error code 38 after rapid reconnects. | Read and build the source yourself. Use a spare PC. Restore point first. Remove with `pnputil`. |
| Windows test-signing | Stage 2C, design A | With it on, Microsoft says "any certificate can sign drivers" the kernel loads. That lowers protection for the whole machine, not only for this driver. | Keep it off the laptop you rely on, or turn it off after testing. |
| Secure Boot off | Needed to enable test-signing | BitLocker may demand its recovery key. Software that requires Secure Boot stops working. BattlEye has a documented "Windows Test-Signing Mode not supported" error. | Save the recovery key. Check your games and work apps first. Re-enable afterwards. |
| No route to a signed driver | Stage 3, design A | Microsoft's [Hardware Developer Program](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/hardware-program-register) requires an EV code-signing certificate and is framed for organisations. MagicPods' [driver](https://magicpods.app/magicaap/) is in the same position. | Accept Test Mode permanently, or choose design B. |
| Device ID registry change | Stage 2A, design A | A system-wide identity change. Effects on your other Bluetooth devices are unknown. LibrePods warns of AirPods disconnecting with an Apple ID set. | Export the key first. Delete the values and reboot to undo. |
| Re-pairing versus resetting | Stage 2A, 2B | Re-pairing in Windows or Linux removes only that device's pairing. A full AirPods reset wipes every pairing and means setting them up on the iPhone again. | No step in this plan needs a reset. Treat any apparent need for one as a stop condition. |
| Firmware | Always | You cannot pin, decline or roll back AirPods firmware. Any update can change behaviour, as the 9A348 reports suggest. This plan involves no modified firmware. | None. Budget for breakage and keep level 1 working as the fallback. |
| Dongle rebinding | Stage 2B variant, design B | The dongle stops being a Windows Bluetooth radio while bound to WinUSB. | Reversible in Device Manager. Never rebind the built-in adapter. |
| Acting as an Apple device | Stage 2 onward | Takeover messages make your own iPhone show banners and hand over audio. A bug could interrupt a call. The protocol is reverse-engineered and unsupported. | Test on your own devices only, and never during calls you care about. |

Two smaller points. NTPods and LibrePods are GPL-3.0, so anything you distribute that builds on them must be GPL-3.0 too. And Microsoft is ending kernel trust for old cross-signed drivers, starting in evaluation mode with the April 2026 update ([The Register](https://www.theregister.com/2026/03/27/microsoft_kernel_trust/)), which is why MagicPods says its community-signed driver works only on earlier Windows versions.

## 10. Effort and main unknowns

The decision costs about two weekends; the build costs two to three months part-time if the decision is yes. These are rough estimates for one developer who already knows the protocol.

| Work | Rough effort |
| --- | --- |
| Stage 0 and Stage 1 | 3 to 5 hours |
| Stage 2A registry test | 1 hour |
| Stage 2B handoff test on Linux | 10 to 20 hours, plus a dongle |
| Stage 2C observation with NTPods | 4 to 6 hours, plus backup and recovery preparation |
| Stage 3, design A, building on NTPods | 60 to 120 hours |
| Stage 3, design A, with your own driver | Add 40 to 80 hours |
| Stage 3, design B, user-mode stack | 200 to 400 hours |

The Stage 3 ranges are wide because the largest item, satisfying firmware 9A348, is open-ended research.

**Main unknowns, in the order the plan resolves them**

1. Does your AirPods firmware keep a Windows link alive while the iPhone plays, with no changes at all? Stage 1.
2. Does an Apple Device ID on Windows change that? Stage 2A.
3. Does the 40-second drop reproduce outside Android, and can a fuller session setup prevent it? Stage 2B.
4. Does your iPhone honour a takeover request from a non-Apple host on current iOS? Stage 2B.
5. Was an Apple Device ID set during NTPods' two-host captures? The maintainer can answer.
6. Can Windows move the audio route in about a second without the service toggle that risks error code 38? Stage 2C and Stage 3.
7. What do the two flag bytes per host in the `0x2E` list mean? NTPods saw them vary for one device; issue #782 reads a change to zero as the AirPods deregistering a host.
8. Will the next firmware update change any of this? Not resolvable.

## 11. Assumptions and what to check

I assumed 64-bit Windows 11 on version 24H2 or 25H2, an iPhone on iOS 27, and AirPods Pro 3 on firmware 9A348. Each assumption changes part of the plan if it is wrong. You confirmed this is your everyday laptop, so the plan keeps Test Mode off it.

- [ ] **Windows version and build.** Run `winver`. NTPods supports Windows 10 and 11, but its microphone driver differs before Windows 11 22H2.
- [ ] **Secure Boot state.** Open System Information and read "Secure Boot State". It matters only for Stage 2C.
- [ ] **BitLocker or device encryption.** Check Settings, Privacy and security. Save the recovery key before any firmware-setting change.
- [ ] **Memory Integrity.** Check Windows Security, Device security, Core isolation. I did not verify how it treats test-signed drivers.
- [ ] **Bluetooth adapter.** Read the model and driver version in Device Manager. NTPods' captures used an Intel AX210; airlow used a MediaTek controller.
- [ ] **iOS version.** Settings, General, About.
- [ ] **AirPods firmware.** On the iPhone, open Bluetooth, tap the info button beside the AirPods, and read Version. If it is newer than 9A348, the drop reports may not apply as written.
- [ ] **"Connect to This iPhone" setting.** Note whether it is "Automatically" or "When Last Connected to This iPhone".
- [ ] **Software that needs Secure Boot or rejects Test Mode.** List games with anti-cheat and any work or banking software.
- [ ] **A spare dongle.** On an everyday laptop it is what keeps Stage 2 away from your boot settings.

## 12. Sources

All pages below were opened on 8 October 2026. Repositories were cloned and read at the commits current that day.

**Apple**

- [Switch your AirPods to another device](https://support.apple.com/HT212204)
- [Pair AirPods with a non-Apple device](https://support.apple.com/guide/airpods/pair-airpods-with-a-non-apple-device-dev499c9718b/web)
- [About firmware updates for AirPods](https://support.apple.com/en-us/106340)

**Microsoft**

- [Bluetooth host radio support](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-host-radio-support), including the Device ID registry values
- [Bluetooth and socket](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-socket)
- [The TESTSIGNING boot configuration option](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option)
- [Driver signing policy](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-)
- [Register for the Hardware Developer Program](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/hardware-program-register)

**LibrePods**

- [Repository and README](https://github.com/librepods-org/librepods)
- [AACPManager.kt](https://github.com/librepods-org/librepods/blob/main/android/app/src/main/java/me/kavishdevar/librepods/bluetooth/AACPManager.kt) and [AirPodsService.kt](https://github.com/librepods-org/librepods/blob/main/android/app/src/main/java/me/kavishdevar/librepods/services/AirPodsService.kt)
- [Linux README](https://github.com/librepods-org/librepods/blob/main/linux/README.md) and the [linux/rust branch](https://github.com/librepods-org/librepods/tree/linux/rust)
- [Issue #782](https://github.com/librepods-org/librepods/issues/782), [issue #700](https://github.com/librepods-org/librepods/issues/700), [PR #202](https://github.com/librepods-org/librepods/pull/202), [discussion #400](https://github.com/librepods-org/librepods/discussions/400)

**NTPods**

- [Repository](https://github.com/arctumn/ntpods), [Windows README](https://github.com/arctumn/ntpods/blob/main/windows/README.md), [driver README](https://github.com/arctumn/ntpods/blob/main/windows/drivers/aap/README.md), [heart-rate notes](https://github.com/arctumn/ntpods/blob/main/windows/docs/heart-rate.md)
- Commits [1306b3b](https://github.com/arctumn/ntpods/commit/1306b3be35577df4c505c5a3062caebb16b0b650), [4f9e3c7](https://github.com/arctumn/ntpods/commit/4f9e3c7acf6c7a040403c87ad5e0a5e3a3ff2d7e), [9bf9aa4](https://github.com/arctumn/ntpods/commit/9bf9aa4f5eae09919ffdea0fa30dee5679e3557e)
- [Issue #11](https://github.com/arctumn/ntpods/issues/11) and the earlier fork, [arctumn/librepods](https://github.com/arctumn/librepods)

**Other projects and references**

- [airlow](https://github.com/jkryspin/airlow), [MagicPods](https://github.com/steam3d/MagicPods-Windows) and its [MagicAAP driver page](https://magicpods.app/magicaap/)
- [Tblob18/librepods-windows](https://github.com/Tblob18/librepods-windows), [brianpht/librepods-rs](https://github.com/brianpht/librepods-rs), [WinPods](https://github.com/changcheng967/WinPods)
- [Bumble on Windows](https://google.github.io/bumble/platforms/windows.html), [apple-wireshark dissector](https://github.com/pabloaul/apple-wireshark)
- [The Register on the kernel trust change](https://www.theregister.com/2026/03/27/microsoft_kernel_trust/)
- [Epic Games on BattlEye and test-signing](https://www.epicgames.com/help/technical-support-c-202300000001619/third-party-support-c-202300000001668/failed-to-initialize-battleye-service-windows-test-signing-mode-not-supported-error-a202300000012901)
