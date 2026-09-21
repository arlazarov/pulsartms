const postalPattern = /^(?:\d{5}(?:-\d{4})?|[A-Z]\d[A-Z]\s?\d[A-Z]\d)$/i;
const regionPattern =
  /^([A-Z]{2})\s*(\d{5}(?:-\d{4})?|[A-Z]\d[A-Z]\s?\d[A-Z]\d)$/i;
const countryPattern = /^(?:US|USA|United States|Canada|CA)$/i;
const unitPattern =
  /^(?:apt|apartment|unit|suite|ste|building|bldg|floor|#)(?:\b|\s|\d)/i;
const regions = new Set(
  (
    'AL AK AZ AR CA CO CT DE DC FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA WA WV WI WY AS GU MP PR VI ' +
    'AB BC MB NB NL NS NT NU ON PE QC SK YT'
  ).split(' '),
);

// An address as the two lines a card shows: the street, and everything
// that places it.
export function addressLines(address: string | null | undefined): {
  street: string;
  locality: string;
} {
  const value = address?.trim() || '';
  const parts = value
    .split(',')
    .map(part => part.trim())
    .filter(Boolean);
  const country = parts.at(-1) || '';
  return (
    (countryPattern.test(country)
      ? split(parts, parts.length - 2, country)
      : null) ||
    split(parts, parts.length - 1, '') || {
      street: parts.join(', '),
      locality: '',
    }
  );
}

function split(
  parts: string[],
  end: number,
  country: string,
): { street: string; locality: string } | null {
  if (end < 2) return null;
  let postal = '',
    region: string | undefined;
  const combined = regionPattern.exec(parts[end]);
  if (combined) {
    [, region, postal] = combined;
    end--;
  } else {
    if (postalPattern.test(parts[end])) postal = parts[end--];
    if (end < 2) return null;
    region = parts[end--];
  }
  const city = parts[end];
  if (
    !regions.has(region.toUpperCase()) ||
    end < 1 ||
    !/\p{L}/u.test(city) ||
    /^\d/.test(city) ||
    regions.has(city.toUpperCase()) ||
    countryPattern.test(city) ||
    postalPattern.test(city) ||
    regionPattern.test(city) ||
    unitPattern.test(city)
  )
    return null;
  return {
    street: parts.slice(0, end).join(', '),
    locality: [city, [region, postal].filter(Boolean).join(' '), country]
      .filter(Boolean)
      .join(', '),
  };
}
