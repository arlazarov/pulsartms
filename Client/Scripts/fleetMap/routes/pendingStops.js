export function pendingStops(plan) {
  const passed = new Set(plan?.tracking?.passedStopIds || []);
  return [...new Map([...(plan?.referenceStops || []), ...(plan?.stops || [])].map(stop => [stop.id, stop])).values()]
    .filter(stop => !passed.has(stop.id));
}
