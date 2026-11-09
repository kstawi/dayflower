// Dayflower's homepage: today's date on the icon, the header's edge once the page scrolls, and sections fading in.
// The page works without it (index.html marks <html> with .js first, so hidden sections never flash).
document.addEventListener('DOMContentLoaded', () => {
  const day = String(new Date().getDate());
  document.querySelectorAll('[data-today]').forEach((el) => { el.textContent = day; });
  const pad = document.querySelector('.pad');
  if (pad) {
    pad.hidden = false;
    document.querySelector('.pad-note').hidden = false;
    document.querySelector('.pad-static').hidden = true;
  }

  const header = document.querySelector('.site-header');
  const onScroll = () => header.classList.toggle('scrolled', window.scrollY > 8);
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });

  const reveal = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window) {
    const seen = new IntersectionObserver((entries) => {
      entries.forEach((e) => {
        if (e.isIntersecting) { e.target.classList.add('in'); seen.unobserve(e.target); }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.12 });
    reveal.forEach((el) => seen.observe(el));
  } else {
    reveal.forEach((el) => el.classList.add('in'));
  }

  // The feature tiles' soft light follows the pointer.
  document.querySelectorAll('.tile').forEach((tile) => {
    tile.addEventListener('pointermove', (e) => {
      const r = tile.getBoundingClientRect();
      tile.style.setProperty('--mx', `${e.clientX - r.left}px`);
      tile.style.setProperty('--my', `${e.clientY - r.top}px`);
    });
  });
});
