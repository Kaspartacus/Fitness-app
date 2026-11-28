import { randomUUID } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import { expect, test as base } from '@playwright/test';
import { fillTestPassword } from './test-password.mjs';

const password = process.env.FITNESS_BROWSER_PASSWORD;
const adminEmail = 'browser-admin@example.test';

async function screenshot(page, testInfo, name) {
  await page.screenshot({
    path: testInfo.outputPath(`${name}.png`),
    mask: [page.locator('input[type="password"], input[type="email"], input[autocomplete="email"]')],
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
  await expect(page.getByText('Mine programmer', { exact: true })).toBeVisible();
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

test('edit and finish a strength workout', async ({ page, evidence }, testInfo) => {
  const programName = `Playwright styrke ${testInfo.project.name}`;
  const exerciseList = page.locator('.strength-exercise-rows');
  const names = ['Squat', 'Roning', 'Step-up', 'Bænkpres', 'Dødløft'];
  const note = '1 minut pause mellem sæt';
  const finish = page.getByRole('button', { name: 'Gem og afslut træning' });
  const expectOrder = (order) => expect(exerciseList.locator('.strength-exercise-card-title'))
    .toHaveText(order.map((name, index) => `${index + 1}. ${name}`));

  await evidence.step('Create a workout and verify large set and rep buttons', async () => {
    await page.goto('/login');
    await submitLogin(page, adminEmail);
    await expect(page.getByRole('heading', { name: 'Hej, Testadministrator' })).toBeVisible();
    await page.goto('/styrke/opret');
    await page.getByLabel('Navn på program').fill(programName);
    await page.getByRole('button', { name: 'Opret program', exact: true }).click();
    await expect(page.getByRole('heading', { name: programName })).toBeVisible();
    await page.getByRole('link', { name: /Tilføj træning/ }).click();
    await page.getByLabel('Navn på træning').fill('Styrkepas');
    for (const name of names) {
      await page.getByRole('button', { name: /Tilføj øvelse/ }).click();
      const dialog = page.getByRole('dialog', { name: 'Tilføj øvelse' });
      await dialog.getByLabel('Navn på øvelse').fill(name);
      if (name === 'Squat') {
        for (const label of ['Færre sæt', 'Flere sæt', 'Færre gentagelser', 'Flere gentagelser']) {
          const bounds = await dialog.getByRole('button', { name: label, exact: true }).boundingBox();
          expect(bounds?.width).toBeGreaterThanOrEqual(44);
          expect(bounds?.height).toBeGreaterThanOrEqual(44);
        }
        await dialog.getByRole('button', { name: 'Flere sæt' }).click();
        await dialog.getByRole('button', { name: 'Færre gentagelser' }).click();
        await expect(dialog.getByRole('spinbutton', { name: 'Sæt' })).toHaveValue('4');
        await expect(dialog.getByRole('spinbutton', { name: 'Gentagelser' })).toHaveValue('9');
        await dialog.getByLabel('Note (valgfri)').fill(note);
        await evidence.screenshot('strength-exercise-controls');
      }
      await dialog.getByRole('button', { name: 'Gem øvelse' }).click();
      await expect(dialog).toBeHidden();
    }
    await expectOrder(names);
  });

  await evidence.step('Drag an exercise and reorder with the keyboard, then reload the saved workout', async () => {
    const squat = exerciseList.getByRole('button', { name: 'Flyt Squat', exact: true });
    const roning = exerciseList.getByRole('button', { name: 'Flyt Roning', exact: true });
    await squat.scrollIntoViewIfNeeded();
    const source = await squat.boundingBox();
    const target = await roning.boundingBox();
    const from = { x: Math.round(source.x + source.width / 2), y: Math.round(source.y + source.height / 2) };
    const to = { x: Math.round(target.x + target.width / 2), y: Math.round(target.y + target.height / 2) };
    if (testInfo.project.use.hasTouch) {
      const cdp = await page.context().newCDPSession(page);
      try {
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [from] });
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [to] });
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      } finally {
        await cdp.detach();
      }
    } else {
      await page.mouse.move(from.x, from.y);
      await page.mouse.down();
      await page.mouse.move(to.x, to.y, { steps: 6 });
      await page.mouse.up();
    }
    await expectOrder(['Roning', 'Squat', ...names.slice(2)]);
    await squat.press('ArrowDown');
    const reordered = ['Roning', 'Step-up', 'Squat', ...names.slice(3)];
    await expectOrder(reordered);
    await expect(squat).toBeFocused();
    await evidence.screenshot('strength-reordered');

    const editSquat = exerciseList.getByRole('button', { name: 'Rediger øvelsen Squat', exact: true });
    await editSquat.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Rediger øvelse' });
    await dialog.getByRole('button', { name: 'Flere sæt', exact: true }).click();
    await dialog.getByRole('button', { name: 'Gem øvelse', exact: true }).press('Enter');
    await expect(dialog).toBeHidden();
    await expect(editSquat).toBeFocused();
    await expect(editSquat.getByText(/5 sæt/)).toBeVisible();
    await evidence.screenshot('strength-edit-focus-restored');

    await page.getByRole('button', { name: 'Gem træning', exact: true }).click();
    await page.getByRole('link', { name: 'Rediger', exact: true }).click();
    await expectOrder(reordered);
    await page.getByRole('button', { name: 'Tilbage til program' }).click();
  });

  await evidence.step('Read the exercise note, use the sticky footer and retry after a save failure', async () => {
    await page.getByRole('link', { name: 'Start træning', exact: true }).click();
    const squat = page.locator('.strength-active-card').filter({ hasText: 'Squat' });
    await expect(squat.getByText(note, { exact: true })).toBeVisible();
    const footer = page.locator('.strength-workout-footer');
    await expect(finish).toBeInViewport({ ratio: 1 });
    const bottom = () => footer.evaluate((element) => element.getBoundingClientRect().bottom);
    expect(await bottom()).toBeCloseTo(page.viewportSize().height, 0);
    await evidence.screenshot('strength-active-note');
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    const lastExercise = page.locator('.strength-active-card').filter({ hasText: 'Dødløft' });
    await lastExercise.getByRole('checkbox').check();
    await expect(lastExercise.getByRole('checkbox')).toBeChecked();
    expect(await bottom()).toBeCloseTo(page.viewportSize().height, 0);
    const lastBounds = await lastExercise.boundingBox();
    const footerBounds = await footer.boundingBox();
    expect(lastBounds.y + lastBounds.height).toBeLessThanOrEqual(footerBounds.y + 1);
    await evidence.screenshot('strength-active-footer');

    await page.route('**/complete', (route) => route.fulfill({ status: 503 }), { times: 1 });
    await finish.click();
    const confirm = page.getByRole('dialog', { name: 'Afslut træning?' });
    await confirm.getByRole('button', { name: 'Gem træning' }).click();
    await expect(footer.getByRole('alert')).toBeVisible();
    await expect(lastExercise.getByRole('checkbox')).toBeChecked();
    await evidence.screenshot('strength-active-save-error');
    await finish.click();
    await confirm.getByRole('button', { name: 'Gem træning' }).click();
    await expect(page.getByRole('heading', { name: programName })).toBeVisible();
  });
});

test('view strength progression by exercise and period', async ({ page, evidence }, testInfo) => {
  const exercise = page.getByLabel('Øvelse', { exact: true });
  const period = page.getByLabel('Periode', { exact: true });
  const results = page.getByRole('table', { name: 'Resultater pr. dag' });
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.name));

  await evidence.step('Open the dashboard link and compare weight and repetitions', async () => {
    await page.goto('/login');
    await submitLogin(page, adminEmail);
    await page.getByRole('link', { name: 'Se styrkeprogression' }).click();
    await expect(page).toHaveURL('/progression');
    await expect(page.getByRole('heading', { name: 'Styrkeprogression' })).toBeVisible();
    await expect(period).toHaveValue('90');
    await exercise.selectOption({ label: 'Bench Press' });
    const weight = page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Højeste vægt' }) });
    await expect(weight.getByText('52,5 kg', { exact: true })).toBeVisible();
    await expect(weight.locator('circle')).toHaveCount(3);
    await expect(page.getByRole('heading', { name: 'Flest gentagelser pr. sæt' })).toBeVisible();
    await expect(results.getByRole('cell', { name: '32,5', exact: true })).toBeVisible();
    await expect(results.getByRole('cell', { name: '10', exact: true })).toBeVisible();
    await expect(page.getByText('2.365 kg', { exact: true })).toBeVisible();
    await evidence.screenshot('strength-progression-weight');
  });

  await evidence.step('Switch to a timed exercise and verify mobile layout', async () => {
    await exercise.selectOption({ label: 'Side Plank' });
    await expect(page.getByRole('heading', { name: 'Længste varighed' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Højeste vægt' })).toHaveCount(0);
    await expect(results.getByRole('cell', { name: '1:16', exact: true })).toBeVisible();
    await expect(results.getByRole('cell', { name: '0:38', exact: true })).toBeVisible();
    await evidence.screenshot('strength-progression-duration');
    if (testInfo.project.use.hasTouch) {
      await page.setViewportSize({ width: 360, height: 844 });
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(360);
      await expect(exercise).toBeVisible();
      await expect(page.getByRole('link', { name: 'Profil og indstillinger' })).toBeInViewport({ ratio: 1 });
      await evidence.screenshot('strength-progression-360');
    }
  });

  await evidence.step('Filter historical results and retry a failed request', async () => {
    const narrow = page.waitForResponse((response) => response.url().endsWith('/api/strength/progression?days=30'));
    await period.selectOption('30');
    expect((await narrow).ok()).toBe(true);
    await expect(period).toBeEnabled();
    await expect(exercise).toHaveValue('catalog:side-plank:time');
    await expect(page.getByText('Du har ingen gennemførte registreringer for øvelsen i perioden.')).toBeVisible();
    await expect(results).toHaveCount(0);
    await evidence.screenshot('strength-progression-empty-period');

    await page.route('**/api/strength/progression?days=365', (route) =>
      route.fulfill({ status: 503 }), { times: 1 });
    await period.selectOption('365');
    await expect(page.getByRole('alert')).toContainText('Kunne ikke kontakte serveren');
    await page.getByRole('button', { name: 'Prøv igen' }).click();
    await exercise.selectOption({ label: 'Side Plank' });
    await expect(results.getByRole('cell', { name: '1:16', exact: true })).toBeVisible();
    await page.goto('/styrke');
    await page.getByRole('link', { name: 'Se styrkeprogression' }).click();
    await expect(page.getByRole('heading', { name: 'Styrkeprogression' })).toBeVisible();
    expect(errors).toEqual([]);
  });
});

test('view earned strength badges and retry an interrupted load', async ({ page, evidence }, testInfo) => {
  await evidence.step('Open the strength badges from strength training and retry the request', async () => {
    await page.goto('/login');
    await submitLogin(page, adminEmail);
    await page.getByRole('navigation', { name: 'Hovednavigation' })
      .getByRole('link', { name: 'Træning', exact: true }).click();
    await page.route('**/api/strength/milestones', (route) => route.fulfill({ status: 503 }), { times: 1 });
    await page.getByRole('link', { name: 'Se styrkebadges' }).click();
    await expect(page).toHaveURL('/styrke/milepaele');
    await expect(page.getByRole('alert')).toContainText('Kunne ikke kontakte serveren');
    await page.getByRole('button', { name: 'Prøv igen' }).click();
  });

  await evidence.step('Check the two earned badges and their dates', async () => {
    const record = page.getByRole('article', { name: 'Personlig rekord' });
    const volume = page.getByRole('article', { name: '1.000 kg løftet' });
    await expect(record.getByText('Optjent', { exact: true })).toBeVisible();
    await expect(volume.getByText('Optjent', { exact: true })).toBeVisible();
    const earned = [record.locator('time'), volume.locator('time')];
    for (const date of earned) {
      await expect(date).toHaveAttribute('datetime', /^\d{4}-\d{2}-\d{2}$/);
    }
    const progress = volume.getByRole('progressbar', { name: 'Fremskridt mod løftet vægt' });
    await expect(progress).toHaveAttribute('max', '1000');
    await expect(progress).toHaveAttribute('value', '1000');
    await evidence.screenshot('strength-badges-earned');

    if (testInfo.project.use.hasTouch) {
      await page.setViewportSize({ width: 360, height: 844 });
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(360);
      await expect(page.getByRole('heading', { name: 'Styrkebadges' })).toBeVisible();
      await evidence.screenshot('strength-badges-360');
    }
    await page.getByRole('link', { name: 'Tilbage til styrketræning' }).click();
    await expectStrength(page);
  });
});

test('find a catalog exercise and save a timed workout', async ({ page, evidence }, testInfo) => {
  const program = `Katalogtræning ${testInfo.project.name}`;
  const dialog = page.getByRole('dialog', { name: 'Tilføj øvelse' });
  const search = dialog.getByRole('searchbox', { name: 'Søg efter en øvelse' });
  const minutes = (container) => container.getByRole('spinbutton', { name: 'Minutter' });
  const seconds = (container) => container.getByRole('spinbutton', { name: 'Sekunder' });

  await evidence.step('Search English, Danish and keyboard spellings in the exercise catalog', async () => {
    await page.goto('/login');
    await submitLogin(page, adminEmail);
    await expect(page.getByRole('heading', { name: 'Hej, Testadministrator' })).toBeVisible();
    await page.goto('/styrke/opret');
    await page.getByLabel('Navn på program').fill(program);
    await page.getByRole('button', { name: 'Opret program', exact: true }).click();
    await page.getByRole('link', { name: /Tilføj træning/ }).click();
    await page.getByLabel('Navn på træning').fill('Katalogpas');
    await page.getByRole('button', { name: /Tilføj øvelse/ }).click();
    await dialog.getByRole('button', { name: 'Vælg fra katalog' }).click();

    const deadlift = dialog.getByRole('button', { name: 'Vælg Deadlift', exact: true });
    for (const query of ['deadlift', 'dødløft', 'doedloeft']) {
      await search.fill(query);
      await expect(deadlift).toBeVisible();
    }
    const category = dialog.getByLabel('Kategori');
    await category.selectOption({ label: 'Ben' });
    await expect(dialog.getByText('Ingen øvelser matcher din søgning.')).toBeVisible();
    await category.selectOption({ label: 'Hele kroppen' });
    await expect(deadlift).toBeVisible();
    await expect(dialog.getByText('1 øvelse fundet', { exact: true })).toBeVisible();
    const illustration = dialog.getByRole('img', { name: /vægtstang med begge hænder foran lårene/ });
    await expect(illustration).toBeVisible();
    await expect.poll(() => illustration.evaluate((image) => image.naturalWidth)).toBeGreaterThan(0);
    await evidence.screenshot('strength-catalog-danish-search');

    await deadlift.click();
    await expect(dialog.getByRole('spinbutton', { name: 'Vægt (kg)' })).toBeVisible();
    await dialog.getByRole('spinbutton', { name: 'Vægt (kg)' }).fill('20');
    await dialog.getByRole('button', { name: 'Gem øvelse' }).click();
  });

  await evidence.step('Save a plank using minutes and seconds and reopen the workout', async () => {
    await page.getByRole('button', { name: /Tilføj øvelse/ }).click();
    await dialog.getByRole('button', { name: 'Vælg fra katalog' }).click();
    await dialog.getByLabel('Kategori').selectOption({ label: 'Mave og core' });
    await search.fill('planke');
    await dialog.getByRole('button', { name: 'Vælg Plank', exact: true }).click();
    await expect(dialog.getByRole('spinbutton', { name: 'Vægt (kg)' })).toHaveCount(0);
    await minutes(dialog).fill('1');
    await seconds(dialog).fill('15');
    await expect(minutes(dialog)).toHaveValue('1');
    await expect(seconds(dialog)).toHaveValue('15');
    await evidence.screenshot('strength-catalog-plank-duration');
    await dialog.getByRole('button', { name: 'Gem øvelse' }).click();
    await page.getByRole('button', { name: 'Gem træning', exact: true }).click();
    await expect(page.getByRole('heading', { name: program })).toBeVisible();

    await page.getByRole('link', { name: 'Start træning', exact: true }).click();
    const plank = page.locator('.strength-active-card').filter({ hasText: 'Plank' });
    await expect(minutes(plank)).toHaveValue('1');
    await expect(seconds(plank)).toHaveValue('15');
    await seconds(plank).fill('26');
    await plank.getByRole('checkbox').check();
    await expect(seconds(plank)).toHaveValue('26');
    await expect(plank.getByRole('checkbox')).toBeChecked();
    await evidence.screenshot('strength-catalog-active-duration');
    await page.getByRole('button', { name: 'Gem og afslut træning' }).click();
    await page.getByRole('dialog', { name: 'Afslut træning?' }).getByRole('button', { name: 'Gem træning' }).click();
    await expect(page.getByRole('heading', { name: program })).toBeVisible();

    await page.getByRole('link', { name: 'Start træning', exact: true }).click();
    await expect(plank.getByText(/Sidst: .*1 min 26 sek/)).toBeVisible();
  });
});

test('enter a weight and reuse personal catalog history across programs', async ({ page, evidence }, testInfo) => {
  const card = page.locator('.strength-active-card').filter({ hasText: 'Squat' });
  const weight = card.getByRole('textbox', { name: 'Vægt (kg)' });
  const finish = page.getByRole('button', { name: 'Gem og afslut træning' });

  async function createProgram(name, plannedWeight) {
    await page.goto('/styrke/opret');
    await page.getByLabel('Navn på program').fill(name);
    await page.getByRole('button', { name: 'Opret program', exact: true }).click();
    await page.getByRole('link', { name: /Tilføj træning/ }).click();
    await page.getByLabel('Navn på træning').fill('Squatpas');
    await page.getByRole('button', { name: /Tilføj øvelse/ }).click();
    const dialog = page.getByRole('dialog', { name: 'Tilføj øvelse' });
    await dialog.getByRole('button', { name: 'Vælg fra katalog' }).click();
    await dialog.getByRole('searchbox').fill('knæbøjning');
    await dialog.getByRole('button', { name: 'Vælg Squat', exact: true }).click();
    await dialog.getByRole('spinbutton', { name: 'Vægt (kg)' }).fill(plannedWeight);
    await dialog.getByRole('spinbutton', { name: 'Sæt', exact: true }).fill('2');
    await dialog.getByRole('spinbutton', { name: 'Gentagelser', exact: true }).fill('15');
    await dialog.getByRole('button', { name: 'Gem øvelse' }).click();
    await page.getByRole('button', { name: 'Gem træning', exact: true }).click();
    await expect(page.getByRole('heading', { name })).toBeVisible();
    await page.getByRole('link', { name: 'Start træning', exact: true }).click();
  }

  await evidence.step('Reject invalid weight and save a Danish decimal weight', async () => {
    await page.goto('/login');
    await submitLogin(page, adminEmail);
    await expect(page.getByRole('heading', { name: 'Hej, Testadministrator' })).toBeVisible();
    const program = `Historik A ${testInfo.project.name}`;
    await createProgram(program, '0');
    await weight.fill('25,5');
    await weight.press('Tab');
    await expect(weight).toHaveValue('25,5');
    await expect(weight).not.toHaveAttribute('aria-invalid', 'true');
    await weight.fill('');
    await weight.press('Tab');
    await expect(weight).toHaveAttribute('aria-invalid', 'true');
    await weight.fill('1001');
    await weight.press('Tab');
    await expect(weight).toHaveAttribute('aria-invalid', 'true');
    await expect(finish).toBeDisabled();
    await evidence.screenshot('strength-weight-validation');

    await card.getByRole('button', { name: 'Øg vægten' }).click();
    await expect(weight).toHaveValue('28');
    await card.getByRole('button', { name: 'Sænk vægten' }).click();
    await expect(weight).toHaveValue('25,5');
    await expect(weight).not.toHaveAttribute('aria-invalid', 'true');
    await card.getByRole('checkbox').check();
    await finish.click();
    await page.getByRole('dialog', { name: 'Afslut træning?' }).getByRole('button', { name: 'Gem træning' }).click();
    await expect(page.getByRole('heading', { name: program })).toBeVisible();
  });

  await evidence.step('Show the prior effort and prefer an explicit plan over the last weight', async () => {
    const program = `Historik B ${testInfo.project.name}`;
    await createProgram(program, '35');
    await expect(weight).toHaveValue('35');
    await expect(card.getByText('Sidst: 25,5 kg · 2 × 15', { exact: true })).toBeVisible();
    await evidence.screenshot('strength-weight-planned-with-history');

    await weight.fill('38.25');
    await weight.press('Tab');
    await expect(weight).toHaveValue('38,25');
    await page.getByRole('link', { name: 'Afbryd og gå tilbage' }).click();
    await page.getByRole('button', { name: 'Forlad uden at gemme' }).click();
    await expect(page.getByRole('heading', { name: program })).toBeVisible();
  });

  await evidence.step('Prefill the last saved weight when the plan has no weight', async () => {
    await page.getByRole('link', { name: 'Rediger', exact: true }).click();
    const editSquat = page.getByRole('button', { name: 'Rediger øvelsen Squat' });
    await editSquat.click();
    const dialog = page.getByRole('dialog', { name: 'Rediger øvelse' });
    await expect(dialog.getByRole('textbox', { name: 'Navn på øvelse' })).toBeFocused();
    await dialog.getByRole('spinbutton', { name: 'Vægt (kg)' }).fill('0');
    await dialog.getByRole('button', { name: 'Gem øvelse' }).click();
    await expect(editSquat.getByText(/^0 kg/)).toBeVisible();
    await page.getByRole('button', { name: 'Gem træning', exact: true }).click();
    await page.getByRole('link', { name: 'Start træning', exact: true }).click();
    await expect(weight).toHaveValue('25,5');
    await expect(card.getByText('Sidst: 25,5 kg · 2 × 15', { exact: true })).toBeVisible();
    await evidence.screenshot('strength-weight-prefilled');
  });
});
