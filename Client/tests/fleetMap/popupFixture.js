// Explicit test port; production uses only the fixed details card.
export function popupFixture(map) {
  const popup = new google.maps.InfoWindow();
  return {
    show(content, position) {
      popup.setContent(content);
      popup.setPosition(position);
      popup.open({ map });
    },
    hide() {
      popup.close();
    },
    dispose() {
      popup.close();
    },
  };
}
