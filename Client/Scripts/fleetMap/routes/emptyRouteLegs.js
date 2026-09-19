export function emptyRouteLegs(plan) {
  return (plan?.route?.legs ?? []).map((_, index) =>
    ['Empty', 'Bobtail'].includes(plan.segments?.[index]?.cargoState),
  );
}
