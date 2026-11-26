import { randomBytes } from 'node:crypto';
import { defineConfig } from '@playwright/test';

const origin = 'https://127.0.0.1:47192';
// Workers inherit this from the runner when they load the config.
process.env.FITNESS_BROWSER_PASSWORD ??= `Browser!1a-${randomBytes(24).toString('hex')}`;

export default defineConfig({
  testMatch: '*.spec.mjs',
  outputDir: 'test-results',
  reporter: 'list',
  workers: 1,
  timeout: 60_000,
  use: {
    baseURL: origin,
    ignoreHTTPSErrors: true,
    serviceWorkers: 'block',
    actionTimeout: 10_000,
    // Native traces can retain form values, bearer tokens and cookies.
    trace: 'off',
  },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1440, height: 900 } } },
    {
      name: 'mobile',
      use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true },
    },
  ],
  webServer: {
    command: 'exec node serve.mjs',
    env: { FITNESS_BROWSER_ORIGIN: origin },
    url: origin,
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    timeout: 240_000,
    gracefulShutdown: { signal: 'SIGTERM', timeout: 5000 },
  },
});
