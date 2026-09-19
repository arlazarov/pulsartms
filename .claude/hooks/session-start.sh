#!/bin/bash
set -euo pipefail

# Claude Code on the web only; local machines keep their own toolchain.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then exit 0; fi
cd "$CLAUDE_PROJECT_DIR"

export DEBIAN_FRONTEND=noninteractive DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
printf 'export DOTNET_CLI_TELEMETRY_OPTOUT=1\nexport DOTNET_NOLOGO=1\n' >> "$CLAUDE_ENV_FILE"

SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO=sudo; fi

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^10\.'; then
  # The sandbox network policy blocks builds.dotnet.microsoft.com; the Microsoft apt repository is reachable.
  . /etc/os-release
  curl -fsSL "https://packages.microsoft.com/config/$ID/$VERSION_ID/packages-microsoft-prod.deb" -o /tmp/packages-microsoft-prod.deb
  $SUDO dpkg -i /tmp/packages-microsoft-prod.deb
  $SUDO apt-get update -o Dir::Etc::sourcelist=sources.list.d/microsoft-prod.list -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=0
  $SUDO apt-get install -y --no-install-recommends dotnet-sdk-10.0
fi
dotnet --version

npm install --prefix Client --no-audit --no-fund
