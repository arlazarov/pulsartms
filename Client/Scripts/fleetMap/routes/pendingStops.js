export function orderedStops(plan) {
  return [
    ...new Map(
      [...(plan?.referenceStops || []), ...(plan?.stops || [])].map(stop => [
        stop.id,
        stop,
      ]),
    ).values(),
  ];
}

export function pendingStops(plan) {
  const passed = new Set(plan?.tracking?.passedStopIds || []);
  return orderedStops(plan).filter(stop => !passed.has(stop.id));
}
