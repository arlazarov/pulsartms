using Domain.Rules.Storage;

namespace Server.Tests.Storage;

// Names read well outside PulsR and are safe everywhere: missing fields
// leave no stray separators, nothing climbs out of its folder, and a second
// file of the same name gets a numbered name rather than replacing one.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class StorageNamingTests
{
  private const string Template =
    "{date} - {load} - {broker} - {order} - {truck}";

  [Fact]
  public void AFullLoadReadsLikeTheCompanysOwnFolders()
  {
    Assert.Equal(
      "2026.09.21 - 1407 - Example Broker - PO-5521 - 101",
      StorageNaming.Render(Template, Values())
    );
    Assert.Equal(
      "2026.09.21 - 1407 - Example Broker - PO-5521 - 101 - Canceled",
      StorageNaming.Render(Template, Values(), "Canceled")
    );
  }

  [Fact]
  public void MissingFieldsLeaveNoSeparatorsBehind()
  {
    Assert.Equal(
      "2026.09.21 - 1407 - 101",
      StorageNaming.Render(Template, Values(broker: " ", order: null))
    );
    Assert.Equal(
      "1407 - Canceled",
      StorageNaming.Render(
        Template,
        Values(date: null, broker: null, order: null, truck: null),
        "Canceled"
      )
    );
    Assert.Equal(
      "file",
      StorageNaming.Render(Template, new Dictionary<string, string?>())
    );
  }

  [Theory]
  [InlineData("../../etc/passwd", "etc passwd")]
  [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a b c d e f g h i j")]
  [InlineData("  ..hidden.  ", "hidden")]
  [InlineData("CON.pdf", "_CON.pdf")]
  [InlineData("nul", "_nul")]
  [InlineData("tab\there\nnew", "tab here new")]
  [InlineData("zero​width", "zero width")]
  [InlineData("", "file")]
  public void ANameCannotLeaveItsFolderOrNameADevice(string raw, string safe) =>
    Assert.Equal(safe, StorageNaming.Segment(raw));

  [Fact]
  public void UnicodeIsNormalizedAndLongNamesKeepTheirExtension()
  {
    Assert.Equal("Café", StorageNaming.Segment("Café"));
    var name = StorageNaming.Segment(new string('ж', 300) + ".pdf");
    Assert.Equal(StorageNaming.MaximumSegment, name.Length);
    Assert.EndsWith(".pdf", name);
  }

  [Fact]
  public void ASecondFileOfTheSameNameIsNumberedWithoutRegardToCase()
  {
    Assert.Equal("POD.pdf", StorageNaming.Unique("POD.pdf", ["BOL.pdf"]));
    Assert.Equal(
      "POD (3).pdf",
      StorageNaming.Unique("POD.pdf", ["pod.PDF", "POD (2).pdf"])
    );
  }

  [Fact]
  public void AFolderPathDropsEmptyAndClimbingParts() =>
    Assert.Equal(
      ["Dispatch", "Loads", "x y"],
      StorageNaming.Folder(["/Dispatch//Loads/", "..", "./x:y"])
    );

  [Theory]
  [InlineData("{load}", null)]
  [InlineData("plain text", "The template must name at least one field.")]
  [InlineData(
    "{load} - {driver}",
    "The template names an unknown field: driver."
  )]
  [InlineData("{load", "The template has a brace that does not close.")]
  [InlineData("load}", "The template has a brace that does not open.")]
  public void OnlyKnownFieldsMakeATemplate(string template, string? problem) =>
    Assert.Equal(problem, StorageNaming.Validate(template));

  private static Dictionary<string, string?> Values(
    string? date = "2026.09.21",
    string? broker = "Example Broker",
    string? order = "PO-5521",
    string? truck = "101"
  ) =>
    new()
    {
      ["date"] = date,
      ["load"] = "1407",
      ["broker"] = broker,
      ["order"] = order,
      ["truck"] = truck,
    };
}
