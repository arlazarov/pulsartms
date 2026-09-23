using Application.Diagnostics.Consistency;
using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetConsistencyAuditQuery
  : IRequest<RequestResponse<ConsistencyAuditReport>>;

public sealed record GetConsistencyEventsQuery(
  long After,
  int Limit,
  string? Kind
) : IRequest<RequestResponse<ConsistencyEventPage>>;

public sealed record GetConsistencyIncidentsQuery(Guid? After, int Limit)
  : IRequest<RequestResponse<ConsistencyIncidentPage>>;

public sealed record RunConsistencyAuditCommand
  : IRequest<RequestResponse<ConsistencyAuditRun>>;

public sealed record ConsistencyEventView(
  long Sequence,
  Guid FindingId,
  string Kind,
  string Rule,
  string EntityKey,
  int Occurrence,
  Guid PassId,
  DateTime At,
  string Detail
);

// Next is the last sequence returned (or After when none): resume from it
// after processing the page. Sequences commit in order, so no later-committed
// event can appear behind Next; re-reading a page is safe. HasMore says
// whether this read found more committed events than Limit.
public sealed record ConsistencyEventPage(
  IReadOnlyList<ConsistencyEventView> Events,
  long Next,
  bool HasMore
);

public sealed record ConsistencyIncidentView(
  Guid Id,
  string Rule,
  string EntityKey,
  string Reason,
  int Recurrences,
  Guid LastFindingId,
  DateTime OpenedAt,
  DateTime LastAt
);

// Incidents in Id order: a complete listing of the rows that exist when read.
// An incident updated after it was listed is not listed again; its updates
// arrive in the event feed as incident-opened and incident-updated.
public sealed record ConsistencyIncidentPage(
  IReadOnlyList<ConsistencyIncidentView> Incidents,
  Guid? Next,
  bool HasMore
);

public sealed record ConsistencyAuditRun(
  ConsistencyAuditor.PassResult Pass,
  ConsistencyAuditReport Report
);

public sealed class ConsistencyAuditHandlers(
  ConsistencyAuditor auditor,
  ConsistencyJournalReads journal,
  IEnumerable<IConsistencyRule> rules,
  ICurrentCompany company
)
  : IRequestHandler<
      GetConsistencyAuditQuery,
      RequestResponse<ConsistencyAuditReport>
    >,
    IRequestHandler<
      GetConsistencyEventsQuery,
      RequestResponse<ConsistencyEventPage>
    >,
    IRequestHandler<
      GetConsistencyIncidentsQuery,
      RequestResponse<ConsistencyIncidentPage>
    >,
    IRequestHandler<
      RunConsistencyAuditCommand,
      RequestResponse<ConsistencyAuditRun>
    >
{
  public const int MaxLimit = 500;

  public async Task<RequestResponse<ConsistencyAuditReport>> Handle(
    GetConsistencyAuditQuery request,
    CancellationToken ct
  ) =>
    company.Id is not { } id
      ? RequestResponse<ConsistencyAuditReport>.Fail("Access denied.", 403)
      : RequestResponse<ConsistencyAuditReport>.Ok(
        await auditor.ReportAsync(id, rules.Select(x => x.Info), journal, ct)
      );

  public async Task<RequestResponse<ConsistencyEventPage>> Handle(
    GetConsistencyEventsQuery request,
    CancellationToken ct
  )
  {
    if (company.Id is not { } id)
      return RequestResponse<ConsistencyEventPage>.Fail("Access denied.", 403);
    if (request.After < 0 || request.Limit is < 1 or > MaxLimit)
      return RequestResponse<ConsistencyEventPage>.Fail(
        $"After must be non-negative and limit between 1 and {MaxLimit}."
      );
    var events = await journal.EventsAsync(
      id,
      request.After,
      request.Limit,
      string.IsNullOrWhiteSpace(request.Kind) ? null : request.Kind.Trim(),
      ct
    );
    var page = events.Take(request.Limit).ToList();
    return RequestResponse<ConsistencyEventPage>.Ok(
      new(
        [
          .. page.Select(x => new ConsistencyEventView(
            x.Sequence,
            x.FindingId,
            x.Kind,
            x.Rule,
            x.EntityKey,
            x.Occurrence,
            x.PassId,
            x.At,
            x.DetailJson
          )),
        ],
        page.Count == 0 ? request.After : page[^1].Sequence,
        events.Count > request.Limit
      )
    );
  }

  public async Task<RequestResponse<ConsistencyIncidentPage>> Handle(
    GetConsistencyIncidentsQuery request,
    CancellationToken ct
  )
  {
    if (company.Id is not { } id)
      return RequestResponse<ConsistencyIncidentPage>.Fail(
        "Access denied.",
        403
      );
    if (request.Limit is < 1 or > MaxLimit)
      return RequestResponse<ConsistencyIncidentPage>.Fail(
        $"Limit must be between 1 and {MaxLimit}."
      );
    var incidents = await journal.IncidentsAsync(
      id,
      request.After,
      request.Limit,
      ct
    );
    var page = incidents.Take(request.Limit).ToList();
    return RequestResponse<ConsistencyIncidentPage>.Ok(
      new(
        [
          .. page.Select(x => new ConsistencyIncidentView(
            x.Id,
            x.Rule,
            x.EntityKey,
            x.Reason,
            x.Recurrences,
            x.LastFindingId,
            x.OpenedAt,
            x.LastAt
          )),
        ],
        page.Count == 0 ? request.After : page[^1].Id,
        incidents.Count > request.Limit
      )
    );
  }

  public async Task<RequestResponse<ConsistencyAuditRun>> Handle(
    RunConsistencyAuditCommand request,
    CancellationToken ct
  )
  {
    if (company.Id is not { } id)
      return RequestResponse<ConsistencyAuditRun>.Fail("Access denied.", 403);
    var pass = await auditor.RunAsync(id, ct);
    return RequestResponse<ConsistencyAuditRun>.Ok(
      new(
        pass,
        await auditor.ReportAsync(id, rules.Select(x => x.Info), journal, ct)
      )
    );
  }
}
