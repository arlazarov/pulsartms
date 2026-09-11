let locks = 0;
let scrollY = 0;

export function lockScroll() {
  locks++;
  if (locks > 1) return;
  scrollY = window.scrollY;
  document.documentElement.classList.add('popup-open');
  document.body.classList.add('popup-open');
  document.body.style.setProperty('--popup-scroll-offset', `-${scrollY}px`);
}

export function unlockScroll() {
  if (locks === 0) return;
  locks--;
  if (locks > 0) return;
  document.documentElement.classList.remove('popup-open');
  document.body.classList.remove('popup-open');
  document.body.style.removeProperty('--popup-scroll-offset');
  window.scrollTo(0, scrollY);
}
