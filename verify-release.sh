#!/bin/bash

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

phase="${1:-all}"
case "$phase" in all|node|dotnet|artifact) ;; *) echo "Usage: bash verify-release.sh [all|node|dotnet|artifact]" >&2; exit 2 ;; esac
if [[ $# -gt 1 ]]; then echo "Unexpected release gate arguments." >&2; exit 2; fi

if [[ ( "$phase" == all || "$phase" == dotnet ) && -z "${AMFTMS_RELEASE_DIR:-}" ]]; then
  exec node scripts/artifacts.mjs run release -- env 'AMFTMS_RELEASE_DIR={artifacts}' bash verify-release.sh "$phase"
fi

if [[ "$phase" == all || "$phase" == node ]]; then
  npm ci --prefix Client
  npm run js:check --prefix Client
  npm run styles:build --prefix Client
  npm run js:build --prefix Client
  npm test --prefix Client
fi

if [[ "$phase" != node ]]; then
  if [[ -z "${AMFTMS_RELEASE_DIR:-}" ]]; then
    echo "AMFTMS_RELEASE_DIR must identify the already-published artifact." >&2
    exit 2
  fi
  if [[ "$AMFTMS_RELEASE_DIR" != /* ]]; then AMFTMS_RELEASE_DIR="$PWD/$AMFTMS_RELEASE_DIR"; fi
  export AMFTMS_RELEASE_DIR
fi

if [[ "$phase" == all || "$phase" == dotnet ]]; then
  if [[ -e "$AMFTMS_RELEASE_DIR/publish" ]]; then
    echo "Release publish directory must be new: $AMFTMS_RELEASE_DIR/publish" >&2
    exit 2
  fi
  dotnet build AMFTMS.slnx -c Release -warnaserror -p:SkipStyleBuild=true -p:SkipJavaScriptBuild=true
  dotnet test AMFTMS.slnx -c Release --no-build
  dotnet publish Client/Client.csproj -c Release -warnaserror --no-restore \
    -p:SkipStyleBuild=true -p:SkipJavaScriptBuild=true -o "$AMFTMS_RELEASE_DIR/publish"
fi

if [[ "$phase" == all || "$phase" == artifact ]]; then
  node Client/build/verifyRelease.mjs "$AMFTMS_RELEASE_DIR/publish"
  if [[ "${AMFTMS_RELEASE_UI:-0}" == 1 ]]; then
    MAP_TEST_ARTIFACT_DIR="$AMFTMS_RELEASE_DIR/publish/wwwroot" npm run test:ui --prefix Client
  else
    echo "Offline UI smoke not run; opt in with AMFTMS_RELEASE_UI=1 and Playwright Chromium installed."
  fi
  if [[ "${AMFTMS_RELEASE_BROWSER:-0}" == 1 ]]; then
    MAP_TEST_ARTIFACT_DIR="$AMFTMS_RELEASE_DIR/publish/wwwroot" npm run test:browser --prefix Client
  else
    echo "Browser verification not run; opt in with AMFTMS_RELEASE_BROWSER=1 and a local API/client origin."
  fi
  echo "Verified Client artifact: $AMFTMS_RELEASE_DIR/publish/wwwroot"
fi
