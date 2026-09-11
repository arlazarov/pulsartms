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

echo "Updating database..."

dotnet ef database update \
    --project Server/Infrastructure \
    --startup-project Server/API

echo "Migration completed."
