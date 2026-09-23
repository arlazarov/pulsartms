using System.ComponentModel.DataAnnotations;

namespace Application.Storage;

public sealed class StorageOptions
{
  // The largest file PulsR stores; providers may accept less.
  [Range(1, 1024)]
  public int MaximumMegabytes { get; set; } = 100;

  // Uploads streaming at once in this process; the rest wait.
  [Range(1, 16)]
  public int MaximumConcurrentUploads { get; set; } = 2;

  // How long one attempt holds an upload. Longer than the providers' request
  // timeout (10 minutes), so an attempt's request has ended before another
  // may take the upload over or the reconciler may settle it.
  [Range(15, 1440)]
  public int UploadLeaseMinutes { get; set; } = 30;

  [Range(1, 1440)]
  public int ReconcileIntervalMinutes { get; set; } = 15;

  [Range(1, 500)]
  public int ReconcileBatchSize { get; set; } = 50;
}
