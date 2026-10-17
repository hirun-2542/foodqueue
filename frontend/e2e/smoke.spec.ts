import { expect, test } from "@playwright/test";

// Smoke: the login form renders through Caddy at the same origin that serves /api and /hubs.
test("login form is visible at /login", async ({ page }) => {
  await page.goto("/login");

  await expect(page.getByRole("heading", { name: "เข้าสู่ระบบ" })).toBeVisible();
  await expect(page.getByLabel("อีเมล")).toBeVisible();
  await expect(page.locator("#password")).toBeVisible();
  await expect(page.getByRole("button", { name: "เข้าสู่ระบบ" })).toBeVisible();
});

test("root redirects to /login", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/login$/);
});
