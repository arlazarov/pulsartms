// Whether to fuel now or wait is read off both sides of today: what the
// price did since yesterday, and what it does tomorrow. One price decides
// that - the one being paid - so the days are a single line of it under the
// price list, each day a name, the price, and what it did. A table of every
// price by day was tried first, and it was a grid of rules with rows of two
// heights and a row of dashes; what retail and the savings did follows from
// this number and only made noise.
// What the price did on either side of today, as a line under the prices.
export function createPriceComparison(
  discount: any,
  today = new Date(),
): HTMLElement | null {
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
  const dateLabel = (date: string) => {
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
  const day = (
    date: string,
    price: number | null,
    comparison: any,
    current = false,
  ) => {
    const cell = document.createElement('div');
    cell.className = `fleet-station-popup__day${current ? ' is-current' : ''}`;
    const label = document.createElement('span');
    label.className = 'fleet-station-popup__day-label';
    label.textContent = dateLabel(date);
    const value = document.createElement('strong');
    value.className = 'fleet-station-popup__day-price';
    value.textContent = format(price);
    // A day says what the price did on it; the first day of the line has
    // nothing behind it to have moved from, and says nothing. The days sit
    // on one line now, on one baseline, so a day without a mark is not a
    // cell of another height - it is a shorter phrase.
    const moved = comparison?.discountChange;
    const known = moved != null && Number.isFinite(Number(moved));
    cell.append(label, value);
    if (known) {
      const mark = document.createElement('small');
      mark.className = `fleet-station-popup__change${moved > 0 ? ' is-increase' : moved < 0 ? ' is-decrease' : ''}`;
      mark.textContent =
        moved === 0
          ? 'same'
          : `${moved > 0 ? '\u25b2' : '\u25bc'} ${format(Math.abs(moved))}`;
      const percentage = comparison?.discountChangePercent;
      if (moved !== 0 && Number.isFinite(Number(percentage)))
        mark.title = `${percentage > 0 ? '+' : ''}${Number(percentage).toFixed(2)}%`;
      cell.append(mark);
    }
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

function format(value: unknown): string {
  return value == null || !Number.isFinite(Number(value))
    ? '—'
    : Number(value).toFixed(3);
}
