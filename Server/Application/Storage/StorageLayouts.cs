using Application.Models;
using Domain.Entities.Storage;
using Domain.Rules.Storage;

namespace Application.Storage;

public sealed record GetStorageLayoutQuery
  : IRequest<RequestResponse<StorageLayoutView>>;

public sealed record UpdateStorageLayoutCommand(
  string LoadsFolder,
  string LoadTemplate,
  string CancelledSuffix,
  string InboxFolder,
  long ExpectedRevision
) : IRequest<RequestResponse<StorageLayoutView>>;

// Example shows the template applied to made-up values, so an administrator
// sees the result before saving; it is not any real load.
public sealed record StorageLayoutView(
  string LoadsFolder,
  string LoadTemplate,
  string CancelledSuffix,
  string InboxFolder,
  long Revision,
  string Example,
  string CancelledExample
);

// What a load's folder is called at the moment it is first written to.
public sealed record LoadFolderFacts(
  DateOnly? Date,
  string? LoadNumber,
  string? Broker,
  string? OrderReference,
  string? TruckNumber,
  bool Cancelled
);

public static class DocumentKinds
{
  public const string ProofOfDelivery = "POD";
  public const string BillOfLading = "BOL";
  public const string RateConfirmation = "Rate Confirmation";
  public const string Receipt = "Receipt";
  public const string Document = "Document";
}

// A company's readable layout. Folder names are decided when a file is
// written and kept with it; a load whose truck or date changes later keeps
// its files where they are until someone deliberately renames them. Files
// nobody has filed go to the inbox under their own name, by day.
public sealed class StorageLayouts(IAppDbContext db, TimeProvider clock)
  : IRequestHandler<GetStorageLayoutQuery, RequestResponse<StorageLayoutView>>,
    IRequestHandler<
      UpdateStorageLayoutCommand,
      RequestResponse<StorageLayoutView>
    >
{
  private static readonly LoadFolderFacts Sample = new(
    new DateOnly(2026, 9, 21),
    "1407",
    "Example Broker",
    "PO-5521",
    "101",
    false
  );

  public async Task<IReadOnlyList<string>> LoadFolderAsync(
    LoadFolderFacts facts,
    CancellationToken ct
  )
  {
    var layout = await CurrentAsync(ct);
    return
    [
      .. StorageNaming.Folder([layout.LoadsFolder]),
      LoadFolderName(layout, facts),
    ];
  }

  public async Task<IReadOnlyList<string>> InboxFolderAsync(
    DateOnly day,
    CancellationToken ct
  ) =>
    [
      .. StorageNaming.Folder([(await CurrentAsync(ct)).InboxFolder]),
      day.ToString("yyyy.MM.dd"),
    ];

  // "POD.pdf": the kind, with the extension the file arrived with.
  public static string DocumentName(string kind, string originalName)
  {
    var extension = Path.GetExtension(StorageNaming.Segment(originalName));
    extension =
      extension.Length is > 1 and <= 10
      && extension[1..].All(char.IsAsciiLetterOrDigit)
        ? extension.ToLowerInvariant()
        : "";
    return StorageNaming.Segment(kind + extension);
  }

  public async Task<RequestResponse<StorageLayoutView>> Handle(
    GetStorageLayoutQuery request,
    CancellationToken ct
  ) => RequestResponse<StorageLayoutView>.Ok(View(await CurrentAsync(ct)));

  public async Task<RequestResponse<StorageLayoutView>> Handle(
    UpdateStorageLayoutCommand request,
    CancellationToken ct
  )
  {
    var loads = string.Join('/', StorageNaming.Folder([request.LoadsFolder]));
    var inbox = string.Join('/', StorageNaming.Folder([request.InboxFolder]));
    var suffix = request.CancelledSuffix?.Trim() ?? "";
    var problem =
      StorageNaming.Validate(request.LoadTemplate)
      ?? (loads.Length is 0 or > 300 ? "Choose the folder for loads." : null)
      ?? (inbox.Length is 0 or > 300 ? "Choose the inbox folder." : null)
      ?? (suffix.Length > 40 ? "The cancelled suffix is too long." : null);
    if (problem is not null)
      return RequestResponse<StorageLayoutView>.Fail(problem);
    var layout = await db.StorageLayouts.SingleOrDefaultAsync(ct);
    if ((layout?.Revision ?? 0) != request.ExpectedRevision)
      return RequestResponse<StorageLayoutView>.Fail(
        "The layout changed. Reload and try again.",
        409
      );
    if (layout is null)
      db.StorageLayouts.Add(layout = new StorageLayout { Id = Guid.NewGuid() });
    layout.LoadsFolder = loads;
    layout.LoadTemplate = request.LoadTemplate.Trim();
    layout.CancelledSuffix = suffix;
    layout.InboxFolder = inbox;
    layout.Revision++;
    layout.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return RequestResponse<StorageLayoutView>.Fail(
        "The layout changed. Reload and try again.",
        409
      );
    }
    return RequestResponse<StorageLayoutView>.Ok(View(layout));
  }

  private async Task<StorageLayout> CurrentAsync(CancellationToken ct) =>
    await db.StorageLayouts.AsNoTracking().SingleOrDefaultAsync(ct)
    ?? new StorageLayout();

  private static string LoadFolderName(
    StorageLayout layout,
    LoadFolderFacts facts
  ) =>
    StorageNaming.Render(
      layout.LoadTemplate,
      new Dictionary<string, string?>
      {
        ["date"] = facts.Date?.ToString("yyyy.MM.dd"),
        ["load"] = facts.LoadNumber,
        ["broker"] = facts.Broker,
        ["order"] = facts.OrderReference,
        ["truck"] = facts.TruckNumber,
      },
      facts.Cancelled ? layout.CancelledSuffix : null
    );

  private static StorageLayoutView View(StorageLayout layout) =>
    new(
      layout.LoadsFolder,
      layout.LoadTemplate,
      layout.CancelledSuffix,
      layout.InboxFolder,
      layout.Revision,
      $"{layout.LoadsFolder}/{LoadFolderName(layout, Sample)}",
      $"{layout.LoadsFolder}/{LoadFolderName(layout, Sample with { Cancelled = true, OrderReference = null })}"
    );
}
