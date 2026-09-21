import { chromium } from 'playwright';

const PORTAL    = 'https://ca-portal.orangeground-e2a25376.westeurope.azurecontainerapps.io';
const REPORTING = 'https://ca-reporting.orangeground-e2a25376.westeurope.azurecontainerapps.io';

async function fillAngularInput(page, index, value) {
  await page.evaluate(({ index, value }) => {
    const inputs = document.querySelectorAll('input');
    const el = inputs[index];
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }, { index, value });
}

async function login(page, user, pw) {
  await page.waitForSelector('input', { state: 'attached', timeout: 20000 });
  await page.waitForTimeout(500);
  await fillAngularInput(page, 0, user);
  await fillAngularInput(page, 1, pw);
  await page.waitForTimeout(300);
  await page.locator('button[type="submit"]').click({ force: true });
  await page.waitForTimeout(5000);
}

(async () => {
  const browser = await chromium.launch({ headless: true });

  // --- Inspect customer portal nav ---
  console.log('\n=== Customer portal nav inspection ===');
  const ctx2 = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const p2 = await ctx2.newPage();
  await p2.goto(REPORTING, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await p2.waitForSelector('app-root', { timeout: 20000 });
  await login(p2, 'customer-a-viewer', process.env.CUST_PW);
  
  const custNav = await p2.evaluate(() => {
    const selectors = ['a', 'button', '[routerlink]', 'mat-list-item', '.nav-link'];
    const all = [];
    for (const sel of selectors) {
      document.querySelectorAll(sel).forEach(el => {
        const text = el.textContent?.trim();
        const tag = el.tagName;
        const rl = el.getAttribute('routerlink') || el.getAttribute('ng-reflect-router-link') || '';
        if (text && text.length > 0 && text.length < 60) {
          all.push({ tag, text: text.slice(0, 40), rl });
        }
      });
    }
    return all;
  });
  console.log('Customer nav elements:', JSON.stringify(custNav, null, 2));

  // --- Inspect admin portal simulator tabs ---
  console.log('\n=== Admin portal simulator tab inspection ===');
  const ctx1 = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const p1 = await ctx1.newPage();
  await p1.goto(PORTAL, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await p1.waitForSelector('app-root', { timeout: 20000 });
  await login(p1, 'admin', process.env.ADMIN_PW);
  // Navigate to simulator
  await p1.locator('a', { hasText: 'science' }).first().click({ force: true }).catch(()=>{});
  await p1.locator('a').filter({ hasText: /simulator/i }).first().click({ force: true }).catch(()=>{});
  await p1.waitForTimeout(3000);
  const simTabs = await p1.evaluate(() => {
    const tabs = document.querySelectorAll('mat-tab-header, .mat-tab-labels, [role="tab"], mat-tab, .mat-mdc-tab');
    const result = [];
    tabs.forEach(el => result.push({ tag: el.tagName, role: el.getAttribute('role'), text: el.textContent?.trim()?.slice(0,60) }));
    return result;
  });
  console.log('Sim tab elements:', JSON.stringify(simTabs, null, 2));

  await browser.close();
})();
