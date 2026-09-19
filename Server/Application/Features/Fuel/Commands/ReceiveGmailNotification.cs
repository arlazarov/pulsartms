using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Models;

namespace Application.Features.Fuel.Commands;

public record ReceiveGmailNotificationCommand(string Authorization, Stream Body)
  : IRequest<RequestResponse<int>>;

public sealed class ReceiveGmailNotificationHandler(
  IGmailPushValidator validator,
  ISender sender
) : IRequestHandler<ReceiveGmailNotificationCommand, RequestResponse<int>>
{
  private static readonly JsonSerializerOptions JsonOptions = new(
    JsonSerializerDefaults.Web
  );

  public async Task<RequestResponse<int>> Handle(
    ReceiveGmailNotificationCommand request,
    CancellationToken cancellationToken
  )
  {
    if (!validator.IsConfigured)
      return RequestResponse<int>.Fail(
        "Gmail notifications are not configured.",
        503
      );
    if (!await validator.ValidateAsync(request.Authorization))
      return RequestResponse<int>.Fail(
        "Invalid notification credentials.",
        401
      );
    try
    {
      var envelope = await JsonSerializer.DeserializeAsync<PushEnvelope>(
        request.Body,
        JsonOptions,
        cancellationToken
      );
      if (string.IsNullOrWhiteSpace(envelope?.Message?.Data))
        return InvalidNotification();
      var notification = JsonSerializer.Deserialize<MailboxNotification>(
        Convert.FromBase64String(envelope.Message.Data),
        JsonOptions
      );
      if (
        notification is null
        || notification.HistoryId is null or 0
        || !validator.IsExpectedMailbox(notification.EmailAddress)
      )
        return InvalidNotification();
    }
    catch (Exception ex) when (ex is JsonException or FormatException)
    {
      return InvalidNotification();
    }
    return await sender.Send(
      new ImportFuelDiscountsCommand(),
      cancellationToken
    );
  }

  private static RequestResponse<int> InvalidNotification() =>
    RequestResponse<int>.Fail("Invalid Gmail notification.");

  private sealed record PushEnvelope(PushMessage? Message);

  private sealed record PushMessage(string? Data);

  private sealed record MailboxNotification(
    string? EmailAddress,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
      ulong? HistoryId
  );
}
