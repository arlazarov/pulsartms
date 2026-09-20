// Whether to fuel now or wait is read off both sides of today: what the
// price did since yesterday, and what it does tomorrow. One price decides
// that - the one being paid - so the days are a single line of it under the
// price list. A table of every price by day was tried first, and it was a
// grid of rules with rows of two heights and a row of dashes; what retail
// and the savings did follows from this number and only made noise.
export function createPriceComparison(discount, today = new Date()) {
  const next = discount?.comparison?.next ? discount.comparison : null;
  const previous = discount?.previous ?? null;
  if (!next && !previous) return null;
  const strip = document.createElement('div');
  strip.className = 'fleet-station-popup__days';
  const currentDay = Date.UTC(
    today.getFullYear(),
    today.getMonth(),
    today.getDate(),
  );
  const dateLabel = date => {
    const day = new Date(`${date}T00:00:00Z`);
    const offset = (day.getTime() - currentDay) / 86400000;
    return (
      { '-1': 'Yesterday', 0: 'Today', 1: 'Tomorrow' }[offset] ??
      new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric',
        timeZone: 'UTC',
      }).format(day)
    );
  };
  const day = (date, price, comparison, current = false) => {
    const cell = document.createElement('div');
    cell.className = `fleet-station-popup__day${current ? ' is-current' : ''}`;
    const label = document.createElement('span');
    label.className = 'fleet-station-popup__day-label';
    label.textContent = dateLabel(date);
    const value = document.createElement('strong');
    value.className = 'fleet-station-popup__day-price';
    value.textContent = format(price);
    // Every day carries the line for its mark, moved or not, so the three
    // stand at one height and nothing in the row steps up or down.
    const mark = document.createElement('small');
    const moved = comparison?.discountChange;
    const known = moved != null && Number.isFinite(Number(moved));
    mark.className = `fleet-station-popup__change${known && moved > 0 ? ' is-increase' : known && moved < 0 ? ' is-decrease' : ''}`;
    mark.textContent = !known
      ? '\u00a0'
      : moved === 0
        ? 'same'
        : `${moved > 0 ? '\u25b2' : '\u25bc'} ${format(Math.abs(moved))}`;
    const percentage = comparison?.discountChangePercent;
    if (known && moved !== 0 && Number.isFinite(Number(percentage)))
      mark.title = `${percentage > 0 ? '+' : ''}${Number(percentage).toFixed(2)}%`;
    cell.append(label, value, mark);
    strip.append(cell);
  };
  const price = discount.discountPrice;
  if (previous)
    // Yesterday is what today was before it moved.
    day(
      previous.date,
      previous.discountChange == null || price == null
        ? null
        : Number(price) - Number(previous.discountChange),
      null,
    );
  day(previous?.nextDate ?? next.date, price, previous, true);
  if (next) day(next.nextDate, next.next.discountPrice, next);
  return strip;
}

function format(value) {
  return value == null || !Number.isFinite(Number(value))
    ? '—'
    : Number(value).toFixed(3);
}
