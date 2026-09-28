// On a phone the map moves only when its reader moves it (the owner,
// September 28): no automatic fit or recentring for a selection, a chosen
// stop, or a card that opens, folds or resizes. Explicit controls (Fit
// route, Follow, a double press) still move it. The same width as the
// styles' map-mobile breakpoint (Styles/base/tokens/_screen.scss).
export function phoneViewport(view?: Window | null) {
  return view?.matchMedia?.('(width < 768px)')?.matches === true;
}
