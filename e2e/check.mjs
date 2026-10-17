// Browser scenarios against a running demo instance (Production, Polyglot__Demo__Enabled=true, cultures en,pl,de):
//   node check.mjs http://localhost:5097/polyglot/demo [chrome|msedge|firefox|webkit]
// Needs the playwright package (npm i --no-save playwright); firefox and webkit need `npx playwright install`.
// Changes the demo's data (an API key is created and deleted, a change is undone), so run it on a fresh database.
import { chromium, firefox, webkit } from 'playwright';

const base = process.argv[2].replace(/\/$/, '');
const which = process.argv[3] ?? 'chrome';
const browser = which === 'firefox' ? await firefox.launch() : which === 'webkit' ? await webkit.launch() : await chromium.launch({ channel: which });

const results = [];
const record = (name, ok, detail = '') => results.push({ name, ok, detail });
const visible = (locator) => locator.first().waitFor({ timeout: 10000 }).then(() => true, () => false);
const menuOf = (page) => page.locator('nav .MuiListItemText-primary').allTextContents();
/** Polls until `check` is true (data arrives after the page's heading), for up to 10 s. */
async function until(check) {
  for (let i = 0; i < 50; i++) {
    if (await check()) return true;
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  return false;
}
const heading = (page, name) => page.getByRole('heading', { level: 1, name });

async function session(contextOptions = {}) {
  // English and light theme to start with, whatever the machine's settings.
  const context = await browser.newContext({ locale: 'en-US', colorScheme: 'light', ...contextOptions });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => {
    if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) errors.push(m.text());
  });
  page.on('response', (r) => {
    if (r.status() >= 400 && !r.url().endsWith('/favicon.ico')) errors.push(`${r.status()} ${r.url()}`);
  });
  return { context, page, errors };
}

async function confirm(page, action) {
  await page.getByRole('dialog').getByRole('button', { name: action }).click();
  await page.getByRole('dialog').waitFor({ state: 'detached' });
}

// The demo administrator.
{
  const { context, page, errors } = await session();
  await page.goto(`${base}/`);
  record('the panel asks to sign in', page.url().includes('/login'), page.url());
  await page.getByRole('button', { name: 'Try the demo' }).click();
  await page.waitForURL(/#\/overview\/$/);
  record('signing in opens the overview in the panel', await visible(heading(page, 'Overview')), page.url());
  record('title of Polyglot', (await page.title()) === 'Polyglot', await page.title());
  const menu = (await menuOf(page)).join(',');
  record('one menu with every page, in order, without Account', menu === 'Overview,Translations,History,Users,API keys', menu);
  record('demo notice above the page', await visible(page.getByTestId('shell-notice').filter({ hasText: 'public demo' })));
  record('overview tiles', await visible(page.getByTestId('tile-languages').filter({ hasText: 'en, pl, de' })));
  record("the demo's key on the overview", await visible(page.getByTestId('demo-token')));

  await page.getByTestId('tile-users').click();
  record('a tile opens its page in the panel', (await visible(heading(page, 'Users'))) && page.url().endsWith('#/users/'), page.url());
  record('users are listed', await until(async () => (await page.getByTestId('user-row').count()) >= 4));
  record('users are read-only in the demo', (await visible(page.getByText('turned off in the demo'))) && (await page.getByRole('button', { name: 'Add user' }).count()) === 0);

  await page.locator('nav').getByText('API keys').click();
  await visible(heading(page, 'API keys'));
  record("the demo's key has no controls", (await page.getByTestId('key-row').filter({ hasText: 'demo' }).getByRole('button').count()) === 0);
  await page.getByRole('button', { name: 'New key' }).first().click();
  await page.getByRole('dialog').getByLabel('Name').fill('e2e-key');
  await page.getByRole('dialog').getByRole('button', { name: '30 days' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Create key' }).click();
  const token = await page.getByTestId('new-token').textContent({ timeout: 10000 }).catch(() => '');
  record('a new key is shown once', /^pg_[a-z0-9]{8}_\w+$/.test(token ?? ''), token ?? '');
  const row = page.getByTestId('key-row').filter({ hasText: 'e2e-key' });
  record('the new key is listed, active', await visible(row.getByText('active')));
  const call = await context.request.get(`${base}/api/v1/cultures`, { headers: { 'X-Api-Key': token ?? '' } });
  record('the new key reads the delivery API', call.status() === 200, `${call.status()}`);
  await row.getByRole('button', { name: 'Disable' }).click();
  await confirm(page, 'Disable');
  record('disabling a key asks first, then shows it', await visible(row.getByText('disabled')));
  await row.getByRole('button', { name: 'Delete' }).click();
  await confirm(page, 'Delete key');
  record('deleting a key asks first, then removes it', (await visible(page.getByText('API key deleted.'))) && (await row.count()) === 0);

  await page.locator('nav').getByText('Translations').click();
  record('the editor opens in the same panel', (await visible(page.getByRole('heading', { name: 'All' }))) && page.url().includes('#/translations/'), page.url());
  record('the menu stays the same in the editor', (await menuOf(page)).join(',') === menu, (await menuOf(page)).join(','));

  await page.locator('nav').getByText('History').click();
  await visible(heading(page, 'History'));
  record('history lists the changes', await until(async () => (await page.getByTestId('change').count()) >= 5));
  await page.getByTestId('change').getByRole('button', { name: 'Checkout.Pay' }).first().click();
  await page.waitForURL(/key=Checkout\.Pay/);
  const keyLinks = () => page.getByTestId('change').locator('button.MuiLink-root').allTextContents();
  const filtered = await until(async () => {
    const keys = await keyLinks();
    return keys.length > 0 && keys.every((k) => k === 'Checkout.Pay');
  });
  record('a key filters the history, in the address', filtered, (await keyLinks()).join(','));
  await page.reload();
  await visible(page.getByTestId('change'));
  record('the filter survives a reload', (await page.getByLabel('Key').inputValue({ timeout: 10000 })) === 'Checkout.Pay');
  const before = await page.getByTestId('change').count(); // loaded: the filter above waited for the rows
  await page.getByTestId('change').first().getByRole('button', { name: 'Undo' }).click();
  await confirm(page, 'Undo');
  record('undo asks first, then records the undo',
    (await visible(page.getByText('Change undone'))) && (await until(async () => (await page.getByTestId('change').count()) === before + 1)));
  await page.getByRole('button', { name: 'Clear' }).click();
  record('clearing the filter', !page.url().includes('key='), page.url());

  await page.getByTestId('shell-user').getByRole('link').click();
  record('the user name opens the account page', (await visible(heading(page, 'Account'))) && page.url().endsWith('#/account/'), page.url());
  record('no password change in the demo', await visible(page.getByText('Changing the password is turned off')));

  await page.getByRole('button', { name: 'Language' }).click();
  await page.getByRole('menuitem', { name: 'Polski' }).click();
  record("Polyglot's pages follow the shell's language", await visible(heading(page, 'Konto')));
  await page.getByRole('button', { name: 'Ciemny motyw' }).click();
  await page.locator('nav').getByText('Overview').click();
  record('the language stays across pages', await visible(heading(page, 'Przegląd')));

  await page.getByRole('button', { name: 'Wyloguj' }).click();
  await page.waitForURL(/\/login/);
  record('sign-out goes to the sign-in page', await visible(page.getByRole('button', { name: 'Try the demo' })));
  record('the sign-in page keeps the theme chosen in the panel', (await page.evaluate(() => document.documentElement.dataset.theme)) === 'dark');
  record('no errors (admin)', errors.length === 0, errors.join(' | '));
  await context.close();
}

// On a phone.
{
  const { context, page, errors } = await session({ viewport: { width: 390, height: 844 }, isMobile: which !== 'firefox', hasTouch: true });
  await page.goto(`${base}/login`);
  await page.getByRole('button', { name: 'Try the demo' }).click();
  await page.waitForURL(/#\/overview\/$/);
  record('menu hidden on a phone', !(await page.locator('nav').isVisible()));
  await page.getByRole('button', { name: 'Open menu' }).first().click();
  await page.locator('nav').getByText('History').click();
  record('choosing a page closes the menu', (await visible(heading(page, 'History'))) && !(await page.locator('nav').isVisible()));
  record('no errors (phone)', errors.length === 0, errors.join(' | '));
  await context.close();
}

await browser.close();
for (const r of results) console.log(`${r.ok ? 'ok  ' : 'FAIL'} ${r.name}${r.detail ? ` — ${r.detail}` : ''}`);
console.log(`\n${which} ${base}: ${results.filter((r) => r.ok).length}/${results.length} ok`);
process.exit(results.every((r) => r.ok) ? 0 : 1);
