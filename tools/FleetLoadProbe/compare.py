"""Compare two local builds on the same isolated, already prepared fixture."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import time

from exercise import login, request
from run import fixture_environment


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--baseline-platform", default="linux/amd64",
                        choices=["linux/amd64", "linux/arm64"])
    parser.add_argument("--candidate-platform", default="linux/amd64",
                        choices=["linux/amd64", "linux/arm64"])
    parser.add_argument("--compact-gc", action="store_true")
    args = parser.parse_args()
    saved = json.loads(args.manifest.read_text())
    schema = saved["schema"]
    name = saved["container"]
    if not re.fullmatch(r"load_[0-9a-f]{32}", schema):
        raise ValueError("Not an isolated probe schema.")
    if name != "pulsr-" + schema:
        raise ValueError("Unexpected probe container.")
    output = Path(os.environ["PULSARTMS_ARTIFACT_DIR"])
    (output / ".keep").touch()

    def restart(publish, platform):
        subprocess.run(["docker", "stop", name], check=True,
                       stdout=subprocess.DEVNULL)
        subprocess.run(["docker", "rm", name], check=True,
                       stdout=subprocess.DEVNULL)
        subprocess.run([
            "docker", "run", "-d", "--platform", platform,
            "--name", name, "-e", "PULSR_LOAD_CONNECTION",
            "-e", "PULSR_LOAD_URL=http://0.0.0.0:5086",
            "--cpus", "1", "--memory", "1g", "--memory-swap", "1g",
            "-v", f"{publish.resolve()}:/app:ro", "-w", "/app",
            "-p", "127.0.0.1:5086:5086",
            "mcr.microsoft.com/dotnet/aspnet:10.0", "dotnet",
            "FleetLoadProbe.dll", "serve", schema, str(saved["trucks"]),
        ], env=fixture_environment(), check=True, stdout=subprocess.DEVNULL)
        for _ in range(90):
            try:
                request("/probe/health", timeout=2)
                return
            except OSError:
                time.sleep(1)
        raise RuntimeError("Probe did not start.")

    candidate_running = False
    try:
        for label, publish, platform in [
            ("baseline", args.baseline, args.baseline_platform),
            ("candidate", args.candidate, args.candidate_platform),
        ]:
            restart(publish, platform)
            candidate_running = label == "candidate"
            token = login()
            fleet = request("/probe/fleet", token)
            request("/probe/tick?detour=false", token, {})
            for phase in ["cold", "warm"]:
                before = request("/api/diagnostics/memory", token)["runtime"]
                start = time.monotonic()
                operations = []
                for index in [0, len(fleet) // 2, len(fleet) - 1]:
                    at = time.monotonic()
                    truck = fleet[index]["truck"]
                    request(f"/api/fleet/trucks/{truck}/planning", token, {})
                    operations.append({"truck": index + 1,
                                       "seconds": time.monotonic() - at})
                at = time.monotonic()
                result = request("/probe/calculate/0", token, {})
                fuel_seconds = time.monotonic() - at
                after = request("/api/diagnostics/memory", token)["runtime"]
                record = {
                    "build": label, "platform": platform, "phase": phase,
                    "seconds": time.monotonic() - start,
                    "allocatedBytes": after["totalAllocatedBytes"]
                    - before["totalAllocatedBytes"],
                    "planning": operations, "fuelEtaSeconds": fuel_seconds,
                    "result": result, "runtime": after,
                }
                with (output / "comparison.jsonl").open("a") as stream:
                    stream.write(json.dumps(record) + "\n")
                print({k: v for k, v in record.items() if k != "runtime"},
                      flush=True)
            if args.compact_gc:
                time.sleep(30)
                snapshot = {
                    "platform": platform,
                    "idle": request("/api/diagnostics/memory", token),
                    "gc": request("/probe/compact-gc", token, {}),
                    "map": request("/api/diagnostics/memory/map", token),
                }
                (output / (label + "-gc.json")).write_text(
                    json.dumps(snapshot, indent=2) + "\n"
                )
                print({"platform": platform, "compacted": True}, flush=True)
    finally:
        if not candidate_running:
            restart(args.candidate, args.candidate_platform)


if __name__ == "__main__":
    main()
