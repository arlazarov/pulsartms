#!/bin/bash

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

PULSARTMS_RELEASE_DIR="${PULSARTMS_RELEASE_DIR-${AMFTMS_RELEASE_DIR:-}}"
if [[ -z "$PULSARTMS_RELEASE_DIR" ]]; then
  exec node scripts/artifacts.mjs run release -- env 'PULSARTMS_RELEASE_DIR={artifacts}' bash deploy-client.sh
fi
export PULSARTMS_RELEASE_DIR
bash verify-release.sh

echo "Deploying Firebase Hosting..."

firebase deploy --only hosting --public "$PULSARTMS_RELEASE_DIR/publish/wwwroot"

echo "Done."
