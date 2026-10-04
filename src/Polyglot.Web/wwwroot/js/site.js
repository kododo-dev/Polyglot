// Small progressive enhancements. Every form still works without this script; it adds the theme
// switch, the mobile menu, dialogs instead of confirm(), copy buttons and relative dates.
(() => {
  'use strict';

  // ── Theme, shared with the translation editor through the same localStorage key ──
  const THEME_KEY = 'cultureway.theme';
  const root = document.documentElement;

  // Razor writes data-* attributes for false values too (data-open="False"), so read the value.
  const isOn = value => value !== undefined && value.toLowerCase() !== 'false';

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

  // ── Mobile side menu ──
  const shell = document.querySelector('.shell');
  document.querySelectorAll('[data-menu-toggle]').forEach(el =>
    el.addEventListener('click', () => shell?.classList.toggle('menu-open')));
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') shell?.classList.remove('menu-open');
  });

  // ── Dialogs ──
  // A button with data-dialog="id" opens that dialog. data-fill-* attributes copy values into
  // elements of the dialog that have the matching data-fill="*" (text) or name="*" (inputs).
  document.querySelectorAll('[data-dialog]').forEach(button => {
    button.addEventListener('click', () => {
      const dialog = document.getElementById(button.dataset.dialog);
      if (!dialog) return;
      for (const [key, value] of Object.entries(button.dataset)) {
        if (!key.startsWith('fill') || key === 'fill') continue;
        const name = key.slice(4).replace(/^./, c => c.toLowerCase());
        dialog.querySelectorAll(`[data-fill="${name}"]`).forEach(el => { el.textContent = value; });
        dialog.querySelectorAll(`input[name="${name}"]`).forEach(el => { el.value = value; });
      }
      dialog.showModal();
      dialog.querySelector('[autofocus], input:not([type=hidden])')?.focus();
    });
  });

  document.querySelectorAll('dialog').forEach(dialog => {
    dialog.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', () => dialog.close()));
    // A click on the backdrop lands on the dialog element itself.
    dialog.addEventListener('click', e => { if (e.target === dialog) dialog.close(); });
    if (isOn(dialog.dataset.open)) dialog.showModal();
  });

  // ── Confirmation before destructive or important actions ──
  // <form data-confirm="Question" data-confirm-action="Delete" data-confirm-danger> asks first.
  // A <select data-confirm=...> inside a form asks when its value changes and submits on yes.
  const confirmDialog = document.getElementById('confirm-dialog');
  const ask = (el, message, onYes, onNo) => {
    const danger = isOn(el.dataset.confirmDanger);
    const ok = confirmDialog.querySelector('[data-confirm-ok]');
    confirmDialog.querySelector('[data-fill="message"]').textContent = message;
    ok.textContent = el.dataset.confirmAction || 'Confirm';
    ok.className = danger ? 'btn btn-danger solid' : 'btn btn-primary';
    let confirmed = false;
    ok.onclick = () => { confirmed = true; confirmDialog.close(); onYes(); };
    confirmDialog.addEventListener('close', () => { if (!confirmed) onNo?.(); }, { once: true });
    confirmDialog.showModal();
  };

  document.querySelectorAll('form[data-confirm]').forEach(form => {
    form.addEventListener('submit', e => {
      if (form.dataset.confirmed) return;
      e.preventDefault();
      ask(form, form.dataset.confirm, () => { form.dataset.confirmed = '1'; form.requestSubmit(); });
    });
  });

  document.querySelectorAll('select[data-confirm]').forEach(select => {
    let previous = select.value;
    select.addEventListener('change', () => {
      const chosen = select.options[select.selectedIndex].text;
      ask(select, select.dataset.confirm.replace('{value}', chosen),
        () => select.form.submit(),
        () => { select.value = previous; });
    });
    select.addEventListener('focus', () => { previous = select.value; });
  });

  // ── Copy to clipboard ──
  document.querySelectorAll('[data-copy]').forEach(button => {
    button.addEventListener('click', async () => {
      const target = document.getElementById(button.dataset.copy);
      if (!target) return;
      try {
        await navigator.clipboard.writeText(target.textContent.trim());
        const label = button.querySelector('[data-label]');
        const before = label?.textContent;
        if (label) label.textContent = 'Copied';
        setTimeout(() => { if (label) label.textContent = before; }, 2000);
      } catch { /* clipboard blocked; the text stays selectable */ }
    });
  });

  // ── Relative dates: <time datetime="..." data-relative> shows "3 days ago" ──
  const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });
  const units = [['year', 31536000], ['month', 2592000], ['week', 604800], ['day', 86400], ['hour', 3600], ['minute', 60]];
  document.querySelectorAll('time[data-relative]').forEach(el => {
    const at = new Date(el.getAttribute('datetime'));
    if (Number.isNaN(at.getTime())) return;
    el.title = at.toLocaleString();
    const seconds = (at.getTime() - Date.now()) / 1000;
    const [unit, size] = units.find(([, s]) => Math.abs(seconds) >= s) ?? ['second', 1];
    el.textContent = Math.abs(seconds) < 60 ? 'just now' : rtf.format(Math.round(seconds / size), unit);
  });
})();
