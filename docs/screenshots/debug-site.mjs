import { chromium } from 'playwright';
const REPORTING = 'https://ca-reporting.nicesmoke-bae83ab1.westeurope.azurecontainerapps.io';
const CUST_PW = '5Xn7y3biQ1zA1Gp9OQnc';
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
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  // Capture console errors
  page.on('console', msg => { if (msg.type() === 'error') console.log('CONSOLE ERROR:', msg.text()); });

  await page.goto(REPORTING, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('app-root', { timeout: 20000 });
  await page.waitForTimeout(1000);

  // Login
  await page.waitForSelector('input', { timeout: 10000 });
  await fillAngularInput(page, 0, 'customer-a-viewer');
  await fillAngularInput(page, 1, CUST_PW);
  await page.locator('button[type="submit"]').click({ force: true });
  await page.waitForURL(url => !url.toString().includes('/login'), { timeout: 15000 });
  await page.waitForTimeout(2000);
  console.log('After login:', page.url());

  // Click first site card
  const card = await page.$('mat-card');
  if (card) {
    await card.click({ force: true });
    await page.waitForTimeout(3000);
    console.log('After card click:', page.url());
    await page.screenshot({ path: `${OUT}/debug-site-detail.png`, fullPage: true });
    console.log('Screenshot saved');
    // Log page content for errors
    const errMsg = await page.$eval('body', el => el.innerText).catch(() => '');
    if (errMsg.toLowerCase().includes('error')) console.log('Error text found:', errMsg.slice(0, 500));
  }

  await browser.close();
})();
