using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchWorkspaceImportTests
{
  [Fact]
  public void CommercialOverridesDoNotFreezeOtherImportedFields()
  {
    var load = new DispatchEntity
    {
      OrderNumber = "original",
      CustomerName = "Customer",
      Price = 1000m,
      Currency = "USD",
    };
    var local = new DispatchWorkspaceMetadata
    {
      OrderNumber = "local reference",
      CustomerName = load.CustomerName,
      Price = load.Price,
      Currency = load.Currency,
      BrokerContact = "Local contact",
    };
    var workspace = new DispatchWorkspace
    {
      OwnsCommercial = true,
      SourceCommercialJson = DispatchWorkspaceData.Write(
        DispatchWorkspaceData.Commercial(load)
      ),
      MetadataJson = DispatchWorkspaceData.Write(local),
    };
    foreach (var price in new[] { 1200m, 1300m, 1000m })
    {
      load.OrderNumber = "provider reference";
      load.CustomerName = "Updated customer";
      load.Price = price;
      DispatchWorkspaceImport.RestoreCommercial(load, workspace);
      Assert.Equal("local reference", load.OrderNumber);
      Assert.Equal("Updated customer", load.CustomerName);
      Assert.Equal(price, load.Price);
      var metadata = DispatchWorkspaceData.Read<DispatchWorkspaceMetadata>(
        workspace.MetadataJson
      );
      Assert.Equal("Local contact", metadata.BrokerContact);
    }
  }

  [Fact]
  public void RateAndCurrencyRemainOneOverride()
  {
    var load = new DispatchEntity { Price = 1000m, Currency = "USD" };
    var workspace = new DispatchWorkspace
    {
      OwnsCommercial = true,
      SourceCommercialJson = DispatchWorkspaceData.Write(
        DispatchWorkspaceData.Commercial(load)
      ),
      MetadataJson = DispatchWorkspaceData.Write(
        new DispatchWorkspaceMetadata { Price = 1500m, Currency = "USD" }
      ),
    };
    for (var iteration = 0; iteration < 2; iteration++)
    {
      load.Price = 1400m;
      load.Currency = "CAD";
      DispatchWorkspaceImport.RestoreCommercial(load, workspace);
      Assert.Equal(1500m, load.Price);
      Assert.Equal("USD", load.Currency);
    }
  }

  [Fact]
  public void NotesOverrideKeepsCargoAndReferencesUpdatingAcrossImports()
  {
    var (load, workspace, source) = Fixture();
    var stop = load.Stops[0];
    stop.Notes = "Dispatcher instructions";
    stop.AppointmentTimeZoneId = "America/Toronto";
    foreach (var revision in new[] { "New", "Newest", "" })
    {
      source.Notes = "Imported instructions";
      source.StopNo = revision;
      source.Commodity = revision;
      source.Weight = 25000m;
      source.WeightUnit = "lb";
      Merge(load, workspace, source);
      Assert.Equal("Dispatcher instructions", stop.Notes);
      Assert.Equal(revision, stop.StopNo);
      Assert.Equal(revision, stop.Commodity);
      Assert.Equal(25000m, stop.Weight);
      Assert.Equal("America/Toronto", stop.AppointmentTimeZoneId);
      Assert.Null(workspace.SourceReviewReason);
    }
  }

  [Fact]
  public void LocalCargoGroupCannotBePartiallyReplaced()
  {
    var (load, workspace, source) = Fixture();
    load.Stops[0].Weight = 40000m;
    load.Stops[0].WeightUnit = "lb";
    source.Weight = 18000m;
    source.WeightUnit = "kg";
    source.Commodity = "Different cargo";
    source.Notes = "New provider instructions";
    Merge(load, workspace, source);
    Merge(load, workspace, source);
    Assert.Equal(40000m, load.Stops[0].Weight);
    Assert.Equal("lb", load.Stops[0].WeightUnit);
    Assert.Equal("", load.Stops[0].Commodity);
    Assert.Equal(source.Notes, load.Stops[0].Notes);
  }

  [Fact]
  public void SourceScheduleUpdatesDespiteUnrelatedLocalEdits()
  {
    var (load, workspace, source) = Fixture();
    source.ScheduledDate = new(2026, 9, 16);
    source.ScheduledTime = new(10, 0);
    load.Stops[0].Notes = "Local instructions";
    source.ArrivedAt = new(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc);
    Merge(load, workspace, source);
    Merge(load, workspace, source);
    Assert.Equal(source.ScheduledDate, load.Stops[0].ScheduledDate);
    Assert.Equal(source.ScheduledTime, load.Stops[0].ScheduledTime);
    Assert.Equal(source.ArrivedAt, load.Stops[0].ArrivedAt);
    Assert.Equal("Local instructions", load.Stops[0].Notes);
    Assert.Null(workspace.SourceReviewReason);
  }

  [Fact]
  public void LocalAppointmentWindowSurvivesRepeatedSourceChanges()
  {
    var (load, workspace, source) = Fixture();
    var stop = load.Stops[0];
    stop.ScheduledDate = new(2026, 9, 17);
    stop.ScheduledTime = new(9, 0);
    stop.ScheduledDate2 = new(2026, 9, 18);
    stop.ScheduledTime2 = new(17, 0);
    stop.IsWindow = true;
    var local = StopAppointment.From(stop);
    foreach (var day in new[] { 16, 19, 20 })
    {
      source.ScheduledDate = new(2026, 9, day);
      source.ScheduledTime = new(10, 0);
      Merge(load, workspace, source);
      Assert.Equal(local, StopAppointment.From(stop));
      var baseline = Assert.Single(
        DispatchWorkspaceData.Read<List<DispatchStop>>(
          workspace.SourceStopsJson
        )
      );
      Assert.Equal(source.ScheduledDate, baseline.ScheduledDate);
      Assert.Null(workspace.SourceReviewReason);
    }
  }

  [Fact]
  public void ContradictoryActualIsRetainedAndRequiresReview()
  {
    var (load, workspace, source) = Fixture();
    var recorded = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
    load.Stops[0].DeliveredAt = recorded;
    source.DeliveredAt = recorded.AddHours(1);
    Merge(load, workspace, source);
    Assert.Equal(recorded, load.Stops[0].DeliveredAt);
    Assert.Contains("actuals conflict", workspace.SourceReviewReason);
  }

  private static void Merge(
    DispatchEntity load,
    DispatchWorkspace workspace,
    ExternalDispatchStop source
  ) =>
    DispatchWorkspaceImport.MergeStops(
      load,
      workspace,
      [source],
      new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc)
    );

  private static (
    DispatchEntity Load,
    DispatchWorkspace Workspace,
    ExternalDispatchStop Source
  ) Fixture()
  {
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Sequence = 1,
      Job = "Delivery",
      Name = "Facility",
      Address = "1 Main St",
      City = "Fixture",
      Country = "US",
      Latitude = 40m,
      Longitude = -80m,
    };
    var load = new DispatchEntity { Stops = [stop] };
    var workspace = new DispatchWorkspace
    {
      OwnsStops = true,
      SourceStopsJson = ExecutionSnapshots.Write(load.Stops),
    };
    var source = DispatchWorkspaceData.Read<ExternalDispatchStop>(
      DispatchWorkspaceData.Write(stop)
    );
    return (load, workspace, source);
  }
}
