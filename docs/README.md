# Documentation

Start with the maintained guides below. Historical audits and measurements are
evidence from a particular date, not a list of current defects or current operating
instructions. Source code and executable checks must be consulted when verifying
that a behavior still matches its guide.

## Project rules

The stable entry points remain here because project instructions and reviews use
them directly: [architecture](ARCHITECTURE.md), [test selection](testing.md), and
[UI controls](ui-controls.md). The mandatory working rules are in
[AGENTS.md](../AGENTS.md).

## Development and architecture

- [Build, run and development workflow](development/setup.md)
- [Temporary outputs and automatic retention](development/artifact-retention.md)
- [JavaScript ownership and boundaries](architecture/javascript.md)
- [SCSS tokens, themes and ownership](architecture/styles.md)
- [Fleet Map component responsibilities](architecture/fleet-map-client.md)

## Features

- [Integration credentials in Settings](features/integration-settings.md)
- [Load numbering and optional prefixes](features/load-numbering.md)
- [Route planning](features/route-planning.md), [saved base routes](features/base-routes.md),
  [empty mileage](features/dispatch-deadhead.md), and [access warnings](features/route-access-warnings.md)
- [Verified stop addresses](features/verified-stop-addresses.md)
- [ETA forecasting](features/eta-service.md) and [HOS/ETA rules](features/hos-eta-planning.md)
- [Fuel selection rules](features/fuel-planning-rules.md) and [onward fuel planning](features/fuel-regions.md)
- [Synchronization](features/synchronization.md) and [truck cameras](features/truck-camera.md)

## Operations

- [Release verification and deployment](operations/release.md)
- [Diagnostics and logging](operations/diagnostics.md)
- [Load testing](operations/load-testing.md)
- [Security rollout and recovery checks](operations/security-rollout.md)
- [Gmail watch ownership and recovery](operations/gmail-watch.md)

## Historical evidence

[Archive index](archive/README.md) groups dated audits, measurements, prototypes
and implementation/release records. Retain their limitations and superseded
findings; do not rewrite an old test result to imply a newer release was tested.

## Maintenance

Update the owning guide when changing behavior. Add dated results to the archive,
link them from its index, and distinguish observations from unmeasured claims.
Keep feature rules out of audit diaries and do not create duplicate authoritative
guides. When moving a document, update inbound links and its relative links.
