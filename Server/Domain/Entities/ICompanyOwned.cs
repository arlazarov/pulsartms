namespace Domain.Entities;

// A row that belongs to one carrier.
//
// Saying so is the whole of it: the database context reads this interface
// and does the rest - it filters every query to the carrier asking, and
// stamps every new row with the carrier that wrote it. Nothing has to
// remember to add "where CompanyId = ..." because nothing is given the
// chance to forget.
//
// What does not carry this is shared on purpose, and there is a list of
// those with a reason beside each. A table that is neither is a mistake,
// and a test says so.
public interface ICompanyOwned
{
  Guid CompanyId { get; set; }
}
