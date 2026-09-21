import type { MapPlan } from '../contracts.d.ts';

// Which legs of a plan are driven with nothing in the trailer.
export function emptyRouteLegs(plan: MapPlan | null | undefined): boolean[] {
  return (plan?.route?.legs ?? []).map((_, index) =>
    ['Empty', 'Bobtail'].includes(
      plan?.segments?.[index]?.cargoState as string,
    ),
  );
}
