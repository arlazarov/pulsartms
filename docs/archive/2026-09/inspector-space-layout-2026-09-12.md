# Truck inspector space layout

The header now owns driver identity and the existing truck actions. Wide truck
bodies place telemetry, unchanged HOS clocks and GPS in adjacent columns. The
route summary groups load/order, distance, next visit/appointment and ETA in four
columns; narrower inspectors retain wrapping and scrolling. GPS remains a
copyable address. Details adds information without moving the primary groups;
the disclosure label reserves equal space for Details and Hide.

Verification: `bash test.sh fleet styles` passed, including 290 Server and 215
Client tests and the required map, styles and architecture JavaScript checks.
Strict Debug Client build and isolated Release Client publish passed. The Fleet
browser fixture passed ten width/theme cases, including increased root text and
stable disclosure geometry. Desktop and mobile screenshots were inspected.

Browser evidence: `artifacts/managed/browser-hours-forecast-T4UNCv/report.json`.
Staged Client: `artifacts/managed/scratch-6vp9rt/publish/wwwroot`. These are managed
temporary outputs subject to retention. APIs and the map provider were stubbed;
this is not a live integration or production performance check. No full release
gate or PostgreSQL checks were run for this Client-only layout change.

The localhost Debug Client was rebuilt and restarted on port 5067. Production
was not deployed. This change requires no migration.
