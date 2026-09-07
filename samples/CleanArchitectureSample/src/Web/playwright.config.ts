import { defineConfig } from '@playwright/test';

const baseURL = process.env.SAMPLE_BASE_URL ?? 'https://localhost:5199';
if (!['localhost', '127.0.0.1', '[::1]'].includes(new URL(baseURL).hostname)) {
  throw new Error(
    'Queue operation tests must target a local sample: they enqueue, cancel, replay, and flush demo work.'
  );
}

export default defineConfig({
  testDir: './tests',
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 20_000 },
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 1000 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH }
      : undefined
  }
});
