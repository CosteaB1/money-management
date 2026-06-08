import { expect, test } from '@playwright/test';

test('dashboard renders, then create a new account via the dialog', async ({ page }) => {
  // 1. Dashboard
  await page.goto('/');
  await expect(page.getByTestId('net-worth-card')).toBeVisible();
  // ro-MD formats currency with either "MDL" or just "L" depending on the
  // browser's ICU data — assert on the digit grouping which is stable.
  // Net worth is the sum of `balanceMdl` across non-archived accounts.
  await expect(page.getByTestId('net-worth-amount')).toContainText('100');

  // 2. Navigate to accounts via sidebar
  await page.getByTestId('nav-accounts').click();
  await expect(page).toHaveURL(/\/accounts$/);
  await expect(page.getByTestId('accounts-table')).toBeVisible();

  const initialRows = await page.getByTestId('account-row').count();
  expect(initialRows).toBeGreaterThan(0);

  // 3. Add an account
  await page.getByTestId('add-account-button').click();
  await page.getByTestId('account-name-input').fill('Revolut EUR');
  // Type select defaults to Cash — keep it.
  await page.getByTestId('account-balance-input').fill('100');

  const today = new Date().toISOString().slice(0, 10);
  await page.getByTestId('account-opening-date-input').fill(today);

  await page.getByTestId('account-submit-button').click();

  // Row count grows by 1, new account is visible.
  await expect(page.getByText('Revolut EUR')).toBeVisible();
  const newRows = await page.getByTestId('account-row').count();
  expect(newRows).toBe(initialRows + 1);
});
