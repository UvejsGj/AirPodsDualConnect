#!/usr/bin/env python3
"""Stand-in for DualConnect.exe, used only by the tests in this folder.

Follows stage1-tools/INTERFACE.md: one JSON line on stdout with --json, exit codes 0-4.
FAKE_STATE names a file holding "on" or "off" (whether the fake AirPods are on the laptop).
FAKE_MODE switches to failure cases: slow, garbage, notfound, refused, notconfirmed, chatty.
"""
import json
import os
import sys
import time

args = sys.argv[1:]
verb = args[0] if args else ""
mode = os.environ.get("FAKE_MODE", "")
state_file = os.environ.get("FAKE_STATE", "")


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


def endpoints(on):
    st = "active" if on else "unplugged"
    return [
        {"name": "Kopfhörer (AirPods Pro)", "deviceName": "AirPods Pro", "flow": "render",
         "state": st, "isDefault": on, "peak": 0.0 if on else -1, "id": "{0.0.0.00000000}.{aaaa}"},
        {"name": "Headset (AirPods Pro)", "deviceName": "AirPods Pro", "flow": "capture",
         "state": st, "isDefault": False, "peak": -1, "id": "{0.0.1.00000000}.{bbbb}"},
    ]


def emit(ok, code, message, eps):
    # ensure_ascii mirrors INTERFACE.md: non-ASCII arrives as \uXXXX escapes
    print(json.dumps({"command": verb, "ok": ok, "exitCode": code, "elapsedMs": 1234,
                      "message": message, "endpoints": eps}, ensure_ascii=True))
    sys.stdout.flush()
    sys.exit(code)


if mode == "slow":
    time.sleep(30)
if mode == "garbage":
    print("this is not json")
    sys.exit(0)
if mode == "notfound":
    emit(False, 2, "No Bluetooth audio endpoint matches", [])
if mode == "refused":
    emit(False, 3, "HRESULT 0x80070005", endpoints(read_state() == "on"))
if mode == "notconfirmed":
    emit(False, 4, "Endpoint did not become active in time", endpoints(False))
if mode == "chatty":
    print("Some human text first")
    print("warning: something", file=sys.stderr)

argv_echo = json.dumps(args)
if verb == "status":
    emit(True, 0, "argv=" + argv_echo, endpoints(read_state() == "on"))
if verb in ("take", "connect"):
    write_state("on")
    emit(True, 0, "argv=" + argv_echo, endpoints(True))
if verb in ("give", "disconnect"):
    write_state("off")
    emit(True, 0, "argv=" + argv_echo, endpoints(False))
emit(False, 1, "unknown command", [])
