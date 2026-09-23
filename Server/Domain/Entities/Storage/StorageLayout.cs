namespace Domain.Entities.Storage;

// How a company's files read outside PulsR: where load folders go, how a
// load folder is named, and where files nobody has filed yet wait. One row
// per company; absent means the defaults.
public sealed class StorageLayout : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string LoadsFolder { get; set; } = StorageLayoutDefaults.LoadsFolder;
  public string LoadTemplate { get; set; } = StorageLayoutDefaults.LoadTemplate;
  public string CancelledSuffix { get; set; } =
    StorageLayoutDefaults.CancelledSuffix;
  public string InboxFolder { get; set; } = StorageLayoutDefaults.InboxFolder;
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}

// An example shaped like a carrier's usual paper layout, not any company's
// data: a company changes it in Settings.
public static class StorageLayoutDefaults
{
  public const string LoadsFolder = "Dispatch/Loads";
  public const string LoadTemplate =
    "{date} - {load} - {broker} - {order} - {truck}";
  public const string CancelledSuffix = "Canceled";
  public const string InboxFolder = "Inbox";
}
