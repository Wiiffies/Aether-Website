(() => {
  // Highlight the current page in the shared nav (works on every page, no dependencies).
  try {
    const here = location.pathname.split('/').pop() || 'index.html';
    document.querySelectorAll('.nav a').forEach(a => {
      const href = (a.getAttribute('href') || '').split('#')[0];
      if (href && href === here) { a.classList.add('is-active'); a.setAttribute('aria-current', 'page'); }
    });
  } catch {}
  const menu = document.querySelector('.menu-btn');
  const nav = document.querySelector('.nav');
  if (menu && nav) menu.addEventListener('click', () => {
    nav.classList.toggle('is-open');
    const open = nav.classList.contains('is-open');
    menu.setAttribute('aria-expanded', String(open));
    menu.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
  });
  // Close the mobile menu on Escape (keyboard users).
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && nav && nav.classList.contains('is-open')) {
      nav.classList.remove('is-open');
      if (menu) { menu.setAttribute('aria-expanded', 'false'); menu.focus(); }
    }
  });
  document.querySelectorAll('.nav a').forEach(link => link.addEventListener('click', () => nav?.classList.remove('is-open')));
  const reveal = document.querySelectorAll('.service-card, .project, .process-item, .page-main > *, .page-card, .detail-list > div, .hosting-note');
  const observer = new IntersectionObserver(entries => entries.forEach(entry => {
    if (entry.isIntersecting) { entry.target.classList.add('is-visible'); observer.unobserve(entry.target); }
  }), { threshold: .12 });
  reveal.forEach(el => { el.classList.add('reveal'); observer.observe(el); });
})();
