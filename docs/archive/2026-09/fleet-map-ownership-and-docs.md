# Documentation and Fleet Map ownership — 2026-09-08

Approved scope: separate maintained documentation from historical reports and
extract independent responsibilities from Fleet Map. This is not a server rewrite,
database operation or deployment.

## Changes

- Added a documentation entry point and archive index. Maintained guides are
  grouped by architecture, development, features and operations. The three stable
  policy entry points remain at the documentation root. Updated inbound and
  relative links; retained dated reports and raw measurement files in the archive.
- Separated release history from release instructions. Preserved earlier route
  and fuel-region guides as historical records, replacing contradictory automatic
  fuel descriptions in maintained guides with the checked manual-calculation flow.
- Extracted component-owned `FleetMapSession<T>` for module/map/callback ownership
  and `FleetStationLayer` for station requests, cancellation and loaded-date/error
  state. The page retains route/selection coordination and Razor state. No new DI
  registrations, endpoints, DTO changes or generic service layer were added.
- Kept Next Loads identity/revision checks, cache limits, polling cadence and
  display-only ETA retention unchanged. Station publication uses the current IFTA
  toggle even if it changed while HTTP was pending. Component teardown releases
  session resources in a finally block.

## Verification

The initial affected `fleet` group passed: 121 Server, 52 Client C# and 81 Node
checks. After the final test additions, the complete offline release gate passed:
514 Server, 111 Client C# and 99 Node tests (724 total), strict Release solution
build and publish with zero warnings/errors, 222 staged asset hashes, six JS
entry-point graphs and all 40 offline UI smoke cases.

Fourteen new regressions cover shared startup, import retry, partial-map cleanup,
disposal during import/creation, station failure/retry, date-change cancellation,
late replies, hide/dispose/lifetime cancellation, pending publication, current IFTA
selection and actual component toggle reuse. They use fake HTTP/JS, not local
SQL containers or provider requests.

Verified artifact: `artifacts/release.tnhBk0/publish/wwwroot`.
The offline UI smoke uses fixture APIs and a substitute map provider. Authenticated
live-map/browser-provider verification was not run in this change. Production
performance, GPU behavior and memory under prolonged use are not established by
these checks. Native `wasm-tools` optimization was not installed.

No deployment was performed. Existing production and local Debug processes were
not restarted; the staged Release artifact contains this change.

## Authorized deployment follow-up

The user subsequently requested deployment. Firebase published the exact verified
`artifacts/release.p0Quqi/publish/wwwroot` after the complete 724-test gate and
40 offline UI cases. Live release `1788870524393000`, finalized version
`f1c79031d5f32ae6`, was released at 2026-09-08 12:28:44 UTC. The custom-domain
index and 68 JS/WASM/CSS resources matched staged SHA-256 bytes and non-HTML asset
MIME types. A fresh production browser reached Login without warning/error logs;
an authenticated production map interaction was not performed. The temporary
verification tab was closed.

Cloud Build `b562efc3-242d-40af-b2ad-02acd660f16c` failed its test gate, so
`deploy-server.sh` did not deploy a server revision. The observed failure was
`MapRoutePublisherTests.LatestSelectionIsTheFinalPublishedPayload`: its scheduling
assumption required one publication even when the first completed before the
second began. The test now explicitly holds the first interop acknowledgement,
publishes the new selection, releases the old acknowledgement and verifies that
subsequent metadata-only publication still uses the new geometry identity.
No production source, assertion policy or release gate was relaxed for this fix.

All ten publisher tests passed in 15 consecutive local runs. The full gate then
passed again (514 Server, 111 Client C#, 99 Node, 40 offline UI cases; zero build
warnings/errors). Its artifact `artifacts/release.O3IgnQ/publish/wwwroot` matches
all 222 files of the already deployed Client artifact byte-for-byte.

Retrying Google Cloud commands was blocked by an explicit reauthentication error.
The user was asked to run `gcloud auth login`; no credentials or authorization
codes were requested in chat. The last verified API revision before the attempt
was `amftms-api-00077-bgr` with 100% traffic. Custom/direct liveness subsequently
returned 200; anonymous readiness and stations returned 401. A server retry and
post-deployment revision verification remain pending successful reauthentication.
No local SQL database container was started.
