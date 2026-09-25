using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Commands;

// An administrator records the templates Meta approved for the number the
// company sends from now, and withdraws them. Nothing here asks Meta; a
// template recorded that Meta did not approve is refused by it when sent.
public sealed record GetApprovedTemplatesQuery
  : IRequest<RequestResponse<ApprovedTemplatesView>>;

public sealed record ApproveTemplateCommand(
  string Name,
  string Language,
  int Parameters,
  string Text
) : IRequest<RequestResponse<ApprovedTemplateView>>;

public sealed record WithdrawTemplateCommand(Guid Id)
  : IRequest<RequestResponse<bool>>;

// BusinessNumberId: the number the company sends from now, or null when
// messaging is not configured and nothing can be recorded. Pulsr: the
// templates PulsR itself uses, what to submit to Meta for each, and
// whether it is recorded for this number exactly as defined.
public sealed record ApprovedTemplatesView(
  string? BusinessNumberId,
  IReadOnlyList<ApprovedTemplateView> Templates,
  IReadOnlyList<PulsrTemplateView> Pulsr
);

public sealed record PulsrTemplateView(
  string Purpose,
  string Name,
  string Language,
  int Parameters,
  string Body,
  string Submission,
  string? Unsupported,
  bool Recorded
);

public sealed record ApprovedTemplateView(
  Guid Id,
  string Name,
  string Language,
  int Parameters,
  string Text
);

public sealed class ApprovedTemplateHandlers(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  IDriverMessaging messaging,
  ApprovedTemplates templates,
  TimeProvider clock
)
  : IRequestHandler<
    GetApprovedTemplatesQuery,
    RequestResponse<ApprovedTemplatesView>
  >,
    IRequestHandler<
      ApproveTemplateCommand,
      RequestResponse<ApprovedTemplateView>
    >,
    IRequestHandler<WithdrawTemplateCommand, RequestResponse<bool>>
{
  public async Task<RequestResponse<ApprovedTemplatesView>> Handle(
    GetApprovedTemplatesQuery request,
    CancellationToken ct
  )
  {
    if (await MessagingAdmin.UserAsync(db, caller, roles, ct) is null)
      return RequestResponse<ApprovedTemplatesView>.Fail(
        MessagingAdmin.Required,
        403
      );
    var number = await messaging.BusinessNumberAsync(ct);
    var current = await templates.CurrentAsync(ct);
    var recorded = current
      .Select(PulsrTemplates.Matching)
      .OfType<PulsrTemplate>()
      .Select(x => x.Purpose)
      .ToHashSet();
    return RequestResponse<ApprovedTemplatesView>.Ok(
      new(
        number,
        [.. current.Select(View)],
        [
          .. PulsrTemplates.All.Select(x => new PulsrTemplateView(
            x.Purpose,
            x.Name,
            x.Language,
            x.Examples.Count,
            x.Body,
            x.Submission(),
            x.Unsupported,
            recorded.Contains(x.Purpose)
          )),
        ]
      )
    );
  }

  public async Task<RequestResponse<ApprovedTemplateView>> Handle(
    ApproveTemplateCommand request,
    CancellationToken ct
  )
  {
    if (await MessagingAdmin.UserAsync(db, caller, roles, ct) is not { } user)
      return RequestResponse<ApprovedTemplateView>.Fail(
        MessagingAdmin.Required,
        403
      );
    var name = request.Name?.Trim() ?? "";
    var language = request.Language?.Trim() ?? "";
    var text = request.Text?.Trim() ?? "";
    if (
      ApprovedTemplates.Refusal(name, language, request.Parameters, text) is
      { } refused
    )
      return RequestResponse<ApprovedTemplateView>.Fail(refused, 400);
    if (await messaging.BusinessNumberAsync(ct) is not { } number)
      return RequestResponse<ApprovedTemplateView>.Fail(
        "WhatsApp is not set up. Add it first.",
        409
      );
    if (await templates.FindAsync(number, name, language, ct) is not null)
      return RequestResponse<ApprovedTemplateView>.Fail(
        "This template is already recorded for this number.",
        409
      );
    var template = new ApprovedTemplate
    {
      Id = Guid.NewGuid(),
      Channel = messaging.Channel,
      BusinessNumberId = number,
      Name = name,
      Language = language,
      Parameters = request.Parameters,
      Text = text,
      CreatedAt = clock.GetUtcNow().UtcDateTime,
      CreatedBy = user,
    };
    db.ApprovedTemplates.Add(template);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      db.Entry(template).State = EntityState.Detached;
      // Another administrator recorded the same template first; any other
      // failure goes to the request boundary.
      if (
        await templates.FindAsync(
          number,
          name,
          language,
          CancellationToken.None
        )
        is null
      )
        throw;
      return RequestResponse<ApprovedTemplateView>.Fail(
        "This template is already recorded for this number.",
        409
      );
    }
    return RequestResponse<ApprovedTemplateView>.Ok(View(template));
  }

  // A template withdrawn here is not sent: a reply already queued with it
  // is withdrawn when its turn comes.
  public async Task<RequestResponse<bool>> Handle(
    WithdrawTemplateCommand request,
    CancellationToken ct
  )
  {
    if (await MessagingAdmin.UserAsync(db, caller, roles, ct) is null)
      return RequestResponse<bool>.Fail(MessagingAdmin.Required, 403);
    var removed = await db
      .ApprovedTemplates.Where(x => x.Id == request.Id)
      .ExecuteDeleteAsync(ct);
    return removed == 0
      ? RequestResponse<bool>.Fail("Template not found.", 404)
      : RequestResponse<bool>.Ok(true);
  }

  private static ApprovedTemplateView View(ApprovedTemplate x) =>
    new(x.Id, x.Name, x.Language, x.Parameters, x.Text);
}
