#!/bin/bash

set -e

if [ -z "$1" ]; then
    echo "Usage: ./migrate.sh MigrationName"
    exit 1
fi

MIGRATION_NAME="$1"

echo "Creating migration: $MIGRATION_NAME"

dotnet ef migrations add "$MIGRATION_NAME" \
    --project Server/Infrastructure \
    --startup-project Server/API

# Creating a migration no longer applies it (audit F27): the API's
# configured database may not be a disposable one. Review it, then apply
# it explicitly as docs/development/setup.md describes.
echo "Created. Review it, then apply it explicitly (docs/development/setup.md)."
