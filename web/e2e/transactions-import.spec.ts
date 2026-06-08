import { expect, test } from '@playwright/test';

test('import a statement: preview, exclude one row, commit', async ({ page }) => {
  await page.goto('/transactions/import');

  // Account selector populates from the seed list. Keep the default value
  // chosen by the upload component (first non-archived).
  await expect(page.getByTestId('import-account-select')).toBeVisible();

  // Upload a tiny in-memory PDF — the mock handler ignores file contents.
  const pdfBytes = Buffer.from('%PDF-1.4\n%mock pdf for tests\n');
  await page.getByTestId('import-file-input').setInputFiles({
    name: 'maib-statement.pdf',
    mimeType: 'application/pdf',
    buffer: pdfBytes,
  });

  await page.getByTestId('import-parse-button').click();

  // Preview table appears.
  await expect(page.getByTestId('import-preview-table')).toBeVisible({ timeout: 10_000 });
  const rows = page.getByTestId('import-preview-row');
  const total = await rows.count();
  expect(total).toBeGreaterThan(0);

  // Uncheck the first non-duplicate row to exclude it.
  const firstCheckbox = page.getByTestId('import-row-checkbox-0');
  const wasChecked = await firstCheckbox.isChecked();
  if (wasChecked) {
    await firstCheckbox.uncheck();
  } else {
    await firstCheckbox.check();
    await firstCheckbox.uncheck();
  }

  await page.getByTestId('import-commit-button').click();

  // Landed on /transactions and the success toast is visible.
  await expect(page).toHaveURL(/\/transactions$/, { timeout: 10_000 });
  await expect(page.getByText(/Imported \d+ transactions/i)).toBeVisible({ timeout: 5_000 });
});
