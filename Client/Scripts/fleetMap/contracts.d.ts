export interface RoutePoint { latitude: number; longitude: number }
export interface MapPoint { lat: number; lng: number }
export interface RouteLeg { miles: number; points: RoutePoint[] }
export interface RouteGeometry { legs: RouteLeg[] }
export interface PlanStop {
  id: string;
  point: RoutePoint;
  name?: string;
  address?: string;
  job?: string;
  scheduledDate?: string;
  scheduledTime?: string;
  scheduledDate2?: string;
  scheduledTime2?: string;
  commodity?: string;
  notes?: string;
}
export interface RouteTracking { nextStopId?: string; passedStopIds?: string[] }
export interface FuelStop { stationId: string; point?: RoutePoint; name?: string; address?: string; yourPrice?: number; currency?: string; dispatchId?: string | null; beforeStopId?: string | null; number?: number; visitKey?: string; buyGallons: number; arrivalGallons?: number | null; departureGallons?: number | null; purchaseCostUsd?: number | null; unit?: string; fillToTarget?: boolean; currentRouteMile?: number | null; milesAhead?: number | null }
export interface RouteMetadata {
  id: string;
  dispatchId?: string;
  version: number;
  truckId: string;
  fromCurrentPosition?: boolean;
  stops: PlanStop[];
  referenceStops?: PlanStop[] | null;
  tracking?: RouteTracking | null;
  fuelPlan?: { needsRefresh?: boolean; stops: FuelStop[]; stopArrivals?: { dispatchId: string; stopId: string; gallons: number; percent: number }[] } | null;
  tankGallons?: number | null;
}
export interface MapPlan extends RouteMetadata {
  geometryOmitted?: false;
  route: RouteGeometry;
  referenceRoute?: RouteGeometry | null;
}
export interface MetadataPayload extends RouteMetadata { geometryOmitted: true }
export type RoutePayload = MapPlan | MetadataPayload | null;
export interface RouteProgress { progressMiles?: number | null }
export interface NextLoadStop extends RoutePoint { id?: string; name?: string; job?: string }
export interface StopHoursAlternative {
  kind: 'recap' | 'restart'; arrival: string; departure: string; lateMinutes?: number | null;
  cycleAfterStopMinutes: number; restStartedAt?: string | null; resumeAt?: string | null;
}
export interface StopHoursForecast {
  cycleAtArrivalMinutes?: number | null; cycleAfterStopMinutes?: number | null;
  drivingShortfallMinutes?: number | null; firstCycleShortageAt?: string | null;
  cycleVerified: boolean; alternatives: StopHoursAlternative[]; unavailableReason?: string | null;
}
export interface StopCycleForecast {
  remainingMinutes: number; nextRecapAt?: string | null; nextRecapMinutes?: number | null;
  homeTimeZoneId?: string | null; recapVerified: boolean;
}
export interface StopEta {
  stopId: string; dispatchId: string; arrival: string; timeZoneId: string;
  appointment?: string | null; lateMinutes?: number | null; hours?: StopHoursForecast | null;
}
export interface DispatchEta {
  validUntil: string; stops: StopEta[]; routeUpdatePending?: boolean;
  cycleAtCalculation?: StopCycleForecast | null;
}
export interface NextLoad {
  id?: string;
  loadNumber: number;
  stops: NextLoadStop[];
  stopCount?: number;
  legs?: RouteLeg[];
  deadhead?: { miles?: number; points?: RoutePoint[] };
}
export interface NextLoadLabels { id: string; names: string[] }
/** Blazor transports UTF-8 JSON bytes; geometry omission is accepted only for a retained matching plan. */
export interface FleetRouteInterop {
  setLoadReference(reference: { dispatchId: string; loadNumber: number; loadLabel?: string; orderNumber?: string } | null): void;
  setRouteBytes(bytes: Uint8Array, progress: RouteProgress | null, fit: boolean): Promise<boolean>;
  setRoute(plan: RoutePayload, progress: RouteProgress | null, fit: boolean): Promise<boolean>;
  setNextLoadsBytes(bytes: Uint8Array): void;
}
