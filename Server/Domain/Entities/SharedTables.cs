namespace Domain.Entities;

// The tables that deliberately belong to nobody.
//
// Every other table belongs to a carrier and carries ICompanyOwned. These
// four do not, and each has a reason that is about the data rather than
// about convenience. A table that is on neither side is an oversight, and
// a test refuses it.
public static class SharedTables
{
  public static readonly IReadOnlyDictionary<string, string> Reasons =
    new Dictionary<string, string>
    {
      // A fuel station is a place in the world. Two carriers pulling into
      // the same truck stop are at the same truck stop, and its prices are
      // the same for both. What differs is the discount a carrier has
      // negotiated, and that is FuelDiscount, which does belong to one.
      ["FuelStation"] = "a place in the world, the same for everyone",
      // Set by governments, not by us.
      ["IftaTaxRate"] = "published by jurisdictions, not by a carrier",
      // A road from one point to another is the same road whoever asked
      // for it, and it was paid for once. Keyed by a hash of the request,
      // so nothing about who asked is in it. Sharing this is what stops
      // the same road being bought twice.
      ["RoutingApiCall"] = "a road is the same road whoever asked",
      // A work queue is the server's own list. A worker takes whatever is
      // next, whoever it is for, and then runs that pass as them - so the
      // row names a carrier without belonging to one. Filtering the queue
      // by carrier would stop a worker seeing the work it is meant to do.
      ["PlanningRefreshRequest"] =
        "the server's work list; each row names a carrier",
      ["SourceRoadRequest"] =
        "the server's work list; each row names a carrier",
      ["ExecutionPlanningChange"] =
        "the server's work list; each row names a carrier",
      // Written by a database trigger, which has never heard of carriers,
      // and read by the publication lock. It counts changes to one truck's
      // planning inputs - a counter beside the data, not part of it.
      ["PlanningInputRevision"] =
        "a change counter kept by a database trigger, one per truck",
      // The lease that decides which instance synchronizes, and the state
      // of its stages. Claimed before any carrier is chosen - it is what
      // decides whether this instance works at all - so filtering it by
      // carrier hid the existing row and the claim tried to insert a
      // second one over the same primary key.
      ["SynchronizationCheckpoint"] =
        "the lease that picks the working instance",
      // Where the server has read up to in the odometer feed it polls.
      // One row with a fixed id, taken before a carrier is chosen. When a
      // second carrier brings its own telematics account this has to
      // become one cursor per account - a fixed id cannot hold two.
      ["OdometerCaptureCheckpoint"] =
        "the server's position in the feed it polls",
      // How server instances tell each other to drop what they have
      // cached. About the servers, not about any carrier's work.
      ["CacheInvalidation"] = "server talking to server",
    };

  // Rows that are part of a carrier's row rather than rows of their own:
  // a crossing's crew, a shipment's commodities. EF reaches them only
  // through the parent, which is filtered, and refuses a filter on them
  // directly. They are a carrier's, just not separately.
  public static readonly IReadOnlyDictionary<string, string> PartOfAnotherRow =
    new Dictionary<string, string>
    {
      ["BorderCrew"] = "part of a border crossing",
      ["BorderEquipment"] = "part of a border crossing",
      ["BorderShipment"] = "part of a border crossing",
      ["ShipmentCommodity"] = "part of a shipment",
      ["ShipmentParty"] = "part of a shipment or a crossing",
    };
}
