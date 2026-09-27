import type { LoadReference } from '../contracts.d.ts';

// The load and its order, said once each, with the number a click copies.
export function loadReferenceContent(
  reference: LoadReference,
  // Where the load number leads; without it the number is copied.
  href?: string | null,
): HTMLElement {
  const header = document.createElement('div');
  header.className = 'fleet-map-route-info__load';
  const status = document.createElement('span');
  status.className = 'visually-hidden';
  status.setAttribute('role', 'status');
  let copyVersion = 0;

  // The card says this line as "AMF1397  Order 568269862": the load stands
  // on its own, and only the order is named before it is said. The popup
  // used to prefix both and punctuate them, which read as a third way of
  // writing the same two numbers.
  function number(
    kind: string,
    label: string,
    value: string,
    display?: string,
  ) {
    const caption = document.createElement('span');
    caption.className = label ? 'fleet-map-route-info__label' : '';
    caption.textContent = label;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'fleet-map-route-info__copy-number';
    button.title = `Copy ${kind} number`;
    const text = document.createElement('strong');
    text.textContent = display ?? value;
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

  if (href) {
    // The load number opens that load, returning to the map.
    const link = document.createElement('a');
    link.className = 'fleet-route-popup__load-link';
    link.href = href;
    link.title = 'Open load';
    const text = document.createElement('strong');
    text.textContent = reference.loadLabel ?? String(reference.loadNumber);
    const arrow = document.createElement('span');
    arrow.setAttribute('aria-hidden', 'true');
    arrow.textContent = '\u00a0↗';
    link.append(text, arrow);
    header.append(link);
  } else
    number(
      'load',
      '',
      String(reference.loadNumber),
      reference.loadLabel ?? String(reference.loadNumber),
    );
  if (reference.orderNumber)
    number('order', 'Order', reference.orderNumber, reference.orderNumber);
  header.append(status);
  return header;
}
