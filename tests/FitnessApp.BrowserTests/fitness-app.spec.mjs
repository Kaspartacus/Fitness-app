import { randomUUID } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import { expect, test as base } from '@playwright/test';
import { fillTestPassword } from './test-password.mjs';

const password = process.env.FITNESS_BROWSER_PASSWORD;
const adminEmail = 'browser-admin@example.test';

async function screenshot(page, testInfo, name) {
  await page.screenshot({
    path: testInfo.outputPath(`${name}.png`),
    mask: [page.locator('input')],
    animations: 'disabled',
    timeout: 4000,
  });
}

const test = base.extend({
  evidence: async ({ page, baseURL }, use, testInfo) => {
    await page.context().route('**/*', (route) =>
      new URL(route.request().url()).origin === baseURL ? route.continue() : route.abort());

    const steps = [];
    await use({
      screenshot: (name) => screenshot(page, testInfo, name),
      step: (name, action) => base.step(name, async () => {
        const step = { name, status: 'started' };
        steps.push(step);
        const start = Date.now();
        try {
          await action();
          step.status = 'passed';
        } catch (error) {
          step.status = 'failed';
          throw error;
        } finally {
          step.durationMs = Date.now() - start;
        }
      }),
    });

    if (testInfo.status !== 'passed') {
      await writeFile(testInfo.outputPath('failure-trace.json'), JSON.stringify({
        project: testInfo.project.name,
        status: testInfo.status,
        expectedStatus: testInfo.expectedStatus,
        steps,
      }, null, 2) + '\n');
      if (!page.isClosed()) await screenshot(page, testInfo, 'failure');
    }
  },
});

async function submitLogin(page, email) {
  await page.getByLabel('E-mail', { exact: true }).fill(email);
  await fillTestPassword(page.getByLabel('Adgangskode', { exact: true }), password);
  await page.getByRole('button', { name: 'Log ind', exact: true }).click();
}

async function expectStrength(page) {
  await expect(page).toHaveURL('/styrke');
  await expect(page.getByRole('heading', { name: 'Styrketræning', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Ingen programmer endnu' })).toBeVisible();
}

test('registration shows that administrator approval is pending', async ({ page, evidence }) => {
  const email = `new-${randomUUID()}@example.test`;
  await evidence.step('Submit a new registration through the login page', async () => {
    await page.goto('/login');
    await page.getByRole('link', { name: 'Opret bruger' }).click();
    await expect(page.getByRole('heading', { name: 'Opret bruger' })).toBeVisible();
    await page.getByLabel('Navn', { exact: true }).fill('Ny testbruger');
    await page.getByLabel('E-mail', { exact: true }).fill(email);
    await fillTestPassword(page.getByLabel('Adgangskode', { exact: true }), password);
    await fillTestPassword(page.getByLabel('Gentag adgangskode', { exact: true }), password);
    await page.getByRole('button', { name: 'Opret bruger' }).click();
  });
  await evidence.step('Confirm that registration was accepted and login requires approval', async () => {
    await expect(page).toHaveURL('/registrering-modtaget');
    await expect(page.getByRole('heading', { name: /Din konto er oprettet|Tak for din registrering/ })).toBeVisible();
    await expect(page.getByText(/administrator/, { exact: false })).toBeVisible();
    await expect(page.getByText(/Du kan logge ind, når kontoen er blevet godkendt/)).toBeVisible();
    await evidence.screenshot('registration-pending');
  });
  await evidence.step('Attempt login before approval', async () => {
    await page.getByRole('link', { name: 'Tilbage til login' }).click();
    await expect(page.getByRole('heading', { name: 'Velkommen tilbage' })).toBeVisible();
    await submitLogin(page, email);
    await expect(page.getByRole('alert')).toHaveText('E-mail eller adgangskode er forkert, eller kontoen har ikke adgang.');
    await expect(page).toHaveURL('/login');
  });
});

test('strength navigation and browser history', async ({ page, evidence }, testInfo) => {
  const home = page.getByRole('heading', { name: 'Hej, Testadministrator' });
  const nav = page.getByRole('navigation', { name: 'Hovednavigation' });
  const mobile = testInfo.project.use.hasTouch;
  const tab = (name) => nav.getByRole('link', { name, exact: true });
  const selectTab = (name) => mobile ? tab(name).tap() : tab(name).click();
  const expectActiveTab = async (name) => {
    await expect(tab(name)).toHaveAttribute('aria-current', 'page');
    await expect(nav.locator('[aria-current="page"]')).toHaveCount(1);
  };
  let pageErrors = 0;
  page.on('pageerror', () => pageErrors++);

  await evidence.step('Sign in and open strength from the dashboard', async () => {
    await page.goto('/login');
    await expect(page.getByRole('heading', { name: 'Velkommen tilbage' })).toBeVisible();
    await expect(nav).toHaveCount(0);
    await submitLogin(page, adminEmail);
    await expect(home).toBeVisible();
    await page.getByRole('link', { name: 'Styrketræning', exact: true }).click();
    await expectStrength(page);
    await expectActiveTab('Træning');
  });
  await evidence.step('Show only the navigation for the current viewport', async () => {
    await expect(nav).toHaveCount(1);
    await expect(nav).toBeInViewport({ ratio: 1 });
    if (mobile) {
      await expect(nav).toHaveClass('strength-bottom-nav');
      await expect(nav).toHaveCSS('bottom', '0px');
      const bottom = await nav.evaluate((element) => element.getBoundingClientRect().bottom);
      expect(bottom).toBeCloseTo(page.viewportSize().height, 0);
    } else {
      await expect(nav).toHaveClass('app-desktop-nav');
      const header = page.getByRole('banner').filter({ has: nav });
      await expect(header).toHaveCSS('position', 'sticky');
      const top = () => header.evaluate((element) => element.getBoundingClientRect().top);
      expect(await top()).toBeCloseTo(0, 0);
      await page.evaluate(() => window.scrollTo(0, 100));
      await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(0);
      expect(await top()).toBeCloseTo(0, 0);
      await page.evaluate(() => window.scrollTo(0, 0));
    }
    await evidence.screenshot('strength');
  });
  await evidence.step('Open every other training-area tab from strength', async () => {
    for (const [name, route] of [['Kost', '/kost'], ['Løb', '/loeb'], ['Kalender', '/kalender']]) {
      await selectTab(name);
      await expect(page).toHaveURL(route);
      await expect(page.getByRole('heading', { name, exact: true, level: 1 })).toBeVisible();
      await expectActiveTab(name);
      await selectTab('Træning');
      await expectStrength(page);
      await expectActiveTab('Træning');
    }
  });
  await evidence.step('Return home with the main navigation and verify browser history', async () => {
    await selectTab('Hjem');
    await expect(page).toHaveURL('/');
    await expect(home).toBeVisible();
    await expectActiveTab('Hjem');
    await evidence.screenshot('home-after-navigation');

    await page.goBack();
    await expectStrength(page);
    await expectActiveTab('Træning');
    await page.goForward();
    await expect(page).toHaveURL('/');
    await expect(home).toBeVisible();
    await expectActiveTab('Hjem');
  });
  await evidence.step('Open a protected deep link and use the keyboard navigation', async () => {
    await page.goto('/loeb/resultater');
    await expect(page.getByRole('heading', { name: 'Resultathistorik', level: 1 })).toBeVisible();
    await expectActiveTab('Løb');
    await tab('Kost').focus();
    await page.keyboard.press('Shift+Tab');
    await expect(tab('Hjem')).toBeFocused();
    await expect(tab('Hjem')).toHaveCSS('outline-style', 'solid');
    await expect(tab('Hjem')).toHaveCSS('outline-width', '2px');
    await evidence.screenshot('keyboard-focus');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL('/');
    await expect(home).toBeVisible();
    await expectActiveTab('Hjem');
  });
  if (mobile) {
    await evidence.step('Keep the navigation visible on a 360px phone', async () => {
      await page.setViewportSize({ width: 360, height: 800 });
      await selectTab('Træning');
      await expectStrength(page);
      await expectActiveTab('Træning');
      await expect(nav).toBeInViewport({ ratio: 1 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
      await evidence.screenshot('strength-360');
    });
  }
  expect(pageErrors).toBe(0);
});
