using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;

namespace Infrastructure.Integrations.Google.Gmail;

public class GmailAttachmentService(GmailServiceFactory gmailServiceFactory)
{
  public async Task<List<GmailAttachment>> GetFuelDiscountAttachmentsAsync(
    IReadOnlyCollection<string> importedMessageIds,
    CancellationToken cancellationToken = default
  )
  {
    using var gmailService = await gmailServiceFactory.CreateAsync(
      cancellationToken
    );
    var imported = importedMessageIds.ToHashSet(
      StringComparer.OrdinalIgnoreCase
    );
    var result = new List<GmailAttachment>();
    string? pageToken = null;

    do
    {
      var listRequest = gmailService.Users.Messages.List("me");
      listRequest.Q =
        "label:fleet-bvd-fuel newer_than:2d has:attachment filename:csv";
      listRequest.MaxResults = 500;
      listRequest.PageToken = pageToken;

      var messages = await listRequest.ExecuteAsync(cancellationToken);

      if (messages.Messages is not null)
      {
        foreach (var messageInfo in messages.Messages)
        {
          if (
            string.IsNullOrWhiteSpace(messageInfo.Id)
            || imported.Contains(messageInfo.Id)
          )
          {
            continue;
          }

          var messageRequest = gmailService.Users.Messages.Get(
            "me",
            messageInfo.Id
          );
          messageRequest.Format = UsersResource
            .MessagesResource
            .GetRequest
            .FormatEnum
            .Full;

          var message = await messageRequest.ExecuteAsync(cancellationToken);
          var parts = GetAllParts(message.Payload);

          foreach (var part in parts)
          {
            if (string.IsNullOrWhiteSpace(part.Filename))
            {
              continue;
            }

            if (
              !part.Filename.EndsWith(
                ".csv",
                StringComparison.OrdinalIgnoreCase
              )
            )
            {
              continue;
            }

            if (string.IsNullOrWhiteSpace(part.Body?.AttachmentId))
            {
              continue;
            }

            var attachmentRequest = gmailService.Users.Messages.Attachments.Get(
              "me",
              message.Id,
              part.Body.AttachmentId
            );

            var attachment = await attachmentRequest.ExecuteAsync(
              cancellationToken
            );
            var content = DecodeBase64Url(attachment.Data);

            result.Add(
              new GmailAttachment
              {
                MessageId = message.Id,
                FileName = part.Filename,
                Content = content,
              }
            );
          }
        }
      }

      pageToken = messages.NextPageToken;
    } while (!string.IsNullOrWhiteSpace(pageToken));

    return result;
  }

  private static IEnumerable<MessagePart> GetAllParts(MessagePart? part)
  {
    if (part is null)
    {
      yield break;
    }

    yield return part;

    if (part.Parts is null)
    {
      yield break;
    }

    foreach (var child in part.Parts)
    {
      foreach (var nested in GetAllParts(child))
      {
        yield return nested;
      }
    }
  }

  private static byte[] DecodeBase64Url(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return [];
    }

    var base64 = value.Replace('-', '+').Replace('_', '/');

    switch (base64.Length % 4)
    {
      case 2:
        base64 += "==";
        break;
      case 3:
        base64 += "=";
        break;
    }

    return Convert.FromBase64String(base64);
  }
}
