// The only API routes the concept host forwards. Each one is a read the
// maintained Fleet Map or Dispatch page already makes (see README.md);
// the POST entries are server queries, not commands. Auth routes are the
// normal sign-in, token refresh and sign-out.
const id = '[0-9a-f-]{36}';

export const routes = [
  ['POST', /^\/api\/auth\/(login|refresh|logout)$/],
  ['GET', /^\/api\/auth\/me$/],
  ['GET', /^\/api\/fleet\/locations$/],
  ['GET', /^\/api\/fleet\/hos$/],
  ['GET', /^\/api\/settings\/planning$/],
  ['GET', new RegExp(`^/api/fleet/trucks/${id}/planning/preview$`)],
  ['POST', new RegExp(`^/api/fleet/trucks/${id}/planning$`)],
  ['POST', new RegExp(`^/api/dispatch/${id}/planning/automatic$`)],
  ['GET', new RegExp(`^/api/dispatch/truck/${id}/next-routes$`)],
  ['GET', new RegExp(`^/api/dispatch/truck/${id}$`)],
  ['GET', new RegExp(`^/api/dispatch/${id}$`)],
  ['GET', /^\/api\/dispatch$/],
  ['GET', /^\/api\/dispatch\/board$/],
  ['GET', /^\/api\/dispatch\/board\/(enrichment|telemetry)$/],
  ['POST', /^\/api\/dispatch\/board\/planning$/],
  // Further reads the real Client makes on these pages (CONCEPT_ROOT).
  ['GET', /^\/api\/settings\/(dispatch|appearance)$/],
  ['GET', /^\/api\/driver-groups$/],
  ['GET', /^\/api\/messaging\/(unread|changes)$/],
  ['GET', /^\/api\/fuel\/(price-overview|stations)$/],
  ['GET', /^\/api\/fleet\/planning\/previews$/],
  ['GET', new RegExp(`^/api/fleet/trucks/${id}/weather$`)],
  ['GET', new RegExp(`^/api/dispatch/${id}/(workspace|planning/map)$`)],
];
