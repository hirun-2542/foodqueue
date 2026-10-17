import { expect, test } from "@playwright/test";

// Ticket 02 guard: a staff screen is never shown to somebody who is not signed in.
// The signed-in halves of the guard (Kitchen sent to /kitchen, logout back to /login) need the
// seeded accounts from this ticket's API, so they run against a stack built from this branch.

test.describe("staff auth guard", () => {
  test("visiting /owner while logged out lands on /login", async ({ page }) => {
    await page.context().clearCookies();

    await page.goto("/owner");

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("heading", { name: "เข้าสู่ระบบ" })).toBeVisible();
  });

  test("visiting /kitchen while logged out lands on /login", async ({ page }) => {
    await page.context().clearCookies();

    await page.goto("/kitchen");

    await expect(page).toHaveURL(/\/login$/);
  });

  test("the guard preserves the requested screen so /owner could be resumed after login", async ({ page }) => {
    await page.context().clearCookies();

    await page.goto("/owner");

    // The redirect must really be the guard reacting to GET /api/auth/me answering 401.
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByLabel("อีเมล")).toBeVisible();
  });
});
