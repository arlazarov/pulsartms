/** @param {number} miles */
export function distanceLabel(miles) {
  return `${Math.round(miles)} mi · ${Math.round(miles * 1.609344)} km`;
}
