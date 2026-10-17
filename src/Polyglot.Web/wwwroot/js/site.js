// The theme switch of the sign-in pages, shared with the shell through Way.Shell's localStorage key.
(() => {
  'use strict';

  const THEME_KEY = 'way-shell:theme';
  const root = document.documentElement;

  const toggles = document.querySelectorAll('[data-theme-toggle]');
  const syncToggles = () => toggles.forEach(button => {
    const dark = root.dataset.theme === 'dark';
    button.querySelector('[data-icon="sun"]').hidden = !dark;
    button.querySelector('[data-icon="moon"]').hidden = dark;
    button.title = dark ? 'Switch to light mode' : 'Switch to dark mode';
    button.setAttribute('aria-label', button.title);
  });
  syncToggles();
  toggles.forEach(button => button.addEventListener('click', () => {
    const next = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.dataset.theme = next;
    try { localStorage.setItem(THEME_KEY, next); } catch { /* storage blocked */ }
    syncToggles();
  }));
})();
