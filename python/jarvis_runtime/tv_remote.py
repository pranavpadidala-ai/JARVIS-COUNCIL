"""JARVIS remote for Android TV devices such as the Jio set-top box (JHSC200v3), on the same Wi-Fi.

Uses androidtvremote2 (the library Home Assistant uses). Pairing happens once: the TV shows a code,
the user reads it out, and a certificate is saved under %LOCALAPPDATA%\\JARVIS-Council\\tv.

Commands (each prints one JSON line):
  discover                       find Android TV devices on the network (5 s)
  pair <host>                    start pairing; prints {"code_required": true}, then reads the code from stdin
  key <host> <KEY>               send a key (POWER, HOME, BACK, VOLUME_UP, MUTE, MEDIA_PLAY_PAUSE, DPAD_UP, ...)
  launch <host> <url-or-package> open an app (e.g. https://www.youtube.com or com.jio.media.stb.ondemand)
  text <host> <text>             type text into the focused search box
  status <host>                  name, power and current app
"""
from __future__ import annotations

import asyncio
import json
import os
import sys

from androidtvremote2 import AndroidTVRemote, CannotConnect, ConnectionClosed, InvalidAuth

STATE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "JARVIS-Council", "tv")
CERT = os.path.join(STATE, "cert.pem")
KEY = os.path.join(STATE, "key.pem")


def out(**data) -> None:
    print(json.dumps(data), flush=True)


def remote(host: str) -> AndroidTVRemote:
    os.makedirs(STATE, exist_ok=True)
    return AndroidTVRemote("JARVIS", CERT, KEY, host, enable_ime=True)


async def discover() -> None:
    from zeroconf import ServiceBrowser, Zeroconf

    found: dict[str, str] = {}

    class Listener:
        def add_service(self, zc, type_, name):
            info = zc.get_service_info(type_, name, 2000)
            if info and info.parsed_addresses():
                found[name.split(".")[0]] = info.parsed_addresses()[0]

        def update_service(self, *args):
            pass

        def remove_service(self, *args):
            pass

    zc = Zeroconf()
    try:
        ServiceBrowser(zc, "_androidtvremote2._tcp.local.", Listener())
        await asyncio.sleep(5)
    finally:
        zc.close()
    out(ok=True, devices=[{"name": n, "host": h} for n, h in found.items()])


async def pair(host: str) -> None:
    r = remote(host)
    await r.async_generate_cert_if_missing()
    name, mac = await r.async_get_name_and_mac()
    await r.async_start_pairing()
    out(code_required=True, name=name)
    code = (await asyncio.get_running_loop().run_in_executor(None, sys.stdin.readline)).strip()
    if not code:
        out(ok=False, error="No pairing code was given.")
        return
    await r.async_finish_pairing(code)
    out(ok=True, paired=name)


async def connected(host: str) -> AndroidTVRemote:
    if not (os.path.exists(CERT) and os.path.exists(KEY)):
        raise InvalidAuth("not paired")
    r = remote(host)
    await r.async_connect()
    return r


async def run(cmd: str, host: str, arg: str) -> None:
    r = await connected(host)
    try:
        if cmd == "key":
            r.send_key_command(arg.upper() if arg.upper().startswith("KEYCODE_") else "KEYCODE_" + arg.upper())
            out(ok=True, sent=arg.upper())
        elif cmd == "launch":
            r.send_launch_app_command(arg)
            out(ok=True, launched=arg)
        elif cmd == "text":
            r.send_text(arg)
            out(ok=True, typed=arg)
        else:
            info = r.device_info or {}
            out(ok=True, is_on=r.is_on, current_app=r.current_app, model=info.get("model"), manufacturer=info.get("manufacturer"))
        await asyncio.sleep(0.3)  # let the command flush before disconnecting
    finally:
        r.disconnect()


def main(argv: list[str]) -> int:
    if not argv:
        out(ok=False, error="usage: discover | pair <host> | key|launch|text|status <host> [arg]")
        return 2
    cmd = argv[0]
    try:
        if cmd == "discover":
            asyncio.run(discover())
        elif cmd == "pair" and len(argv) >= 2:
            asyncio.run(pair(argv[1]))
        elif cmd in ("key", "launch", "text") and len(argv) >= 3:
            asyncio.run(run(cmd, argv[1], " ".join(argv[2:])))
        elif cmd == "status" and len(argv) >= 2:
            asyncio.run(run(cmd, argv[1], ""))
        else:
            out(ok=False, error="bad arguments")
            return 2
    except InvalidAuth:
        out(ok=False, error="Not paired with this TV yet. Pair first; the TV will show a code.")
        return 1
    except (CannotConnect, ConnectionClosed, OSError) as e:
        out(ok=False, error=f"Could not reach the TV ({e}). Is it on and on the same Wi-Fi?")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
