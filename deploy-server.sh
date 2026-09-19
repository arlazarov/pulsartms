#!/bin/bash

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

echo "Building API image..."

build_id="$(gcloud builds submit \
  --config cloudbuild.yaml \
  --suppress-logs \
  --format='value(id)' \
  .)"
if [[ ! "$build_id" =~ ^[a-zA-Z0-9-]+$ ]]; then
  echo "Cloud Build did not return a valid build ID; deployment stopped." >&2
  exit 1
fi
image_repository=us-east4-docker.pkg.dev/amftms/amftms/api
image_result="$(gcloud builds describe "$build_id" --format='value(status,results.images[0].name,results.images[0].digest)')"
IFS=$'\t' read -r build_status image_name image_digest <<< "$image_result"
if [[ "$build_status" != SUCCESS || "$image_name" != "$image_repository:$build_id" || ! "$image_digest" =~ ^sha256:[a-f0-9]{64}$ ]]; then
  echo "Cloud Build result does not identify the successful expected image; deployment stopped." >&2
  exit 1
fi

echo "Deploying API to Cloud Run..."

deploy_args=(
  --image "$image_repository@$image_digest"
  --region us-east4
  --port 8080
  --min 1
  --max-instances 1
  --no-cpu-throttling
  # Held telemetry polls (wait=) each occupy a request slot; keep this above the open-tab count.
  --concurrency 250
)
if [[ -n "${AMFTMS_DEPLOY_ENV_FILE:-}" ]]; then
  deploy_args+=(--env-vars-file "$AMFTMS_DEPLOY_ENV_FILE")
fi

gcloud run deploy amftms-api "${deploy_args[@]}"

echo "Deployed build $build_id: $image_repository@$image_digest"
