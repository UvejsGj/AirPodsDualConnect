# Security policy

## Supported versions

There are no releases yet. Only the latest code on `main` is supported.

## Reporting a vulnerability

Please don't open a public issue for a security problem.

- If the repository's **Security** tab offers **Report a vulnerability**, use it. That sends a private report to the maintainer.
- Otherwise, contact the maintainer, [@UvejsGj](https://github.com/UvejsGj), using the contact details on that GitHub profile.

Include what you found, how to reproduce it, and the Windows build you saw it on.

## What counts

These tools are meant to run as an ordinary user, with no admin rights, no driver and no registry writes. Any of these is worth reporting:

- A way to make DualConnect or DualConnect Tray run code it shouldn't, for example through its settings file, its command line or the output it reads from DualConnect.
- Anything that makes either tool ask for admin rights, write to the registry, install a driver, or change a Bluetooth or audio setting without the user asking.
- Anything that could reset or re-pair the AirPods, or touch their firmware.
- Bluetooth addresses or link keys written somewhere other users of the PC can read.
