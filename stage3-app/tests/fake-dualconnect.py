#!/usr/bin/env python3
"""Stand-in for DualConnect.exe, used only by the tests in this folder.

Follows stage1-tools/INTERFACE.md (final, 9 Oct 2026): one JSON line on stdout with --json,
exit codes 0-4, messages worded as DualConnect.cs words them.
FAKE_STATE names a file holding "on" or "off" (whether the fake AirPods are on the laptop).
FAKE_MODE switches to failure cases: slow, garbage, notfound, refused, notconfirmed, chatty,
several, stillrunning, replaced, stopped, lockerror, watchdog.
"""
import json
import os
import sys
import time

args = sys.argv[1:]
verb = args[0] if args else ""
mode = os.environ.get("FAKE_MODE", "")
state_file = os.environ.get("FAKE_STATE", "")
MINE = "6f1d2a3b-0000-1111-2222-333344445555"
OTHER = "0a0b0c0d-9999-8888-7777-666655554444"


def read_state():
    try:
        with open(state_file) as f:
            return f.read().strip() or "off"
    except OSError:
        return "off"


def write_state(value):
    if state_file:
        with open(state_file, "w") as f:
            f.write(value)


def endpoints(on, container=MINE, suffix="aaaa"):
    st = "active" if on else "unplugged"
    return [
        {"name": "Kopfhörer (AirPods Pro)", "deviceName": "AirPods Pro", "flow": "render",
         "state": st, "isDefault": on, "peak": 0.0 if on else -1, "id": "{0.0.0.00000000}.{%s}" % suffix,
         "filterId": "{2}.\\\\?\\bthenum#x", "containerId": container},
        {"name": "Headset (AirPods Pro)", "deviceName": "AirPods Pro", "flow": "capture",
         "state": st, "isDefault": False, "peak": -1, "id": "{0.0.1.00000000}.{%s}" % suffix,
         "filterId": None, "containerId": container},
    ]


def emit(ok, code, message, eps):
    # ensure_ascii mirrors INTERFACE.md: non-ASCII arrives as \uXXXX escapes
    print(json.dumps({"command": verb, "ok": ok, "exitCode": code, "elapsedMs": 1234,
                      "message": message, "endpoints": eps}, ensure_ascii=True))
    sys.stdout.flush()
    sys.exit(code)


on_now = read_state() == "on"
if mode == "slow":
    time.sleep(30)
if mode == "garbage":
    print("this is not json")
    sys.exit(0)
if mode == "notfound":
    emit(False, 2, "no Bluetooth audio endpoint matches 'AirPods'", [])
if mode == "refused":
    emit(False, 3, "reconnect: 0x80070005 Windows refused the connect", endpoints(on_now))
if mode == "notconfirmed":
    emit(False, 4, "not connected at timeout; the AirPods may be in the case, out of range or busy with the iPhone",
         endpoints(False))
if mode == "several" and "--container" not in args:
    msg = ("2 paired devices match the name: 'AirPods Pro' (connected, containerId %s), 'AirPods Pro' "
           "(not connected, containerId %s). Some have the same name, so --name can't tell them apart: "
           "add --container with the containerId of yours." % (MINE, OTHER))
    if verb != "status":
        msg += " Nothing was sent."
    emit(False, 1, msg, endpoints(True) + endpoints(False, OTHER, "cccc"))
if mode == "stillrunning":
    emit(False, 4, "an earlier DualConnect command moving the sound the same way is still running; nothing was sent",
         endpoints(on_now))
if mode == "replaced":
    emit(False, 4, "replaced by a newer DualConnect command; nothing was sent", endpoints(on_now))
if mode == "stopped":
    emit(False, 4, "reconnect sent to 1 filter(s). stopped because a newer DualConnect command started; "
         "asked Windows to drop the connect it had started", endpoints(False))
if mode == "lockerror":
    emit(False, 3, "could not open the DualConnect lock: Access to the path is denied.; nothing was sent", [])
if mode == "watchdog":
    emit(False, 3, "a Windows audio call did not return; gave up", [])
if mode == "chatty":
    print("Some human text first")
    print("warning: something", file=sys.stderr)

argv_echo = json.dumps(args)
if verb == "status":
    emit(True, 0, "argv=" + argv_echo, endpoints(on_now))
if verb in ("take", "connect"):
    write_state("on")
    emit(True, 0, "argv=" + argv_echo, endpoints(True))
if verb in ("give", "disconnect"):
    write_state("off")
    emit(True, 0, "argv=" + argv_echo, endpoints(False))
emit(False, 1, "unknown command: " + verb, [])
