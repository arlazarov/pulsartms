export function loadReferenceContent(reference) {
  const header = document.createElement('div');
  header.className = 'fleet-map-route-info__load';
  const status = document.createElement('span');
  status.className = 'visually-hidden';
  status.setAttribute('role', 'status');
  let copyVersion = 0;

  function number(label, value, display) {
    const caption = document.createElement('span');
    caption.textContent = label;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'fleet-map-route-info__copy-number';
    button.title = `Copy ${label.startsWith('Load') ? 'load' : 'order'} number`;
    const text = document.createElement('strong');
    text.textContent = display;
    button.append(text);
    button.addEventListener('click', async event => {
      event.stopPropagation();
      const version = ++copyVersion;
      try {
        await navigator.clipboard.writeText(value);
        if (version === copyVersion) {
          status.className = 'visually-hidden';
          status.textContent = 'Copied';
          button.title = 'Copied';
        }
      } catch {
        if (version === copyVersion) {
          status.className = 'fleet-map-route-info__secondary';
          status.textContent = ' Could not copy. Try again.';
        }
      }
    });
    header.append(caption, button);
  }

  number(
    'Load ',
    String(reference.loadNumber),
    reference.loadLabel ?? String(reference.loadNumber),
  );
  if (reference.orderNumber)
    number(' · Order: ', reference.orderNumber, reference.orderNumber);
  header.append(status);
  return header;
}
