using Client.Services;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class StopAppointmentReferenceTests
{
  private const string Shared =
    "Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Service for Load sentinel.";

  [Fact]
  public void ExplicitReferencesRespectTheSelectedPickupOrDeliveryRole()
  {
    Assert.Equal(
      ["PU123456"],
      StopAppointmentReference.Extract(Shared, "Pick Up")
    );
    Assert.Equal(
      ["DL654321"],
      StopAppointmentReference.Extract(Shared, "Drop Off")
    );
    Assert.Empty(StopAppointmentReference.Extract(Shared, "unknown"));
    const string example =
      "Shipper BOL: 42845601. Receiver appointment confirmation number: T380213479058.";
    Assert.Equal(
      ["T380213479058"],
      StopAppointmentReference.Extract(example, "Delivery")
    );
    Assert.Empty(StopAppointmentReference.Extract(example, "Pickup"));
    Assert.Equal(
      ["DL1234"],
      StopAppointmentReference.Extract(
        "Receiver's appointment confirmation number: DL1234",
        "Delivery"
      )
    );
  }

  [Fact]
  public void GenericReferencesBelongToTheStopAndPreserveDistinctExactIds()
  {
    const string notes =
      "Appointment #: AP123456; appt no. 'ABC-123'; Appointment confirmation: AP123456. Appt # abc-123.";
    Assert.Equal(
      ["AP123456", "ABC-123"],
      StopAppointmentReference.Extract(notes, "Pickup")
    );
    Assert.Equal(
      ["AP123456", "ABC-123"],
      StopAppointmentReference.Extract(notes, "Delivery")
    );
    Assert.Equal(
      ["PU-98765"],
      StopAppointmentReference.Extract(
        "Pickup: Appt confirmation #: PU-98765. Delivery: Appointment no: DL_54321.",
        "Pickup"
      )
    );
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("Shipper BOL: 42845601. Load number: 1373. Order: 565606595.")]
  [InlineData("Service for Load. General information.")]
  [InlineData("Use appointment number: 123456.")]
  [InlineData("Appointment: 123456.")]
  [InlineData("Appt #: 2026-09-09.")]
  [InlineData("Appt #: 09/09/2026.")]
  [InlineData("Appt #: 20260909.")]
  [InlineData("Appt #: 09:00.")]
  [InlineData("Appt #: Sep92026.")]
  [InlineData("Appt #: 2026.")]
  [InlineData("Appt #: TBD.")]
  [InlineData("Appt #: 12345 confirmed.")]
  [InlineData("Appt #: BOL12345.")]
  [InlineData("Appt #: LOAD12345.")]
  [InlineData("Appt #: ORDER12345.")]
  [InlineData("Appt #: A123.45.")]
  [InlineData("Shipper (pickup) appointment number: PU1234.")]
  [InlineData("Appt #: <img src=x onerror=alert(1)>.")]
  public void OtherIdsDatesTimesAndAmbiguousProseNeverBecomeAppointmentReferences(
    string? notes
  )
  {
    Assert.Empty(StopAppointmentReference.Extract(notes, "Pickup"));
  }

  [Fact]
  public void BoundedScansNeverPublishAPartialIdentifier()
  {
    Assert.Equal(
      ["AP1230", "AP1231", "AP1232", "AP1233"],
      StopAppointmentReference.Extract(
        string.Join(
          " ",
          Enumerable.Range(0, 6).Select(i => $"Appt #: AP123{i}.")
        ),
        "Pickup"
      )
    );
    Assert.Empty(
      StopAppointmentReference.Extract(
        $"Appt #: {new string('A', 64)}123.",
        "Pickup"
      )
    );
    var prefix =
      new string(' ', 4096 - "Appt #: AP123".Length) + "Appt #: AP123";
    Assert.Empty(StopAppointmentReference.Extract(prefix + "456.", "Pickup"));
    Assert.Empty(
      StopAppointmentReference.Extract(
        new string(' ', 4096) + "Appt #: AP123456.",
        "Pickup"
      )
    );
  }
}
