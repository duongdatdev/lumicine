import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';

function tomorrowInVietnam() {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date());
  const tomorrow = new Date(new Date(`${today}T00:00:00+07:00`).getTime() + 86_400_000);
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(tomorrow);
}

test('loads seeded catalog, filters movies, and opens a showtime seat map', async ({ page }, testInfo) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));

  await page.goto('/');
  await expect(page.getByRole('heading', { name: /trải nghiệm điện ảnh/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /the glass horizon/i }).first()).toBeVisible();
  await page.getByPlaceholder('Tìm kiếm phim theo tên...').fill('Glass Horizon');
  await page.getByPlaceholder('Tìm kiếm phim theo tên...').press('Enter');
  await expect(page).toHaveURL(/search=Glass\+Horizon/);
  await expect(page.getByText('The Glass Horizon', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('The Last Orbit', { exact: true })).toHaveCount(0);

  const movies = await page.request.get('/api/movies?page=1&pageSize=12');
  expect(movies.ok()).toBeTruthy();
  const movieData = await movies.json();
  expect(movieData.items.length).toBeGreaterThan(0);
  const showtimes = await page.request.get(`/api/showtimes?date=${tomorrowInVietnam()}&page=1&pageSize=10`);
  expect(showtimes.ok()).toBeTruthy();
  const showtimeData = await showtimes.json();
  expect(showtimeData.items.length).toBeGreaterThan(0);

  await page.goto(`/showtimes/${showtimeData.items[0].id}/seats`);
  await expect(page.getByText('Màn hình')).toBeVisible();
  await expect(page.getByRole('button', { name: /^Hàng A Ghế 1,/ })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({ path: testInfo.outputPath('seat-map.png'), fullPage: true });
  expect(errors).toEqual([]);
});

test('login page rejects empty submission and protected tickets route redirects', async ({ page }) => {
  await page.goto('/account/login');
  await expect(page.getByRole('heading', { name: /đăng nhập/i })).toBeVisible();
  await page.locator('form').getByRole('button', { name: /đăng nhập/i }).click();
  await expect(page.getByText(/email/i).first()).toBeVisible();

  await page.goto('/tickets');
  await expect(page).toHaveURL(/account\/login/);
});

test('gets antiforgery token, logs in seeded admin, and opens the reporting dashboard', async ({ page }, testInfo) => {
  const blockedWrite = await page.request.post('/api/auth/login', { data: { email: 'nobody@example.test', password: 'invalid' } });
  expect(blockedWrite.status()).toBe(400);

  const date = tomorrowInVietnam();
  const index = ['chromium-375', 'chromium-768', 'chromium-1440', 'webkit-375', 'webkit-768', 'webkit-1440'].indexOf(testInfo.project.name);
  await page.goto(`/admin/dashboard?fromDate=${date}&toDate=${date}&groupBy=showtime`);
  await expect(page).toHaveURL(/account\/login/);
  await page.getByLabel('Địa chỉ Email').fill(`admin${index + 1}@lumicine.example.test`);
  await page.getByPlaceholder('••••••••').fill(process.env.E2E_USER_PASSWORD!);
  const loginResponse = page.waitForResponse((response) => response.url().includes('/api/auth/login'));
  await page.locator('form').getByRole('button', { name: 'Đăng nhập' }).click();
  expect((await loginResponse).status()).toBe(200);
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  await expect(page.getByRole('heading', { level: 1, name: 'Tổng quan & Báo cáo Doanh thu' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Tổng quan & Báo cáo Doanh thu' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({ path: testInfo.outputPath('admin-report.png'), fullPage: true });

  if (testInfo.project.name === 'chromium-1440') {
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Xuất CSV Suất chiếu' }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe(`showtimes-${date}-to-${date}.csv`);
    const csv = await readFile((await download.path())!, 'utf8');
    expect(csv).toContain('"showtimeId","movieId","movieTitle"');
    expect(csv).toContain('The Glass Horizon');
  }
});

test('customer holds and cancels a seat', async ({ page }, testInfo) => {
  test.setTimeout(90_000);
  const date = tomorrowInVietnam();
  const showtimesResponse = await page.request.get(`/api/showtimes?date=${date}&page=1&pageSize=20`);
  expect(showtimesResponse.ok()).toBeTruthy();
  const showtimes = (await showtimesResponse.json()).items;
  const index = ['chromium-375', 'chromium-768', 'chromium-1440', 'webkit-375', 'webkit-768', 'webkit-1440'].indexOf(testInfo.project.name);
  const showtime = showtimes[index];
  expect(showtime).toBeTruthy();

  const seatsResponse = await page.request.get(`/api/showtimes/${showtime.id}/seats`);
  expect(seatsResponse.ok()).toBeTruthy();
  const seat = (await seatsResponse.json()).find((item: { rowLabel: string; number: number }) => item.rowLabel === 'A' && item.number === 1);
  expect(seat).toBeTruthy();

  await page.goto(`/account/login?returnTo=${encodeURIComponent(`/showtimes/${showtime.id}/seats`)}`);
  await page.getByLabel('Địa chỉ Email').fill(`customer${index + 1}@lumicine.example.test`);
  await page.getByPlaceholder('••••••••').fill(process.env.E2E_USER_PASSWORD!);
  const customerLogin = page.waitForResponse((response) => response.url().includes('/api/auth/login'));
  await page.locator('form').getByRole('button', { name: 'Đăng nhập' }).click();
  expect((await customerLogin).status()).toBe(200);
  await expect(page).toHaveURL(new RegExp(`/showtimes/${showtime.id}/seats`));
  await page.getByRole('button', { name: /^Hàng A Ghế 1,/ }).click();
  await page.getByRole('button', { name: 'Giữ ghế & Thanh toán' }).click();
  await expect(page).toHaveURL(/\/bookings\//);

  await page.goto(`/showtimes/${showtime.id}/seats`);
  await expect(page.getByText('Bạn đang có đơn giữ chỗ chờ thanh toán cho suất này')).toBeVisible();
  await page.getByRole('button', { name: 'Hủy giữ chỗ' }).click();
  await expect(page.getByText('Bạn đang có đơn giữ chỗ chờ thanh toán cho suất này')).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Hàng A Ghế 1,.*Còn trống/ })).toBeEnabled();

});

test('staff can read the seeded catalog and open check-in', async ({ page }) => {
  await page.goto(`/account/login?returnTo=${encodeURIComponent('/staff/catalog')}`);
  await page.getByLabel('Địa chỉ Email').fill('staff@lumicine.example.test');
  await page.getByPlaceholder('••••••••').fill(process.env.E2E_USER_PASSWORD!);
  const staffLogin = page.waitForResponse((response) => response.url().includes('/api/auth/login'));
  await page.locator('form').getByRole('button', { name: 'Đăng nhập' }).click();
  expect((await staffLogin).status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1, name: 'Danh mục hệ thống' })).toBeVisible();
  await expect(page.getByText('The Glass Horizon', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Rạp chiếu' }).click();
  await expect(page.getByText('Riverlight Cinema')).toBeVisible();
  await page.goto('/staff/checkin');
  await expect(page.getByPlaceholder('Nhập mã vé (VD: TC-ABCD-1234)')).toBeVisible();
});
