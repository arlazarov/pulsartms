using Application.Features.Dispatch.Documents;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchDocumentRulesTests
{
  [Fact]
  public void SniffsAllowedSignaturesAndBoundsContent()
  {
    Assert.Equal(
      "application/pdf",
      DispatchDocumentRules.ContentType("%PDF-1.7 test"u8.ToArray())
    );
    Assert.Equal(
      "image/png",
      DispatchDocumentRules.ContentType([137, 80, 78, 71, 13, 10, 26, 10])
    );
    Assert.Equal(
      "image/jpeg",
      DispatchDocumentRules.ContentType([255, 216, 255, 224, 0, 16, 74, 70])
    );
    Assert.Null(DispatchDocumentRules.ContentType(null));
    Assert.Null(DispatchDocumentRules.ContentType([1, 2, 3]));
    Assert.Null(
      DispatchDocumentRules.ContentType("<svg>test</svg>"u8.ToArray())
    );
    var maximum = new byte[DispatchDocumentRules.MaximumBytes];
    "%PDF-1.7"u8.CopyTo(maximum);
    Assert.Equal("application/pdf", DispatchDocumentRules.ContentType(maximum));
    Assert.Null(
      DispatchDocumentRules.ContentType(
        new byte[DispatchDocumentRules.MaximumBytes + 1]
      )
    );
  }

  [Theory]
  [InlineData("../../invoice.html", "application/pdf", "invoice.pdf")]
  [InlineData("C:\\secret\\photo.exe", "image/png", "photo.png")]
  [InlineData("bad\r\nname.jpg", "image/jpeg", "badname.jpg")]
  [InlineData("", "application/pdf", "document.pdf")]
  public void NamesNeverUsePathsOrClientMimeTypes(
    string name,
    string type,
    string expected
  ) => Assert.Equal(expected, DispatchDocumentRules.FileName(name, type));

  [Theory]
  [InlineData("rc", true)]
  [InlineData("bol", true)]
  [InlineData("pod", true)]
  [InlineData("other", true)]
  [InlineData("script", false)]
  [InlineData(null, false)]
  public void OnlyKnownDocumentKindsAreAccepted(string? kind, bool expected) =>
    Assert.Equal(expected, DispatchDocumentRules.ValidKind(kind));
}
