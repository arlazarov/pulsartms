using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Customers;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class CustomerDirectoryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CompanyEditsUseOpenedRevisionAndRetainFailures(
    bool conflict
  )
  {
    var profile = new BrokerProfile
    {
      Id = Guid.NewGuid(),
      Revision = 7,
      Name = "C.H. Robinson",
      Contact = "Saved contact",
      Terms = new() { PaymentDays = 30 },
    };
    BrokerProfile? written = null;
    var reads = 0;
    await using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal("/api/brokers", request.RequestUri!.AbsolutePath);
        if (request.Method == HttpMethod.Get)
        {
          reads++;
          return MileageComponentResponses.Ok(
            new List<BrokerProfile> { profile }
          );
        }
        written = await request.Content!.ReadFromJsonAsync<BrokerProfile>(ct);
        return conflict
          ? new HttpResponseMessage(HttpStatusCode.Conflict)
          : MileageComponentResponses.Ok(
            new BrokerProfile
            {
              Id = profile.Id,
              Revision = 8,
              Name = profile.Name,
              Contact = written!.Contact,
              Terms = written.Terms,
            }
          );
      }
    );
    var page = context.Render<Customers>();
    Assert.Equal(0, reads);
    await page.Find("#company-search").ChangeAsync("CH Robinson");
    Assert.Equal(0, reads);
    await page.Find("form").SubmitAsync();
    Assert.Equal(1, reads);
    await page.Find(".customers-page__results button").ClickAsync(new());
    await page.Find("#company-contact").InputAsync("Updated contact");
    Assert.Equal("Saved contact", profile.Contact);
    Assert.Null(written);
    await page.Find("form").SubmitAsync();
    Assert.NotNull(written);
    Assert.Equal(7, written.Revision);
    Assert.Equal(profile.Id, written.Id);
    Assert.Equal("Updated contact", written.Contact);
    Assert.Equal(30, written.Terms.PaymentDays);
    Assert.Equal(
      "Updated contact",
      page.Find("#company-contact").GetAttribute("value")
    );
    Assert.Equal(
      !conflict,
      page.Find("button[type='submit']").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task DiscardDoesNotCreateACompany()
  {
    await using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No writes before Save.")
    );
    var page = context.Render<Customers>();
    await page.Find("button.btn--primary").ClickAsync(new());
    await page.Find("#company-name").InputAsync("Example company");
    await page.Find("button[type='button']").ClickAsync(new());
    Assert.NotNull(page.Find("#company-search"));
  }
}
