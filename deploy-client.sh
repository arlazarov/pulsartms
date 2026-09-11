#!/bin/bash

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

if [[ -z "${AMFTMS_RELEASE_DIR:-}" ]]; then
  exec node scripts/artifacts.mjs run release -- env 'AMFTMS_RELEASE_DIR={artifacts}' bash deploy-client.sh
fi
export AMFTMS_RELEASE_DIR
bash verify-release.sh

echo "Deploying Firebase Hosting..."

firebase deploy --only hosting --public "$AMFTMS_RELEASE_DIR/publish/wwwroot"

echo "Done."
