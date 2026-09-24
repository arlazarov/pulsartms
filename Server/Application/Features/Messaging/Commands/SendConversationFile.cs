using System.Text.Json;
using Application.Features.Messaging.Options;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Models;
using Application.Storage;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;
using Microsoft.Extensions.Options;

namespace Application.Features.Messaging.Commands;

// A file a dispatcher sends. Sha256 is the content's hash as the browser
// computed it: the file is stored under the retry key as its id, checked
// against its declared kind, and queued only when it may be sent.
public sealed record SendConversationFileCommand(
  Guid ConversationId,
  Guid IdempotencyKey,
  string FileName,
  string ContentType,
  long Length,
  string Sha256,
  Stream Content,
  string? Caption,
  Guid? LastSeenMessageId,
  bool Confirm
) : IRequest<RequestResponse<MessageView>>;

public sealed record SendConversationTemplateCommand(
  Guid ConversationId,
  Guid IdempotencyKey,
  string Name,
  string Language,
  IReadOnlyList<string> Parameters
) : IRequest<RequestResponse<MessageView>>;

public sealed record GetMessageTemplatesQuery
  : IRequest<RequestResponse<IReadOnlyList<MessageTemplateView>>>;

public sealed record MessageTemplateView(
  string Name,
  string Language,
  int Parameters,
  string Text
);

public sealed class ConversationFilesAndTemplates(
  IAppDbContext db,
  ICurrentUser caller,
  ReplyQueue queue,
  FileStore files,
  StoredFileCheck check,
  IOptions<MessagingOptions> options,
  TimeProvider clock
)
  : IRequestHandler<SendConversationFileCommand, RequestResponse<MessageView>>,
    IRequestHandler<
      SendConversationTemplateCommand,
      RequestResponse<MessageView>
    >,
    IRequestHandler<
      GetMessageTemplatesQuery,
      RequestResponse<IReadOnlyList<MessageTemplateView>>
    >
{
  public const int MaximumCaption = 1024;

  // WhatsApp's own limits per kind; PulsR's storage may allow less.
  public static long Limit(string contentType) =>
    contentType.Split('/')[0] switch
    {
      "image" => 5L * 1024 * 1024,
      "audio" or "video" => 16L * 1024 * 1024,
      _ => 100L * 1024 * 1024,
    };

  public async Task<RequestResponse<MessageView>> Handle(
    SendConversationFileCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return Fail("Access denied.", 403);
    var caption = request.Caption?.Trim() ?? "";
    var type =
      request.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? "";
    if (
      request.IdempotencyKey == Guid.Empty
      || caption.Length > MaximumCaption
      || request.Length <= 0
      || request.Length > Limit(type)
    )
      return Fail("This file cannot be sent over WhatsApp.", 400);
    var conversation = await db.Conversations.SingleOrDefaultAsync(
      x => x.Id == request.ConversationId,
      ct
    );
    if (conversation is null)
      return Fail("Conversation not found.", 404);
    var verdict = await queue.CheckAsync(
      conversation,
      request.IdempotencyKey,
      user,
      request.LastSeenMessageId,
      request.Confirm,
      needsWindow: true,
      earlier => earlier.Kind == ConversationMessageKinds.File,
      ct
    );
    if (verdict.Refused is { } refused)
      return Fail(refused.Message, refused.Status);
    if (verdict.Earlier is { } earlier)
      return RequestResponse<MessageView>.Ok(ConversationReplies.View(earlier));
    StoredFile file;
    try
    {
      file = await files.PutAsync(
        new(
          request.IdempotencyKey,
          request.FileName,
          type,
          request.Length,
          request.Sha256?.ToLowerInvariant() ?? "",
          ["Sent", clock.GetUtcNow().UtcDateTime.ToString("yyyy.MM.dd")],
          request.FileName
        ),
        request.Content,
        ct
      );
    }
    catch (Exception ex)
      when (ex
          is ArgumentException
            or StorageContentMismatchException
            or StorageConflictException
      )
    {
      return Fail("The file did not arrive as the browser described it.", 400);
    }
    catch (StorageBusyException)
    {
      return Fail("This file is still being stored. Try again.", 409);
    }
    catch (StorageUnavailableException)
    {
      return Fail("Choose where to store files first.", 409);
    }
    if (await check.CheckAsync(file.Id, ct) != StoredFileStates.Available)
      return Fail("PulsR sends PDF, images, audio and video only.", 400);
    var message = queue.Queue(
      conversation,
      ConversationMessageKinds.File,
      caption,
      caption.Length > 0 ? caption : file.Name,
      user,
      request.IdempotencyKey,
      1
    );
    var now = clock.GetUtcNow().UtcDateTime;
    db.MessageAttachments.Add(
      new MessageAttachment
      {
        Id = Guid.NewGuid(),
        MessageId = message.Id,
        StoredFileId = file.Id,
        DeclaredType = type,
        OriginalName = file.Name,
        Caption = caption,
        State = MessageAttachmentStates.Stored,
        NextAttemptAt = now,
        CreatedAt = now,
      }
    );
    return await queue.CommitAsync(conversation, ct) is { } failed
      ? Fail(failed.Message, failed.Status)
      : RequestResponse<MessageView>.Ok(ConversationReplies.View(message));
  }

  public async Task<RequestResponse<MessageView>> Handle(
    SendConversationTemplateCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return Fail("Access denied.", 403);
    var template = options.Value.Templates.FirstOrDefault(x =>
      x.Name == request.Name && x.Language == request.Language
    );
    if (
      template is null
      || request.Parameters is not { } parameters
      || parameters.Count != template.Parameters
      || parameters.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200)
      || request.IdempotencyKey == Guid.Empty
    )
      return Fail("Choose an approved template and fill in every field.", 400);
    var conversation = await db.Conversations.SingleOrDefaultAsync(
      x => x.Id == request.ConversationId,
      ct
    );
    if (conversation is null)
      return Fail("Conversation not found.", 404);
    var text = Fill(template.Text, parameters);
    var payload = JsonSerializer.Serialize(
      new TemplatePayload(template.Name, template.Language, [.. parameters])
    );
    var verdict = await queue.CheckAsync(
      conversation,
      request.IdempotencyKey,
      user,
      null,
      confirm: true,
      needsWindow: false,
      earlier => earlier.Template == payload,
      ct
    );
    if (verdict.Refused is { } refused)
      return Fail(refused.Message, refused.Status);
    if (verdict.Earlier is { } earlier)
      return RequestResponse<MessageView>.Ok(ConversationReplies.View(earlier));
    var message = queue.Queue(
      conversation,
      ConversationMessageKinds.Template,
      text,
      text,
      user,
      request.IdempotencyKey,
      1,
      payload
    );
    return await queue.CommitAsync(conversation, ct) is { } failed
      ? Fail(failed.Message, failed.Status)
      : RequestResponse<MessageView>.Ok(ConversationReplies.View(message));
  }

  public Task<RequestResponse<IReadOnlyList<MessageTemplateView>>> Handle(
    GetMessageTemplatesQuery request,
    CancellationToken ct
  ) =>
    Task.FromResult(
      RequestResponse<IReadOnlyList<MessageTemplateView>>.Ok(
        [
          .. options.Value.Templates.Select(x => new MessageTemplateView(
            x.Name,
            x.Language,
            x.Parameters,
            x.Text
          )),
        ]
      )
    );

  public static string Fill(string text, IReadOnlyList<string> parameters)
  {
    for (var i = 0; i < parameters.Count; i++)
      text = text.Replace($"{{{{{i + 1}}}}}", parameters[i].Trim());
    return text.Length <= 4096 ? text : text[..4096];
  }

  private static RequestResponse<MessageView> Fail(
    string message,
    int status
  ) => RequestResponse<MessageView>.Fail(message, status);
}

public sealed record TemplatePayload(
  string Name,
  string Language,
  IReadOnlyList<string> Parameters
);
