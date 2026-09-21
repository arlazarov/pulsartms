const maximumInput = 4096,
  maximumReferences = 4;
const reference =
  /(?:^|[.;,\r\n])\s*(?:[-*•]\s*)?(?:(shipper|pick\s*up|receiver|deliver(?:y)?|drop\s*off)(?:['’]s)?\s*:?\s*)?(?:appointment|appt)\b\s*(?:(?:confirmation|confirm|conf\.?)\s*(?:number|no\.?|#)?|number|no\.?|#)\s*[:=#-]?\s*["']?([A-Z0-9][A-Z0-9_/-]{2,63})/gi;
const dateOrOtherId =
  /^(?:\d{1,4}[-/_]\d{1,2}[-/_]\d{1,4}|\d{1,2}[-/]\d{1,2}|\d{1,4}(?:am|pm)|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[-_]?\d{1,2}[-_]?\d{2,4}|\d{1,2}[-_]?(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[-_]?\d{2,4}|(?:bol|load|order)[-_]?\d.*)$/i;

function kind(value: unknown): 'pickup' | 'delivery' | null {
  const normalized = String(value ?? '')
    .replace(/[^a-z]/gi, '')
    .toLowerCase();
  const pickup =
    normalized.includes('pickup') || ['pu', 'shipper'].includes(normalized);
  const delivery =
    normalized.includes('delivery') ||
    normalized.includes('dropoff') ||
    ['del', 'deliver', 'receiver'].includes(normalized);
  return pickup === delivery ? null : pickup ? 'pickup' : 'delivery';
}

function endsClause(notes: string, index: number, limit: number): boolean {
  if (index < limit && ['"', "'"].includes(notes[index])) index++;
  while (index < limit && [' ', '\t'].includes(notes[index])) index++;
  if (index === limit) return notes.length === limit;
  if (!['.', ',', ';', '\r', '\n'].includes(notes[index])) return false;
  return (
    !['.', ','].includes(notes[index]) ||
    index + 1 === notes.length ||
    !/[a-z0-9]/i.test(notes[index + 1])
  );
}

function isReference(value: string): boolean {
  if (!/\d/.test(value) || dateOrOtherId.test(value)) return false;
  if (/^(?:19|20)\d{2}$/.test(value)) return false;
  if (!/^\d{8}$/.test(value)) return true;
  const formats = [
    [value.slice(0, 4), value.slice(4, 6), value.slice(6)],
    [value.slice(4), value.slice(0, 2), value.slice(2, 4)],
    [value.slice(4), value.slice(2, 4), value.slice(0, 2)],
  ];
  return !formats.some(parts => {
    const [year, month, day] = parts.map(Number),
      date = new Date(Date.UTC(year, month - 1, day));
    return (
      year >= 1900 &&
      year <= 2099 &&
      date.getUTCFullYear() === year &&
      date.getUTCMonth() === month - 1 &&
      date.getUTCDate() === day
    );
  });
}

// The appointment numbers written into a stop's notes, for the job that
// stop is.
export function stopAppointmentReference(
  notes: unknown,
  job: unknown,
): string[] {
  if (typeof notes !== 'string' || !notes.length) return [];
  const input = notes.slice(0, maximumInput),
    selectedKind = kind(job),
    values: string[] = [];
  for (const match of input.matchAll(reference)) {
    const [, qualifier, value] = match;
    if (
      (qualifier && kind(qualifier) !== selectedKind) ||
      !isReference(value) ||
      !endsClause(notes, match.index + match[0].length, input.length)
    )
      continue;
    if (
      !values.some(existing => existing.toLowerCase() === value.toLowerCase())
    )
      values.push(value);
    if (values.length === maximumReferences) break;
  }
  return values;
}
