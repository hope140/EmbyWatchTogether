const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './specs',
  timeout: 15000,
  expect: { timeout: 5000 },
  fullyParallel: false,
  reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:4179',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off'
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } }
  ],
  webServer: {
    command: 'node fixture/server.js',
    url: 'http://127.0.0.1:4179',
    reuseExistingServer: false,
    timeout: 10000
  }
});
