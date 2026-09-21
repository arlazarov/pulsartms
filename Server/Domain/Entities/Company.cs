namespace Domain.Entities;

// A carrier using the product. Everything a carrier does - its loads, its
// trucks, its drivers, its fuel, its settings - belongs to exactly one of
// these, and nothing a carrier does is ever visible to another.
public sealed class Company : BaseEntity
{
  // The carrier this system was built for. It exists so that the rows that
  // are already here have an owner: every one of them is AMF's.
  public static readonly Guid Amf = new("a0f0a0f0-0000-4000-8000-000000000001");

  public string Key { get; set; } = "";
  public string Name { get; set; } = "";
  public bool IsActive { get; set; } = true;
  public DateTime CreatedAt { get; set; }
}
