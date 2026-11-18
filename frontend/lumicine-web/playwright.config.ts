import { defineConfig } from '@playwright/test';

if (process.env.LUMICINE_E2E_CONFIGURED !== '1') {
  throw new Error('Run `npm run e2e` to create an isolated database and local configuration before Playwright.');
}

const widths = [375, 768, 1440];

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  retries: 0,
  reporter: 'list',
  use: { baseURL: 'https://localhost:5173', ignoreHTTPSErrors: true, trace: 'retain-on-failure' },
  projects: ['chromium', 'webkit'].flatMap((browserName) => widths.map((width) => ({
    name: `${browserName}-${width}`,
    use: { browserName: browserName as 'chromium' | 'webkit', viewport: { width, height: 900 } },
  }))),
  webServer: [
    {
      command: 'node scripts/start-api.mjs',
      url: 'https://localhost:7005/api/genres?page=1&pageSize=1',
      reuseExistingServer: false,
      ignoreHTTPSErrors: true,
      timeout: 120_000,
    },
    {
      command: 'npm run dev -- --host 127.0.0.1',
      url: 'https://localhost:5173',
      reuseExistingServer: false,
      ignoreHTTPSErrors: true,
      timeout: 60_000,
    },
  ],
});
