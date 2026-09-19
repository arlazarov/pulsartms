using Application.Features.Fuel.Interfaces;
using Google.Apis.Gmail.v1.Data;

namespace Infrastructure.Integrations.Google.Gmail;

public class GmailWatchService(GmailServiceFactory gmailServiceFactory)
  : IGmailWatchService
{
  public async Task<GmailWatchResult> StartAsync(
    CancellationToken cancellationToken = default
  )
  {
    using var gmail = await gmailServiceFactory.CreateAsync(cancellationToken);

    var request = new WatchRequest
    {
      TopicName = "projects/amftms/topics/gmail-fuel-notifications",

      LabelIds = ["Label_7441158285664312766"],

      LabelFilterBehavior = "include",
    };

    var response = await gmail
      .Users.Watch(request, "me")
      .ExecuteAsync(cancellationToken);

    return new GmailWatchResult
    {
      HistoryId = response.HistoryId,
      Expiration = response.Expiration,
    };
  }
}
