using Application.Interfaces;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class DeliveryOwnershipTests
{
  [Fact]
  public void DeliveryConsumersUseDriverIdentityInsteadOfChoosingChannelContacts()
  {
    var method = typeof(IDriverTextDelivery).GetMethod("RecipientAsync")!;
    Assert.Equal(typeof(Guid?), method.GetParameters()[0].ParameterType);
    foreach (
      var file in Directory.GetFiles(
        Path.Combine(Root(), "Server/Application/Features/Routing"),
        "*.cs",
        SearchOption.AllDirectories
      )
    )
      Assert.DoesNotMatch(
        @"\bDriverWhatsApp\b|\bWhatsAppPhone\b|\bDriverRecipients\b",
        File.ReadAllText(file)
      );
    Assert.Empty(
      Directory.GetFiles(
        Path.Combine(Root(), "Server/Domain/Rules/Fleet"),
        "*WhatsApp*"
      )
    );
  }
}
