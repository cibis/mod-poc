import { chromium } from 'playwright';

const REPORTING = 'https://ca-reporting.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const CUST_PW   = process.env.CUST_PW;
const OUT       = 'c:/PROJECTS/mod-poc/docs/screenshots';

async function shot(page, name, desc) {
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: false });
  console.log(`  [ok] ${name}.png — ${desc}`);
}

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

(async () => {
  const browser = await chromium.launch({ headless: true });
  const ctx  = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  try {
    console.log('Navigating to customer portal...');
    await page.goto(REPORTING, { waitUntil: 'networkidle', timeout: 30000 });
    await page.waitForSelector('app-root', { timeout: 20000 });
    await page.waitForTimeout(2000);
    await shot(page, '10-customer-login', 'Customer portal — login page');

    console.log('Logging in...');
    await page.waitForSelector('input', { state: 'attached', timeout: 15000 });
    await page.waitForTimeout(500);
    await fillAngularInput(page, 0, 'customer-a-viewer');
    await fillAngularInput(page, 1, CUST_PW);
    await page.waitForTimeout(300);
    await page.locator('button[type="submit"]').click({ force: true });

    // Wait for navigation away from login page
    await page.waitForURL(url => !url.toString().includes('/login'), { timeout: 15000 });
    await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
    await page.waitForTimeout(2000);

    console.log('URL after login:', page.url());
    await shot(page, '11-customer-dashboard', 'Customer A portal — dashboard (sites list)');

    // Find site cards — customer portal uses mat-card grid, not sidenav
    const cards = await page.$$eval('mat-card, .site-card, [routerLink]',
      els => els.map(e => ({ text: e.textContent?.trim().slice(0, 40), tag: e.tagName }))
        .filter(e => e.text && e.text.length > 2).slice(0, 6)
    );
    console.log('  Cards/links found:', JSON.stringify(cards));

    // Click first site card
    const firstCard = await page.$('mat-card');
    if (firstCard) {
      await firstCard.click({ force: true });
      await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});
      await page.waitForTimeout(1500);
      console.log('  After card click URL:', page.url());
      await shot(page, '12-customer-site-detail', 'Customer A — site detail');

      // Navigate directly to a known asset (from /api/hierarchy)
      const assetId = '0a1b9a47-e76d-5089-9c6f-282209a22433'; // Line 1 Capper, Customer A North 1
      await page.goto(`${REPORTING}/assets/${assetId}`, { waitUntil: 'networkidle', timeout: 15000 }).catch(() => {});
      await page.waitForTimeout(2000);
      console.log('  Asset page URL:', page.url());
      await shot(page, '12-customer-asset-detail', 'Customer A — asset detail (rollup chart)');
    } else {
      console.log('  [skip] no mat-card found on dashboard');
    }

  } catch (e) {
    console.error('Error:', e.message);
    // Take error screenshot for debugging
    await page.screenshot({ path: `${OUT}/debug-customer-error.png`, fullPage: true }).catch(() => {});
    process.exit(1);
  } finally {
    await ctx.close();
    await browser.close();
  }
})();
