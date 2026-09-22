"""Isolated local API fixture. Never load the application's User Secrets."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.request
import uuid


def fixture_environment():
    path = (
        Path.home()
        / ".local/share/pulsartms/development/databases.json"
    )
    document = json.loads(path.read_text())
    rows = document if isinstance(document, list) else document["databases"]
    row = next(
        x for x in rows
        if x["database"].startswith("pulsr_core_fixture_")
    )
    return dict(os.environ, PULSR_LOAD_CONNECTION=row["connection"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["start", "stop", "user"])
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--trucks", type=int, choices=[10, 50, 100], default=10)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--memory", choices=["512m", "1g"], default="1g")
    parser.add_argument("--platform", default="linux/amd64",
                        choices=["linux/amd64", "linux/arm64"])
    args = parser.parse_args()
    output = Path(os.environ["PULSARTMS_ARTIFACT_DIR"])
    (output / ".keep").touch()
    environment = fixture_environment()
    image = "mcr.microsoft.com/dotnet/aspnet:10.0"
    common = [
        "docker", "run", "--platform", args.platform,
        "-e", "PULSR_LOAD_CONNECTION",
        "-e", "PULSR_LOAD_URL=http://0.0.0.0:5086", "--cpus", "1",
        "--memory", args.memory, "--memory-swap", args.memory,
        "-v", f"{args.publish.resolve()}:/app:ro", "-w", "/app",
    ]
    if args.action == "user":
        saved = json.loads(args.manifest.read_text())
        login_path = (Path.home()
                      / ".local/share/pulsartms/development/load-fixture-login.json")
        login = json.loads(login_path.read_text())
        environment["PULSR_LOAD_USER_EMAIL"] = login["email"]
        environment["PULSR_LOAD_USER_PASSWORD"] = login["password"]
        subprocess.run(common + ["--rm", "-e", "PULSR_LOAD_USER_EMAIL",
                       "-e", "PULSR_LOAD_USER_PASSWORD", image, "dotnet",
                       "FleetLoadProbe.dll", "user", saved["schema"],
                       str(saved["trucks"])], env=environment, check=True)
        return
    if args.action == "stop":
        saved = json.loads(args.manifest.read_text())
        subprocess.run(["docker", "stop", saved["container"]], check=True)
        subprocess.run(["docker", "rm", saved["container"]], check=True)
        subprocess.run(
            common + ["--rm", image, "dotnet", "FleetLoadProbe.dll",
                      "drop", saved["schema"], str(saved["trucks"])],
            env=environment, check=True,
        )
        return
    schema = "load_" + uuid.uuid4().hex
    name = "pulsr-" + schema
    saved = {"schema": schema, "container": name, "trucks": args.trucks, "memory": args.memory,
             "platform": args.platform, "cpus": 1}
    (output / "fixture.json").write_text(json.dumps(saved, indent=2) + "\n")
    with (output / "seed.log").open("w") as log:
        result = subprocess.run(
            common + ["--rm", image, "dotnet", "FleetLoadProbe.dll",
                      "seed", schema, str(args.trucks)],
            env=environment, stdout=log, stderr=subprocess.STDOUT,
        )
    if result.returncode:
        raise SystemExit("Fixture seed failed; see seed.log.")
    login_path = (Path.home()
                  / ".local/share/pulsartms/development/load-fixture-login.json")
    if login_path.exists():
        login = json.loads(login_path.read_text())
        environment["PULSR_LOAD_USER_EMAIL"] = login["email"]
        environment["PULSR_LOAD_USER_PASSWORD"] = login["password"]
        subprocess.run(common + ["--rm", "-e", "PULSR_LOAD_USER_EMAIL",
                       "-e", "PULSR_LOAD_USER_PASSWORD", image, "dotnet",
                       "FleetLoadProbe.dll", "user", schema, str(args.trucks)],
                       env=environment, check=True)
    result = subprocess.run(
        common + ["-d", "--name", name,
                  "-p", "127.0.0.1:5086:5086", image,
                  "dotnet", "FleetLoadProbe.dll", "serve", schema,
                  str(args.trucks)],
        env=environment, check=True, capture_output=True, text=True,
    )
    for _ in range(90):
        try:
            with urllib.request.urlopen(
                "http://localhost:5086/probe/health", timeout=2
            ) as response:
                if response.status == 200:
                    print(json.dumps(saved), flush=True)
                    return
        except (OSError, TimeoutError):
            pass
        state = subprocess.check_output(
            ["docker", "inspect", "--format", "{{.State.Running}}", name],
            text=True,
        ).strip()
        if state != "true":
            break
        time.sleep(1)
    with (output / "server.log").open("w") as log:
        subprocess.run(["docker", "logs", name], stdout=log,
                       stderr=subprocess.STDOUT)
    raise SystemExit("Fixture API did not become ready; see server.log.")


if __name__ == "__main__":
    main()
