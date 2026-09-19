#!/bin/bash

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

PULSARTMS_DEPLOY_ENV_FILE="${PULSARTMS_DEPLOY_ENV_FILE-${AMFTMS_DEPLOY_ENV_FILE:-}}"
project=amftms
service=amftms-api
region=us-east4
command -v node >/dev/null

echo "Building API image..."

build_id="$(gcloud builds submit \
  --project "$project" \
  --config cloudbuild.yaml \
  --suppress-logs \
  --format='value(id)' \
  .)"
if [[ ! "$build_id" =~ ^[a-z0-9]+(-[a-z0-9]+)*$ ||
  ${#build_id} -gt 50 ]]; then
  echo "Cloud Build did not return a valid build ID; deployment stopped." >&2
  exit 1
fi
image_repository=us-east4-docker.pkg.dev/amftms/amftms/api
image_result="$(gcloud builds describe "$build_id" \
  --project "$project" \
  --format='value(status,results.images[0].name,results.images[0].digest)')"
IFS=$'\t' read -r build_status image_name image_digest <<< "$image_result"
if [[ "$build_status" != SUCCESS ||
  "$image_name" != "$image_repository:$build_id" ||
  ! "$image_digest" =~ ^sha256:[a-f0-9]{64}$ ]]; then
  echo "Cloud Build result does not identify the expected successful image." >&2
  exit 1
fi
image="$image_repository@$image_digest"
revision="$service-b-$build_id"

echo "Deploying API to Cloud Run..."

deploy_args=(
  --image "$image"
  --project "$project"
  --region "$region"
  --revision-suffix "b-$build_id"
  --no-traffic
  --quiet
  --format=none
  --port 8080
  --min 1
  # Still one instance, but no longer for the reason recorded here before.
  # Read caches now agree: CacheInvalidationRelay carries invalidations
  # between instances through the database, and RouteDisplayCache keys on the
  # same generations, so it follows. What is untested is actually running two
  # of them - the background operations assume they are alone unless
  # BackgroundOperations:Roles names which instance runs them. Raise this
  # after a deliberate two-instance run, not before.
  # See docs/architecture/module-ownership.md.
  --max-instances 1
  --no-cpu-throttling
  # Nothing was watching this instance. When it stopped, synchronization
  # stopped with it and the fault surfaced seven and a half hours later as a
  # missing fuel stop. /api/health/live now fails when a background loop this
  # instance was told to run has gone quiet, and this is what asks it.
  #
  # Deliberately slow to act: four minutes before the first question, then
  # five failures a minute apart. The check itself already allows ten cycles
  # of silence. A restart loop would be worse than the stall it replaces.
  --liveness-probe=httpGet.path=/api/health/live,initialDelaySeconds=240,periodSeconds=60,timeoutSeconds=10,failureThreshold=5
)
if [[ -n "$PULSARTMS_DEPLOY_ENV_FILE" ]]; then
  deploy_args+=(--env-vars-file "$PULSARTMS_DEPLOY_ENV_FILE")
fi

gcloud run deploy "$service" "${deploy_args[@]}"

revision_fields='metadata.name,spec.containers[0].image'
revision_fields+=',status.imageDigest,status.conditions'
gcloud run revisions describe "$revision" \
  --project "$project" --region "$region" \
  --format="json($revision_fields)" |
  node scripts/cloud-run-release.mjs revision "$revision" "$image"

echo "Routing all traffic to verified revision $revision..."
gcloud run services update-traffic "$service" \
  --project "$project" --region "$region" \
  --to-revisions="$revision=100" --quiet --format=none

traffic_fields='metadata.name,metadata.generation,spec.traffic'
traffic_fields+=',status.observedGeneration,status.conditions,status.traffic'
gcloud run services describe "$service" \
  --project "$project" --region "$region" \
  --format="json($traffic_fields)" |
  node scripts/cloud-run-release.mjs traffic "$service" "$revision"

echo "Deployed build $build_id: $image"
echo "Verified 100% traffic on revision $revision."
