import { chromium } from 'playwright';

const PORTAL = 'https://ca-portal.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const ADMIN_PW = process.env.ADMIN_PW ?? 'rBWc4TkDI1uDKHEMxV0q';
const OUT = 'c:/PROJECTS/mod-poc/docs/screenshots';

async function fillAngularInput(page, index, value) {
  await page.evaluate(({ index, value }) => {
    const el = document.querySelectorAll('input')[index];
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }, { index, value });
}

(async () => {
  const browser = await chromium.launch({
    headless: true,
    args: ['--enable-gpu', '--use-gl=angle', '--use-angle=swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'],
  });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  await page.goto(PORTAL, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('input', { state: 'attached', timeout: 20000 });
  await page.waitForTimeout(500);
  await fillAngularInput(page, 0, 'admin');
  await fillAngularInput(page, 1, ADMIN_PW);
  await page.locator('button[type="submit"]').click({ force: true });
  await page.waitForTimeout(5000);

  await page.goto(`${PORTAL}/simulation/buffers`, { waitUntil: 'domcontentloaded', timeout: 20000 });
  await page.waitForSelector('mat-spinner', { state: 'hidden', timeout: 10000 }).catch(() => {});
  // Wait for ECharts canvases to paint
  await page.waitForTimeout(4000);

  await page.screenshot({ path: `${OUT}/05-admin-sim-buffers.png`, fullPage: false });
  console.log('Saved 05-admin-sim-buffers.png');

  await browser.close();
})();
