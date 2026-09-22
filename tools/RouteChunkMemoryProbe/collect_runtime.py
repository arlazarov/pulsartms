"""Read protected process counters; never print or archive credentials."""

import argparse
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--seconds", type=int, default=1080)
    parser.add_argument("--interval", type=int, default=5)
    parser.add_argument("--include-map", action="store_true")
    args = parser.parse_args()
    if not 1 <= args.seconds <= 3600 or not 2 <= args.interval <= 60:
        parser.error("Use 1–3600 seconds and a 2–60 second interval.")
    destination = Path(os.environ["PULSARTMS_ARTIFACT_DIR"])
    session_path = (
        Path.home()
        / ".local/share/pulsartms/development/runtime-diagnostics-session.json"
    )
    session = json.loads(session_path.read_text())
    origin = "https://amftms-api-ddgxhwho3a-uk.a.run.app"

    def read(path):
        request = urllib.request.Request(
            origin + path,
            headers={"Authorization": "Bearer " + session["accessToken"]},
        )
        with urllib.request.urlopen(request, timeout=15) as response:
            return json.load(response)

    deadline = time.monotonic() + args.seconds
    next_summary = 0
    with (
        (destination / "runtime.jsonl").open("a", buffering=1) as output,
        (destination / "stages.jsonl").open("a", buffering=1) as stages,
        (destination / "maps.jsonl").open("a", buffering=1) as maps,
    ):
        (destination / ".keep").touch()
        while time.monotonic() < deadline:
            started = time.monotonic()
            try:
                snapshot = read("/api/diagnostics/memory")
                output.write(json.dumps(snapshot) + "\n")
                if started >= next_summary:
                    runtime = snapshot["runtime"]
                    print(
                        json.dumps(
                            {
                                "time": runtime["observedAt"],
                                "started": runtime["startedAt"],
                                "rssMiB": runtime["workingSetBytes"] / 2**20,
                                "managedMiB": runtime["managedBytesEstimate"]
                                / 2**20,
                                "heapMiB": runtime["heapBytesAfterLastGc"]
                                / 2**20,
                                "committedMiB": runtime[
                                    "committedBytesAfterLastGc"
                                ] / 2**20,
                                "fragmentedMiB": runtime[
                                    "fragmentedBytesAfterLastGc"
                                ] / 2**20,
                                "gc2": runtime["gen2Collections"],
                                "cacheEstimatedMiB": sum(
                                    x["estimatedSize"] or 0
                                    for x in snapshot["caches"]
                                    if x["unit"] == "bytes"
                                ) / 2**20,
                            }
                        ),
                        flush=True,
                    )
                    stages.write(json.dumps({
                        "time": runtime["observedAt"],
                        "stages": read("/api/diagnostics/stages"),
                    }) + "\n")
                    if args.include_map:
                        maps.write(json.dumps(
                            read("/api/diagnostics/memory/map")
                        ) + "\n")
                    next_summary = started + 30
            except urllib.error.HTTPError as error:
                output.write(json.dumps({"httpStatus": error.code}) + "\n")
                if error.code in (401, 403):
                    raise SystemExit("Diagnostic session needs authentication.")
            except (urllib.error.URLError, TimeoutError):
                output.write(json.dumps({"transportFailure": True}) + "\n")
            remaining = deadline - time.monotonic()
            if remaining > 0:
                time.sleep(min(args.interval, remaining))


if __name__ == "__main__":
    main()
