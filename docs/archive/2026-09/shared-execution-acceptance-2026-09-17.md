# Shared execution acceptance — September 17, 2026

ExecutionAcceptance now owns accepted stop replacement, revision advancement,
immutable history and durable planning demand for initial assignment, imports,
manual corrections and the complete transfer lifecycle. Commands retain their
resource, custody, status and independent confirmation rules in the same
transaction. Transfer boundary source references are applied before recording
history. New transfer legs start at revision one, matching ordinary acceptance.
An existing leg still advances once per accepted command.

Transfer splitting previously replaced accepted stops without the shared
movement-path guard. It now rejects a split across recorded/manual mileage,
retires obsolete automatic planned mileage and retains unaffected adjacent paths.
Cancellation uses the same retirement owner. Source-review preview uses the same
path protection as acceptance, including evidence spanning changed visits.
Three architecture checks prevent separate stop/history/planning writers from
being added to Application.

## Verification

- Full suite: 2,699 Server, 1,014 Client C# and 561 JavaScript tests: 4,274 passed.
  Evidence: `artifacts/managed/diagnostic-S6Q5GJ/tests.log`.
- Strict migration-probe build: zero warnings/errors.
  Evidence: `artifacts/managed/diagnostic-JqlZh1/build.log`.
- Isolated PostgreSQL transition passed with new transfer command probes.
  Evidence: `artifacts/managed/diagnostic-f90lnU/postgresql.log`.
  The probe removed its isolated fixture.

Transfer probes cover planning, cancellation, separate release and receipt,
unknown actual times and persisted replay. Each accepted revision has one
history record and planning request. The split regression covers both protected
actual mileage and replaceable planned mileage, retaining an unaffected path.
Existing history tests now expect revision one for new outgoing legs and compare
later revisions against the accepted revision instead of an implicit bootstrap
increment. Replay, cancellation and historical-content assertions remain intact.

## Boundaries

This unifies the acceptance writer; it does not infer ambiguous imported resource
boundaries, remove all mutable calculation adapters or replace the conservative
publication guard. Those gates remain in the core specification. No deployment,
working-database migration or authenticated browser check was performed in this
slice. No production throughput or contention claim was measured.
