using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchCompletedDocumentsTests
{
  [Fact]
  public async Task CompletedLoadAllowsPodUploadWithoutUnlockingLoadEdits()
  {
    var workspace = new DispatchWorkspaceResponse
    {
      Load = new()
      {
        Id = Guid.NewGuid(),
        LoadNumber = 2060,
        Status = "completed",
      },
      CanEdit = false,
      ReadOnlyReason = "Completed or cancelled loads are read-only.",
      SourceFingerprint = new string('A', 64),
    };
    UploadDispatchDocumentRequest? uploaded = null;
    var writes = 0;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post)
        {
          Assert.Equal($"/api/dispatch/{workspace.Load.Id}/documents", path);
          var content = request.Content!;
          uploaded =
            await content.ReadFromJsonAsync<UploadDispatchDocumentRequest>(ct);
          writes++;
          return MileageComponentResponses.Ok(
            new DispatchDocumentInfo(
              uploaded!.IdempotencyKey,
              uploaded.Kind,
              uploaded.FileName,
              "application/pdf",
              uploaded.Content.Length,
              DateTime.UtcNow,
              "Dispatcher"
            )
          );
        }
        if (path.EndsWith("/activity"))
          return MileageComponentResponses.Ok(
            new DispatchActivityPage(
              workspace.Load.Id,
              0,
              [],
              null,
              0,
              [],
              null
            )
          );
        if (path.EndsWith("/documents"))
          return MileageComponentResponses.Ok(new List<DispatchDocumentInfo>());
        return MileageComponentResponses.Ok(workspace);
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var authorization = context.AddAuthorization();
    authorization.SetAuthorized("Dispatcher");
    authorization.SetRoles("Dispatch");
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, workspace.Load.Id)
    );
    page.WaitForElement("#load-instructions");
    Assert.True(page.Find("#load-instructions").HasAttribute("disabled"));
    var documents = page.FindComponent<DispatchDocuments>();
    documents.WaitForAssertion(
      () => Assert.False(documents.Find("input[type=file]").HasAttribute("disabled"))
    );
    await documents.Find(".dispatch-documents select").ChangeAsync("pod");
    documents.FindComponent<InputFile>()
      .UploadFiles(
        InputFileContent.CreateFromText(
          "%PDF-1.7 fixture",
          "proof.pdf",
          contentType: "application/pdf"
        )
      );
    documents.WaitForAssertion(
      () => Assert.Single(documents.FindAll(".dispatch-documents__list li"))
    );
    Assert.Equal(1, writes);
    Assert.Equal("pod", uploaded!.Kind);
    Assert.True(Button(page, "Save changes").HasAttribute("disabled"));
    Assert.True(page.Find("#load-instructions").HasAttribute("disabled"));
  }

  private static IElement Button(
    IRenderedComponent<DispatchDetails> page,
    string text
  ) => page.FindAll("button").Single(x => x.TextContent.Trim() == text);
}
