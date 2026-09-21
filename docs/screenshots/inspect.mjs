import { chromium } from 'playwright';

const PORTAL = 'https://ca-portal.orangeground-e2a25376.westeurope.azurecontainerapps.io';

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.goto(PORTAL, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(2000);
  // Dump all input elements and their attributes
  const inputs = await page.$$eval('input, button', els => els.map(el => ({
    tag: el.tagName,
    type: el.type,
    name: el.name,
    id: el.id,
    placeholder: el.placeholder,
    class: el.className.substring(0, 80),
    ariaLabel: el.ariaLabel,
    value: el.value,
    textContent: el.textContent?.substring(0, 40),
  })));
  console.log(JSON.stringify(inputs, null, 2));
  await browser.close();
})();
