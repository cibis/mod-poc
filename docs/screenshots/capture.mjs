import { chromium } from 'playwright';

const PORTAL    = 'https://ca-portal.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const REPORTING = 'https://ca-reporting.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const ADMIN_PW  = process.env.ADMIN_PW  ?? 'rBWc4TkDI1uDKHEMxV0q';
const CUST_PW   = process.env.CUST_PW   ?? '5Xn7y3biQ1zA1Gp9OQnc';
const OUT       = 'c:/PROJECTS/mod-poc/docs/screenshots';

async function shot(page, name, desc) {
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: false });
  console.log(`  [ok] ${name}.png — ${desc}`);
}

// Wait for Angular loading to settle: spinner gone and no pending HTTP
async function waitForData(page, maxMs = 5000) {
  try {
    // Wait for Angular Material progress spinners to disappear
    await page.waitForSelector('mat-spinner, mat-progress-bar', { state: 'hidden', timeout: maxMs })
      .catch(() => {});
    // Small extra settle time for Angular change detection
    await page.waitForTimeout(800);
  } catch { /* ignore */ }
}

// Angular Material inputs need native value setter to trigger change detection
async function fillAngularInput(page, index, value) {
  await page.evaluate(({ index, value }) => {
    const inputs = document.querySelectorAll('input');
    const el = inputs[index];
    if (!el) throw new Error(`No input at index ${index}`);
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }, { index, value });
}

async function login(page, username, password) {
  await page.waitForSelector('input', { state: 'attached', timeout: 20000 });
  await page.waitForTimeout(500);
  await fillAngularInput(page, 0, username);
  await fillAngularInput(page, 1, password);
  await page.waitForTimeout(300);
  await page.locator('button[type="submit"]').click({ force: true });
  await page.waitForTimeout(5000);   // wait for router navigation + first API calls
}

async function navTo(page, linkText) {
  await page.locator('a', { hasText: linkText }).first().click({ force: true });
  await waitForData(page, 6000);
}

async function testAdminPortal(browser) {
  console.log('\n=== Admin Portal ===');
  const ctx  = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  await page.goto(PORTAL, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('app-root', { timeout: 20000 });
  await page.waitForTimeout(2000);
  await shot(page, '01-admin-login', 'Admin portal — login page');

  await login(page, 'admin', ADMIN_PW);
  await waitForData(page, 5000);
  await shot(page, '03-admin-dashboard', 'Admin portal — dashboard (tenants list)');

  // Collect sidenav links — extract title from matListItemTitle span (avoids icon text)
  const navLinks = await page.$$eval('mat-nav-list a', els =>
    [...new Set(els.map(e => {
      const title = e.querySelector('span[ng-reflect-title], .mdc-list-item__primary-text, [matlistitemtitle]');
      if (title) return title.textContent?.trim();
      // Fallback: take the longest word chunk (icon names are single short words)
      return (e.textContent ?? '').trim().split(/\s+/).filter(t => t.length > 3).join(' ').trim();
    }).filter(t => t && t.length > 1))]
  );
  console.log('  Nav links found:', navLinks.join(' | '));

  const wantedSections = ['Tenants', 'Collectors', 'Audit', 'Simulator'];

  for (const section of wantedSections) {
    const match = navLinks.find(l => l.toLowerCase().includes(section.toLowerCase()));
    if (!match) { console.log(`  [skip] no link for "${section}"`); continue; }
    try {
      await navTo(page, match);
      const slug = section.toLowerCase().replace(/[^a-z0-9]+/g, '-');
      await shot(page, `04-admin-${slug}`, `Admin — ${section}`);
    } catch (e) {
      console.log(`  [skip] ${section}: ${e.message.split('\n')[0]}`);
    }
  }

  // Simulator sub-pages: navigate by routerLink <a> in nav.sim-nav
  const simMatch = navLinks.find(l => l.toLowerCase().includes('simulator'));
  if (simMatch) {
    try {
      await navTo(page, simMatch);
      // Wait for topology to render (SignalR snapshot can take a few seconds)
      await page.waitForSelector('.topology-chart, mat-spinner', { timeout: 3000 }).catch(() => {});
      await page.waitForSelector('mat-spinner', { state: 'hidden', timeout: 12000 }).catch(() => {});
      await page.waitForTimeout(2000);
      await shot(page, '05-admin-sim-overview', 'Admin — Simulator / Overview');
      for (const tab of ['Fleet', 'Buffers', 'Scenarios']) {
        try {
          // Simulator uses plain <a routerLink="..."> in nav.sim-nav, not Material tabs
          await page.locator('nav.sim-nav a', { hasText: tab }).click({ force: true });
          await waitForData(page, 6000);
          await page.waitForTimeout(1500);  // extra settle for table rows
          await shot(page, `05-admin-sim-${tab.toLowerCase()}`, `Admin — Simulator / ${tab}`);
        } catch (e) { console.log(`  [skip] sim tab ${tab}: ${e.message.split('\n')[0]}`); }
      }
    } catch (e) { console.log(`  [skip] Simulator: ${e.message.split('\n')[0]}`); }
  }

  await ctx.close();
}

async function testCustomerPortal(browser) {
  console.log('\n=== Customer Portal ===');
  const ctx  = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  await page.goto(REPORTING, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('app-root', { timeout: 20000 });
  await page.waitForTimeout(2000);
  await shot(page, '10-customer-login', 'Customer portal — login page');

  await login(page, 'customer-a-viewer', CUST_PW);
  await waitForData(page, 5000);
  await shot(page, '11-customer-dashboard', 'Customer A portal — sites list');

  // Click first site card (card-based grid, no sidenav)
  const firstCard = await page.$('mat-card');
  if (firstCard) {
    await firstCard.click({ force: true });
    await waitForData(page, 6000);
    console.log('  After card click URL:', page.url());
    await shot(page, '12-customer-site-detail', 'Customer A — site detail');

    // Navigate directly to a known asset (Line 1 Capper, Customer A North 1)
    const assetId = '0a1b9a47-e76d-5089-9c6f-282209a22433';
    await page.goto(`${REPORTING}/assets/${assetId}`, { waitUntil: 'networkidle', timeout: 20000 });
    await waitForData(page, 8000);
    await page.waitForTimeout(3000);
    console.log('  Asset page URL:', page.url());
    await shot(page, '12-customer-asset-detail', 'Customer A — asset detail (rollup chart)');
  } else {
    console.log('  [skip] no mat-card found on dashboard');
  }

  await ctx.close();
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    await testAdminPortal(browser);
    await testCustomerPortal(browser);
    const files = (await import('fs')).readdirSync(OUT).filter(f => f.endsWith('.png'));
    console.log(`\nDone — ${files.length} screenshots in ${OUT}`);
  } catch (e) {
    console.error('Fatal:', e.message);
    process.exit(1);
  } finally {
    await browser.close();
  }
})();
