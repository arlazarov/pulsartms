"""Exercise the real local API; keep authentication tokens only in memory."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import threading
import time
import urllib.error
import urllib.request

ORIGIN = "http://localhost:5086"


def request(path, token=None, data=None, timeout=240):
    headers = {"X-Route-Geometry": "encoded"}
    if token:
        headers["Authorization"] = "Bearer " + token
    if data is not None:
        headers["Content-Type"] = "application/json"
    call = urllib.request.Request(
        ORIGIN + path, headers=headers,
        data=json.dumps(data).encode() if data is not None else None,
    )
    with urllib.request.urlopen(call, timeout=timeout) as response:
        raw = response.read()
        value = json.loads(raw) if raw else None
    if isinstance(value, dict) and value.get("success") is False:
        raise RuntimeError(str(value.get("errors")))
    return value


def login(index=0):
    email = ("load-test@example.invalid" if index == 0
             else f"load-test-{index}@example.invalid")
    return request("/api/auth/login", data={
        "email": email, "password": "Synthetic-local-100!",
    })["accessToken"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--seconds", type=int, default=240)
    parser.add_argument("--idle", type=int, default=180)
    parser.add_argument("--detour", action="store_true")
    parser.add_argument("--dispatchers", type=int, default=5)
    args = parser.parse_args()
    output = Path(os.environ["PULSARTMS_ARTIFACT_DIR"])
    (output / ".keep").touch()
    token = login()
    fleet = request("/probe/fleet", token)
    stop = threading.Event()
    phase = "baseline"
    lock = threading.Lock()

    def write(name, value):
        with lock, (output / (name + ".jsonl")).open("a") as stream:
            stream.write(json.dumps({"time": time.time(), "phase": phase,
                                     **value}) + "\n")

    def timed(path, credential, body=None, category="read"):
        start = time.monotonic()
        try:
            result = request(path, credential, body)
            write("requests", {"category": category, "ok": True,
                               "ms": (time.monotonic() - start) * 1000})
            return result
        except (OSError, ValueError, RuntimeError) as error:
            write("requests", {"category": category, "ok": False,
                               "ms": (time.monotonic() - start) * 1000,
                               "error": str(error)})
            raise

    def samples():
        while not stop.is_set():
            try:
                write("memory", request("/api/diagnostics/memory", token,
                                        timeout=15))
            except (OSError, ValueError) as error:
                write("memory", {"error": str(error)})
            stop.wait(3)

    def dispatcher(index, duration):
        credential = login(index + 1)
        deadline = time.monotonic() + duration
        iteration = 0
        while time.monotonic() < deadline and not stop.is_set():
            truck = fleet[(iteration * 7 + index) % len(fleet)]["truck"]
            try:
                timed("/api/fleet/locations", credential)
                timed(f"/api/fleet/trucks/{truck}/planning",
                      credential, {}, "truck-planning")
                timed(f"/api/dispatch/board?page={1 + index % 2}", credential,
                      category="board")
            except (OSError, ValueError, RuntimeError):
                pass
            iteration += 1
            stop.wait(5)

    monitor = threading.Thread(target=samples, daemon=True)
    monitor.start()
    try:
        write("state", request("/probe/state", token))
        if args.prepare:
            phase = "cold-preparation"
            with ThreadPoolExecutor(max_workers=2) as pool:
                def prepare(index):
                    result = timed(f"/probe/prepare/{index}", token, {},
                                   "prepare")
                    print(json.dumps({"truck": index + 1,
                                      "prepared": result}), flush=True)
                for start in range(0, len(fleet), 2):
                    request("/probe/tick?detour=false", token, {})
                    list(pool.map(prepare, range(start, min(start + 2,
                                                          len(fleet)))))
            write("state", request("/probe/state", token))
        phase = "steady"
        if args.detour:
            phase = "detour"
        with ThreadPoolExecutor(max_workers=args.dispatchers) as pool:
            readers = [pool.submit(dispatcher, i, args.seconds)
                       for i in range(args.dispatchers)]
            deadline = time.monotonic() + args.seconds
            iteration = 0
            while time.monotonic() < deadline:
                timed(f"/probe/tick?detour={str(args.detour).lower()}",
                      token, {}, "telemetry")
                timed(f"/probe/enqueue?version=load-{time.time_ns()}",
                      token, {}, "enqueue")
                write("state", request("/probe/state", token))
                iteration += 1
                time.sleep(min(30, max(0, deadline - time.monotonic())))
            for result in readers:
                result.result()
        if args.detour:
            phase = "fuel-eta-refresh"
            with ThreadPoolExecutor(max_workers=2) as pool:
                list(pool.map(lambda i: timed(
                    f"/probe/calculate/{i}", token, {}, "recalculate"
                ), range(len(fleet))))
        phase = "drain"
        deadline = time.monotonic() + 600
        while True:
            state = request("/probe/state", token)
            write("state", state)
            if state["pending"] == 0:
                break
            if time.monotonic() > deadline:
                raise RuntimeError("Planning queue failed to drain.")
            time.sleep(5)
        phase = "idle"
        time.sleep(args.idle)
        write("state", request("/probe/state", token))
        write("map", request("/api/diagnostics/memory/map", token))
        print(json.dumps({"completed": True, "trucks": len(fleet)}), flush=True)
    finally:
        stop.set()
        monitor.join(timeout=20)


if __name__ == "__main__":
    main()
