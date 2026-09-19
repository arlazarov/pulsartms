using System.Globalization;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Microsoft.Extensions.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Server.Tests.Support;

internal static class DispatchImportTestData
{
  public const string Key = "fixture";
  public const string DisplayName = "Fixture import";
  public static IOptions<DispatchImportOptions> Options =>
    OptionsFactory.Create(new DispatchImportOptions { Provider = Key });

  public static IReadOnlyList<ExternalDispatch> Identify(
    IReadOnlyList<ExternalDispatch> sources
  )
  {
    foreach (var source in sources)
      if (source.ExternalId.Length == 0)
        source.ExternalId = source.LoadNumber.ToString(
          CultureInfo.InvariantCulture
        );
    return sources;
  }
}
