import type { MapPlan, MapPoint } from '../contracts.d.ts';

// Which truck's plan is being edited. A camera move meant for one plan must
// not land after the page has moved on to another.
type PlanIdentity = { truckId?: string; dispatchId?: string };

/**
 * The camera and the keyboard while a fuel plan is being edited.
 *
 * Two things have to wait a frame. The camera does, because Blazor selects
 * a station before it has committed the layout the editor expands into, so
 * panning immediately pans to a viewport that is about to change. Focus
 * does, because the editor closes by being removed, and where focus lands
 * after that is only known once it has.
 */
export function createFuelEditorFocus(
  // The map's own element: where focus goes when the control that opened
  // the editor has gone with it.
  element: HTMLElement,
  {
    disposed,
    editing,
    plan,
    routeEditing,
    centerOn,
    fitRoute,
  }: {
    disposed: () => boolean;
    editing: () => boolean;
    plan: () => MapPlan | null;
    routeEditing: () => boolean;
    centerOn: (position: MapPoint) => void;
    fitRoute: () => void;
  },
) {
  const viewport = element.ownerDocument?.defaultView;
  let focusFrame: number | null = null;
  let focusVersion = 0;
  let opener: Element | null = null;
  let returnFrame: number | null = null;

  const sameWork = (identity: PlanIdentity) =>
    identity.truckId === plan()?.truckId &&
    identity.dispatchId === plan()?.dispatchId;

  function cancelReturn() {
    if (returnFrame !== null) viewport?.cancelAnimationFrame(returnFrame);
    returnFrame = null;
  }

  // Anything already asked for is no longer wanted: the version moves, and
  // a frame that runs anyway finds it is not the current one.
  function cancel() {
    focusVersion++;
    if (focusFrame !== null) viewport?.cancelAnimationFrame(focusFrame);
    focusFrame = null;
  }

  const afterFrame = (run: () => void) => {
    if (viewport?.requestAnimationFrame)
      focusFrame = viewport.requestAnimationFrame(run);
    else run();
  };

  return {
    cancel,
    cancelReturn,
    // The editor is about to open, so what opened it is remembered.
    captureOpener() {
      cancelReturn();
      opener = element.ownerDocument?.activeElement ?? null;
    },
    forgetOpener() {
      opener = null;
    },
    // The editor has closed. Focus goes back to what opened it, unless the
    // page has already put it somewhere of its own.
    restoreFocus() {
      const document = element.ownerDocument;
      const target = opener as (HTMLElement & { disabled?: boolean }) | null;
      opener = null;
      const closingFocus = document?.activeElement;
      if (!target || !closingFocus?.closest?.('.fuel-plan-editor')) return;
      cancelReturn();
      const restore = () => {
        returnFrame = null;
        if (
          disposed() ||
          document.querySelector('.fuel-plan-editor') ||
          (document.activeElement !== document.body &&
            document.activeElement !== closingFocus)
        )
          return;
        const connected =
          target.isConnected && !target.closest?.('[inert]') && !target.disabled;
        (connected ? target : element).focus?.({ preventScroll: true });
      };
      if (viewport?.requestAnimationFrame)
        returnFrame = viewport.requestAnimationFrame(restore);
      else restore();
    },
    // A station has been picked in the editor: bring it into the part of
    // the map the editor has left.
    focusOn(position: MapPoint, station: PlanIdentity) {
      cancel();
      const version = focusVersion;
      afterFrame(() => {
        if (
          disposed() ||
          version !== focusVersion ||
          !editing() ||
          !sameWork(station)
        )
          return;
        focusFrame = null;
        centerOn(position);
      });
    },
    // The editor has closed on the same work it opened on, so the road
    // still to drive comes back into view.
    returnToRoute(identity: PlanIdentity) {
      const version = focusVersion;
      afterFrame(() => {
        if (
          disposed() ||
          version !== focusVersion ||
          editing() ||
          routeEditing() ||
          !sameWork(identity) ||
          plan()?.tracking?.allStopsPassed
        )
          return;
        focusFrame = null;
        fitRoute();
      });
    },
    release() {
      cancelReturn();
      opener = null;
      cancel();
    },
  };
}
