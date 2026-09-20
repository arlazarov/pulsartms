// Whether to fuel now or wait is read off both sides of today: what the
// price did since yesterday, and what it does tomorrow. The day before used
// to be compared by opening the map on it and remembering the number.
export function createPriceComparison(discount, today = new Date()) {
  const next = discount?.comparison?.next ? discount.comparison : null;
  const previous = discount?.previous ?? null;
  if (!next && !previous) return null;
  const table = document.createElement('table');
  const caption = document.createElement('caption');
  caption.textContent = [discount.currency, discount.unit]
    .filter(Boolean)
    .join(' / ');
  table.append(caption);
  const head = document.createElement('thead');
  const heading = document.createElement('tr');
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
  // Days run left to right, one column each. What a day's price did since
  // the day before is a small mark under that price, not columns of its own:
  // six columns did not fit where the prices stand, and the table arrived
  // cut off behind a scrollbar.
  for (const label of [
    'Price',
    ...(previous ? [dateLabel(previous.date)] : []),
    dateLabel(previous?.nextDate ?? next.date),
    ...(next ? [dateLabel(next.nextDate)] : []),
  ]) {
    const cell = document.createElement('th');
    cell.scope = 'col';
    cell.textContent = label;
    heading.append(cell);
  }
  head.append(heading);
  const body = document.createElement('tbody');
  for (const [label, field, change, role] of [
    ['Retail', 'retailPrice', 'retailChange', 'retail'],
    ['Your price', 'discountPrice', 'discountChange', 'discount'],
    ['After IFTA', 'priceAfterIfta', 'iftaChange', 'ifta'],
    ['Savings', 'savings', 'savingsChange', 'savings'],
  ]) {
    const row = document.createElement('tr');
    const title = document.createElement('th');
    title.scope = 'row';
    title.textContent = label;
    title.className = `fleet-station-popup__${role}-label`;
    row.append(title);
    const day = (quote, comparison) => {
      const cell = document.createElement('td');
      cell.className = `fleet-station-popup__${role}`;
      const value = document.createElement('span');
      value.textContent = format(quote);
      cell.append(value);
      const moved = comparison?.[change];
      if (moved != null && Number.isFinite(Number(moved)) && moved !== 0) {
        const mark = document.createElement('small');
        mark.className = `fleet-station-popup__change${moved > 0 ? ' is-increase' : ' is-decrease'}${role === 'savings' ? ' is-savings' : ''}`;
        mark.textContent = `${moved > 0 ? '\u25b2' : '\u25bc'}${format(Math.abs(moved))}`;
        const percentage = comparison[`${change}Percent`];
        if (percentage != null && Number.isFinite(Number(percentage)))
          mark.title = `${percentage > 0 ? '+' : ''}${Number(percentage).toFixed(2)}%`;
        cell.append(mark);
      }
      row.append(cell);
    };
    if (previous)
      // Yesterday is what today was before it moved.
      day(
        previous[change] == null || discount[field] == null
          ? null
          : Number(discount[field]) - Number(previous[change]),
        null,
      );
    day(discount[field], previous);
    if (next) day(next.next[field], next);
    body.append(row);
  }
  table.append(head, body);
  return table;
}

function format(value) {
  return value == null || !Number.isFinite(Number(value))
    ? '—'
    : Number(value).toFixed(3);
}
