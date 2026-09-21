# Completed loads retained by source review

Read-only production inspection confirmed AMF1370, AMF1379 and AMF1383 had
recorded final deliveries on September 12, 13 and 16 respectively. All retained
the imported header status `sent` and an assignment review notice, without an
accepted execution leg. Their actual visits were chronologically ordered.

`ExecutionWorkReader` treated any source review as unconditional membership in
remaining work. Consequently, delivery-based completion on a card did not agree
with active-board selection. Review now relaxes the overdue-date filter instead
of bypassing completion. Conflicting actual chronology remains explicit review
evidence; the existing import tests continue to require those unsafe inputs to
remain visible without becoming accepted planning authority.

The Completed query also omitted recorded deliveries whose imported header had
not changed to `completed`. It now includes recorded final delivery/departure
facts, respecting an explicit completion override of false. No provider status,
review notice or execution history is rewritten by either read path.

Regression tests cover completed and cancelled reviewed sources, unfinished
overdue review, recorded delivery with a `sent` header, reopened delivery and
non-delivery stops. The existing archive fixture now clears its delivery actual
when representing active work; changing only its header did not make it active.

The changes are local and require no migration. Production was inspected only;
no release or data repair was performed. Browser rendering was not exercised.

`bash test.sh dispatch fuel synchronization` passed 2,501 server, 692 Client C#
and 64 JavaScript tests, including dependent routing and architecture checks.
This was not a full-suite run. Evidence:
`artifacts/managed/diagnostic-Y9CA8k/check.log`.
