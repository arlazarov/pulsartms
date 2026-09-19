#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ $# -eq 0 ]]; then set -- all; fi
filter='Category=Architecture'
all=false
map=false
styles=false
identity=false
dispatch=false

for category in "$@"; do
  case "$category" in
    all) all=true ;;
    map) filter+='|Category=Fleet'; map=true ;;
    styles) styles=true ;;
    architecture) ;;
    addresses) filter+='|Category=Addresses|Category=Routing|Category=Finance' ;;
    costs) filter+='|Category=Costs|Category=Finance' ;;
    routing|finance|eta) filter+='|Category=Routing|Category=Finance|Category=Eta|Category=Addresses' ;;
    fleet) filter+='|Category=Fleet|Category=Eta'; map=true ;;
    dispatch) filter+='|Category=Dispatch|Category=Finance|Category=Routing'; dispatch=true ;;
    fuel) filter+='|Category=Fuel|Category=Routing' ;;
    identity) filter+='|Category=Identity'; identity=true ;;
    caching) filter+='|Category=Caching' ;;
    database) filter+='|Category=Database' ;;
    synchronization) filter+='|Category=Synchronization|Category=Caching|Category=Dispatch|Category=Addresses'; dispatch=true ;;
    *) echo 'Usage: bash test.sh [all|map|styles|architecture|addresses|costs|routing|finance|eta|fleet|dispatch|fuel|identity|caching|database|synchronization] ...' >&2; exit 2 ;;
  esac
done

node scripts/artifacts.mjs prune --apply

dotnet_args=(pulsartms.slnx -warnaserror -p:UseSharedCompilation=false --artifacts-path "$PWD/artifacts/tests")
if ! $all; then dotnet_args+=(--filter "$filter"); fi
dotnet test "${dotnet_args[@]}"

if $all; then
  npm test --prefix Client
else
  if $map; then npm run test:map --prefix Client; fi
  if $styles; then npm run test:styles --prefix Client; fi
  if $identity; then npm run test:identity --prefix Client; fi
  if $dispatch; then npm run test:dispatch --prefix Client; fi
  npm run test:architecture --prefix Client
fi
