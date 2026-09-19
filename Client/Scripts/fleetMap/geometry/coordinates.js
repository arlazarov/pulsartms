export function coordinates(latitude, longitude) {
  if (latitude == null || longitude == null) return null;
  const lat = Number(latitude);
  const lng = Number(longitude);
  return Number.isFinite(lat) &&
    Math.abs(lat) <= 90 &&
    Number.isFinite(lng) &&
    Math.abs(lng) <= 180
    ? { lat, lng }
    : null;
}
