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

  await evidence.step('Sign in and open strength from the dashboard', async () => {
    await page.goto('/login');
    await submitLogin(page, adminEmail);
    await expect(home).toBeVisible();
    await page.getByRole('link', { name: 'Styrketræning', exact: true }).click();
    await expectStrength(page);
    await evidence.screenshot('strength');
  });
  await evidence.step('Return to the dashboard with browser history', async () => {
    await page.goBack();
    await expect(page).toHaveURL('/');
    await expect(home).toBeVisible();
    await evidence.screenshot('home-after-back');
    await page.goForward();
    await expectStrength(page);
  });
  await evidence.step('Use main navigation from strength', async () => {
    // The desktop stylesheet currently hides this navigation. Remove with NAV-01.
    test.fail(testInfo.project.name === 'desktop', 'NAV-01: main navigation is hidden on desktop.');
    await nav.getByRole('link', { name: 'Hjem' }).click({ timeout: 4000 });
    await expect(page).toHaveURL('/');
    await expect(home).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Hjem' })).toHaveAttribute('aria-current', 'page');
    await evidence.screenshot('home-after-navigation');
  });
});
