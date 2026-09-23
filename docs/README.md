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

## Design

- [PulsR TMS brand design](design/brand-design.md) is the authoritative visual
  identity guide. Follow it alongside UI controls for new and changed product UI.
- [Visual brand guide](../Client/wwwroot/brand/index.html) uses the shared artwork
  and compiled tokens; open `/brand/index.html` in the running client.

## Development and architecture

- [Fleet read ownership and efficiency rules](architecture/fleet-efficiency.md)
- [Core rebuild specification](architecture/core-rebuild.md) and
  [acceptance scenarios](architecture/core-rebuild-scenarios.md) define the
  staged replacement plan; they do not describe deployed functionality.
- [Customs data preparation](architecture/customs-preparation.md) defines the
  planned shipment, crossing and fleet data for future ACE/ACI manifests.
- [Costs, and separating the order from the work][costs] decides how an
  expense, its attribution to a load and the commercial order are recorded.
- [How mileage is attributed to a load](architecture/mileage-allocation.md)
  records the existing attribution mechanism and what it cannot express.
- [Module ownership and save boundaries](architecture/module-ownership.md)
  records measured coupling, table owners and transaction boundaries.
- [Build, run and development workflow](development/setup.md)
- [PulsR product naming and compatibility](development/product-branding.md)
- [Temporary outputs and automatic retention](development/artifact-retention.md)
- [JavaScript ownership and boundaries](architecture/javascript.md)
- [SCSS tokens, themes and ownership](architecture/styles.md)
- [Fleet Map component responsibilities](architecture/fleet-map-client.md)
- [SaaS evolution plan: retain TorqueAI and migrate incrementally](architecture/saas-evolution-plan.md)
- [Driver messaging inside PulsR (plan, not implemented)](architecture/driver-messaging-plan.md)
- [Fleet configuration and dispatch execution][dispatch-execution]
  covers Trucks, Trailers, Drivers and Switch; settlement design is deferred.

[costs]: architecture/costs-and-commercial-work.md
[dispatch-execution]: architecture/dispatch-execution-and-settlements.md

## Features

- [Optional load imports and provider independence](features/dispatch-import.md)
- [Dispatch load editor, activity and documents](features/dispatch-workspace.md)
- [Shipments and independent Border preparation](features/border-preparation.md)
- [Trucks, Trailers and Drivers configuration][fleet-configuration]
- [Operational mileage and load attribution](features/mileage-attribution.md)
- [Integration credentials in Settings](features/integration-settings.md)
- [Load numbering and optional prefixes](features/load-numbering.md)
- [Route planning](features/route-planning.md), [saved base routes](features/base-routes.md),
  [empty mileage](features/dispatch-deadhead.md), and [access warnings](features/route-access-warnings.md)
- [Verified stop addresses](features/verified-stop-addresses.md)
- [ETA forecasting](features/eta-service.md) and [HOS/ETA rules](features/hos-eta-planning.md)
- [Fuel selection rules](features/fuel-planning-rules.md) and [onward fuel planning](features/fuel-regions.md)
- [Synchronization](features/synchronization.md) and [truck cameras](features/truck-camera.md)
- [Truck weather](features/truck-weather.md)

[fleet-configuration]: features/fleet-resource-configuration.md

## Operations

- [Release verification and deployment](operations/release.md)
- [Verifying that a backup can be restored](operations/restore-verification.md)
- [Core storage cutover](operations/core-storage-cutover.md)
- [Diagnostics and logging](operations/diagnostics.md)
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
