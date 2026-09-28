// What a load's stop badges say, in the load's stop order: P for a pickup;
// D for the load's only delivery, D1, D2, ... when it has several. A stop
// that neither loads nor unloads (a trailer hook or drop, a switch) keeps
// its place in the load. A badge never says which load of the chain it
// belongs to; the trip chain numbers loads, and only it does.
//
// The words are the ones every Dispatch view reads
// (Shared/Dispatch/DispatchStopPresentation.IsPickup / IsDelivery).
const words = (job: unknown) =>
  String(job ?? '')
    .trim()
    .split(/\s+/)
    .join(' ')
    .toUpperCase();

export const isPickupJob = (job: unknown) =>
  ['PICKUP', 'PICK UP'].includes(words(job));

export const isDeliveryJob = (job: unknown) =>
  ['DELIVERY', 'DROP OFF', 'DROPOFF'].includes(words(job));

export function tripStopLabels(jobs: readonly unknown[]): string[] {
  const deliveries = jobs.filter(isDeliveryJob).length;
  let delivery = 0;
  return jobs.map((job, index) => {
    if (isPickupJob(job)) return 'P';
    if (isDeliveryJob(job)) return deliveries === 1 ? 'D' : `D${++delivery}`;
    return String(index + 1);
  });
}

// Every character a badge can say, for the text atlas that draws them: a
// character missing from it is drawn as nothing.
export const stopBadgeCharacters = 'PD0123456789/ ·';
