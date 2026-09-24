using System.Text;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Storage;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Storage;

// A company's readable layout: its own template and folders, files named
// for what they are, the name they arrived with kept apart, and a second
// file of the same name numbered rather than replacing the first.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StorageLayoutTests
{
  [Fact]
  public async Task TheLayoutIsTheCompanysOwnAndChangesOnlyByRevision()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var layouts = Layouts(f.Db);

    var defaults = (
      await layouts.Handle(new GetStorageLayoutQuery(), default)
    ).Response!;
    Assert.Equal(
      "Dispatch/Loads/2026.09.21 - 1407 - Example Broker - PO-5521 - 101",
      defaults.Example
    );
    Assert.EndsWith(
      "1407 - Example Broker - 101 - Canceled",
      defaults.CancelledExample
    );

    Assert.Equal(
      400,
      (await layouts.Handle(Update("{load} - {driver}", 0), default)).StatusCode
    );
    var saved = await layouts.Handle(Update("{load} - {date}", 0), default);
    Assert.Equal(
      ("Files/Loads", 1),
      (saved.Response!.LoadsFolder, saved.Response.Revision)
    );
    f.Db.ChangeTracker.Clear();
    Assert.Equal(
      409,
      (await Layouts(f.Db).Handle(Update("{load}", 0), default)).StatusCode
    );

    var folder = await Layouts(f.Db)
      .LoadFolderAsync(
        new(new DateOnly(2026, 9, 21), "1399", null, null, null, true),
        default
      );
    Assert.Equal(["Files", "Loads", "1399 - 2026.09.21 - Void"], folder);
    Assert.Equal(
      ["Waiting", "2026.09.23"],
      await Layouts(f.Db).InboxFolderAsync(new DateOnly(2026, 9, 23), default)
    );
  }

  [Theory]
  [InlineData(DocumentKinds.ProofOfDelivery, "IMG_2231.JPG", "POD.jpg")]
  [InlineData(
    DocumentKinds.RateConfirmation,
    "rc.pdf",
    "Rate Confirmation.pdf"
  )]
  [InlineData(DocumentKinds.Receipt, "no extension", "Receipt")]
  [InlineData(DocumentKinds.BillOfLading, "bol.p$f", "BOL")]
  public void ADocumentIsNamedForWhatItIs(
    string kind,
    string original,
    string name
  ) => Assert.Equal(name, StorageLayouts.DocumentName(kind, original));

  // Two receipts for one load read as "Receipt.jpg" and "Receipt (2).jpg";
  // each keeps the name it arrived with, and a retry of the first keeps its
  // own name.
  [Fact]
  public async Task ASecondFileOfTheSameNameIsNumberedAndKeepsItsOriginalName()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    string[] folder = ["Dispatch", "Loads", "2026.09.21 - 1407"];
    var first = Guid.NewGuid();

    var one = await PutAsync(f, first, "Receipt.jpg", "IMG_1.JPG", folder, [1]);
    var two = await PutAsync(
      f,
      Guid.NewGuid(),
      "receipt.JPG",
      "IMG_2.JPG",
      folder,
      [2]
    );
    var again = await PutAsync(
      f,
      first,
      "Receipt.jpg",
      "IMG_1.JPG",
      folder,
      [1]
    );

    Assert.Equal(
      ("Receipt.jpg", "IMG_1.JPG", "Dispatch/Loads/2026.09.21 - 1407"),
      (one.Name, one.OriginalName, one.Folder)
    );
    Assert.Equal(
      ("receipt (2).JPG", "IMG_2.JPG"),
      (two.Name, two.OriginalName)
    );
    Assert.Equal((one.Id, one.Name), (again.Id, again.Name));
  }

  [Fact]
  public async Task AStorageHoldingFilesCannotBeDisconnectedSilently()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    await FileStorageTests.Targets(f.Db).DefaultAsync(default);
    var drive = new StorageConnection
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      Kind = StorageKinds.Managed + "-second",
      DisplayName = "Second",
      State = StorageConnectionStates.Connected,
      Revision = 1,
    };
    f.Db.StorageConnections.Add(drive);
    f.Db.StoredFiles.Add(
      new StoredFile
      {
        Id = Guid.NewGuid(),
        CompanyId = Company.Amf,
        ConnectionId = drive.Id,
        ObjectKey = "k",
        ContentType = "application/pdf",
        Name = "POD.pdf",
        Sha256 = new string('a', 64),
        State = StoredFileStates.Uploading,
      }
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();

    var refused = await new StorageConnectionHandlers(
      f.Db,
      FileStorageTests.Targets(f.Db),
      [],
      [],
      [],
      TimeProvider.System
    ).Handle(new DisconnectStorageCommand(drive.Id, 1), default);

    Assert.Equal(409, refused.StatusCode);
    Assert.Contains("moved", refused.Errors![0]);
    Assert.Equal(
      StorageConnectionStates.Connected,
      (
        await f
          .Db.StorageConnections.AsNoTracking()
          .SingleAsync(x => x.Id == drive.Id)
      ).State
    );
  }

  private static Task<StoredFile> PutAsync(
    DispatchSyncFixture f,
    Guid id,
    string name,
    string original,
    string[] folder,
    byte[] bytes
  )
  {
    f.Db.ChangeTracker.Clear();
    return FileStorageTests
      .Store(f.Db)
      .PutAsync(
        new(
          id,
          name,
          "image/jpeg",
          bytes.Length,
          FileStorageTests.Sha(bytes),
          folder,
          original
        ),
        new MemoryStream(bytes),
        default
      );
  }

  private static UpdateStorageLayoutCommand Update(
    string template,
    long revision
  ) => new("/Files//Loads/", template, "Void", "Waiting", revision);

  private static StorageLayouts Layouts(AppDbContext db) =>
    new(db, TimeProvider.System);
}
