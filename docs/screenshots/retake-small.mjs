import { chromium } from 'playwright';

const PORTAL    = 'https://ca-portal.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const REPORTING = 'https://ca-reporting.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const ADMIN_PW  = process.env.ADMIN_PW  ?? 'rBWc4TkDI1uDKHEMxV0q';
const CUST_PW   = process.env.CUST_PW   ?? '5Xn7y3biQ1zA1Gp9OQnc';
const OUT       = 'c:/PROJECTS/mod-poc/docs/screenshots';

async function fillAngularInput(page, index, value) {
  await page.evaluate(({ index, value }) => {
    const el = document.querySelectorAll('input')[index];
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }, { index, value });
}

async function waitForData(page, ms = 5000) {
  await page.waitForSelector('mat-spinner, mat-progress-bar', { state: 'hidden', timeout: ms }).catch(() => {});
  await page.waitForTimeout(1000);
}

const browserArgs = ['--enable-gpu','--use-gl=angle','--use-angle=swiftshader','--enable-webgl','--ignore-gpu-blocklist'];

(async () => {
  // ── 03-admin-dashboard ──────────────────────────────────────────────────
  {
    const browser = await chromium.launch({ headless: true, args: browserArgs });
    const ctx  = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await ctx.newPage();
    await page.goto(PORTAL, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForSelector('input', { state: 'attached', timeout: 20000 });
    await page.waitForTimeout(500);
    await fillAngularInput(page, 0, 'admin');
    await fillAngularInput(page, 1, ADMIN_PW);
    await page.locator('button[type="submit"]').click({ force: true });
    await page.waitForTimeout(5000);
    await waitForData(page, 6000);
    await page.screenshot({ path: `${OUT}/03-admin-dashboard.png`, fullPage: false });
    console.log('03-admin-dashboard.png saved');
    await browser.close();
  }

  // ── 11-customer-dashboard ───────────────────────────────────────────────
  {
    const browser = await chromium.launch({ headless: true, args: browserArgs });
    const ctx  = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await ctx.newPage();
    await page.goto(REPORTING, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForSelector('input', { state: 'attached', timeout: 20000 });
    await page.waitForTimeout(500);
    await fillAngularInput(page, 0, 'customer-a-viewer');
    await fillAngularInput(page, 1, CUST_PW);
    await page.locator('button[type="submit"]').click({ force: true });
    await page.waitForTimeout(5000);
    await waitForData(page, 6000);
    await page.screenshot({ path: `${OUT}/11-customer-dashboard.png`, fullPage: false });
    console.log('11-customer-dashboard.png saved');
    await browser.close();
  }
})();
