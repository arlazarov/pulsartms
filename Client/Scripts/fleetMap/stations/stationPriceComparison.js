export function createPriceComparison(discount, today = new Date()) {
  const comparison = discount?.comparison;
  if (!comparison?.next) return null;
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
  for (const label of [
    'Price',
    dateLabel(comparison.date),
    dateLabel(comparison.nextDate),
    'Change',
    'Change %',
  ]) {
    const cell = document.createElement('th');
    cell.scope = 'col';
    cell.textContent = label;
    heading.append(cell);
  }
  head.append(heading);
  const body = document.createElement('tbody');
  for (const [label, field, change, role] of [
    ['Retail price', 'retailPrice', 'retailChange', 'retail'],
    ['Your price', 'discountPrice', 'discountChange', 'discount'],
    ['Price after IFTA', 'priceAfterIfta', 'iftaChange', 'ifta'],
    ['Savings', 'savings', 'savingsChange', 'savings'],
  ]) {
    const row = document.createElement('tr');
    const title = document.createElement('th');
    title.scope = 'row';
    title.textContent = label;
    title.className = `fleet-station-popup__${role}-label`;
    row.append(title);
    const value = comparison[change];
    const changeClass = `fleet-station-popup__change${value > 0 ? ' is-increase' : value < 0 ? ' is-decrease' : ''}${role === 'savings' ? ' is-savings' : ''}`;
    for (const [index, quote] of [
      discount[field],
      comparison.next[field],
    ].entries()) {
      const cell = document.createElement('td');
      cell.textContent = format(quote);
      cell.className =
        index === 0 ? `fleet-station-popup__${role}` : changeClass;
      row.append(cell);
    }
    const delta = document.createElement('td');
    delta.className = changeClass;
    delta.textContent =
      value == null ? '—' : `${value > 0 ? '+' : ''}${format(value)}`;
    row.append(delta);
    const percent = document.createElement('td');
    const percentage = comparison[`${change}Percent`];
    percent.className = delta.className;
    percent.textContent =
      percentage == null || !Number.isFinite(Number(percentage))
        ? '—'
        : `${percentage > 0 ? '+' : ''}${Number(percentage).toFixed(2)}%`;
    row.append(percent);
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
