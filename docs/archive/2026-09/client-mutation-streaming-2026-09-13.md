# Client mutation response streaming

## Scope

`Client/Services/ApiService.cs` owns the shared HTTP response handling. GET and
POST already requested headers-first responses, but PUT, PATCH and DELETE used
convenience methods that buffer the entire body before response handling starts.
Those three methods now request headers-first responses too, retaining the same
JSON request body, cancellation token, status mapping and response disposal.

The existing responsive JSON reader consumes successful response streams. This
removes an avoidable full-body buffering stage for mutation responses, including
returned route state, without changing their values or visibility. Error-body
handling is unchanged. No cache, polling interval, request count, server endpoint,
database schema or business calculation changes in this pass.

## Evidence and limits

The added `ApiResponseStreamingTests` source covers all five HTTP methods,
successful payload/status handling, request bodies, streaming rather than
buffering, and response content disposal. It belongs to Category Identity and
Kind Unit. Tests were not run, following the operator's execution preference.

The isolated Client and Client.Tests builds completed with zero warnings and
errors; the latter compiled regression sources without executing tests. Neither
replaced the running development server's framework output. No browser checks,
production traffic experiments, heap measurements or latency measurements were
performed. No quantitative or production performance improvement is claimed.
No deployment or migration was performed.

Potential coalescing of concurrent fuel-station requests was not introduced:
the page explicitly cancels their owner when dates or visibility change, and
inspector callbacks share that path. Altering that ownership needs a separate
race-focused change; it must not reuse a request for a different selected date or
weaken cancellation and current-owner checks.
