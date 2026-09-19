#!/bin/bash

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

phase="${1:-all}"
case "$phase" in all|node|dotnet|artifact) ;; *) echo "Usage: bash verify-release.sh [all|node|dotnet|artifact]" >&2; exit 2 ;; esac
if [[ $# -gt 1 ]]; then echo "Unexpected release gate arguments." >&2; exit 2; fi

PULSARTMS_RELEASE_DIR="${PULSARTMS_RELEASE_DIR-${AMFTMS_RELEASE_DIR:-}}"
PULSARTMS_RELEASE_UI="${PULSARTMS_RELEASE_UI-${AMFTMS_RELEASE_UI:-0}}"
PULSARTMS_RELEASE_BROWSER="${PULSARTMS_RELEASE_BROWSER-${AMFTMS_RELEASE_BROWSER:-0}}"

if [[ ( "$phase" == all || "$phase" == dotnet ) && -z "$PULSARTMS_RELEASE_DIR" ]]; then
  exec node scripts/artifacts.mjs run release -- env 'PULSARTMS_RELEASE_DIR={artifacts}' bash verify-release.sh "$phase"
fi

if [[ "$phase" == all || "$phase" == node ]]; then
  npm ci --prefix Client
  npm run format:check --prefix Client
  npm run js:check --prefix Client
  npm run styles:build --prefix Client
  npm run js:build --prefix Client
  npm test --prefix Client
fi

if [[ "$phase" != node ]]; then
  if [[ -z "$PULSARTMS_RELEASE_DIR" ]]; then
    echo "PULSARTMS_RELEASE_DIR must identify the already-published artifact." >&2
    exit 2
  fi
  if [[ "$PULSARTMS_RELEASE_DIR" != /* ]]; then PULSARTMS_RELEASE_DIR="$PWD/$PULSARTMS_RELEASE_DIR"; fi
  export PULSARTMS_RELEASE_DIR
fi

if [[ "$phase" == all || "$phase" == dotnet ]]; then
  if [[ -e "$PULSARTMS_RELEASE_DIR/publish" ]]; then
    echo "Release publish directory must be new: $PULSARTMS_RELEASE_DIR/publish" >&2
    exit 2
  fi
  dotnet build pulsartms.slnx -c Release -warnaserror -p:SkipStyleBuild=true -p:SkipJavaScriptBuild=true
  dotnet test pulsartms.slnx -c Release --no-build
  dotnet publish Client/Client.csproj -c Release -warnaserror --no-restore \
    -p:SkipStyleBuild=true -p:SkipJavaScriptBuild=true -o "$PULSARTMS_RELEASE_DIR/publish"
fi

if [[ "$phase" == all || "$phase" == artifact ]]; then
  node Client/build/verifyRelease.mjs "$PULSARTMS_RELEASE_DIR/publish"
  if [[ "$PULSARTMS_RELEASE_UI" == 1 ]]; then
    MAP_TEST_ARTIFACT_DIR="$PULSARTMS_RELEASE_DIR/publish/wwwroot" npm run test:ui --prefix Client
  else
    echo "Offline UI smoke not run; opt in with PULSARTMS_RELEASE_UI=1 and Playwright Chromium installed."
  fi
  if [[ "$PULSARTMS_RELEASE_BROWSER" == 1 ]]; then
    MAP_TEST_ARTIFACT_DIR="$PULSARTMS_RELEASE_DIR/publish/wwwroot" npm run test:browser --prefix Client
  else
    echo "Browser verification not run; opt in with PULSARTMS_RELEASE_BROWSER=1 and a local API/client origin."
  fi
  echo "Verified Client artifact: $PULSARTMS_RELEASE_DIR/publish/wwwroot"
fi
