import { expect, test } from '@playwright/test';

test('operator can sign in through the real API', async ({ page }) => {
  await page.goto('/login');

  await page.getByRole('button', { name: 'Operator' }).click();

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('heading', { name: /operations overview/i })).toBeVisible();
});
