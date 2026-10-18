import { type APIRequestContext, type Page, expect, test } from "@playwright/test";

// Ticket 15: the owner's new-order popup (frames o20/o21) on a running stack built from this branch.
// Orders are submitted through the customer API of a real table round; every round this file creates
// is cancelled afterwards and the run's own table (a table of its own, per project) is switched off again, so no real
// table's bill is touched.
//
// The popup consumes the hub's `orderCreated`, so every case first waits for the page's live
// connection to finish its handshake — otherwise the order would be submitted before the page can
// hear it, which is a test race, not a product behaviour.
//
// Sound is observed, not heard: an init script counts the oscillators the page's Web Audio chime
// creates (two tones per chime), so "one chime" means the counter went up by exactly 2.
//
// Run this file with `--workers=1` against a shared stack: the popup is shop-wide (the newest round
// replaces whatever is on screen), so the two viewport projects running at the same time would keep
// replacing each other's dialog. Each project uses its own table, so serial runs never collide.

const OWNER = { email: "owner@demo.local", password: "Demo1234!" };
const KITCHEN = { email: "kitchen@demo.local", password: "Demo1234!" };
const TONES_PER_CHIME = 2;

/**
 * The run's own table, so no real table's bill is touched; it is switched off again afterwards. One
 * per project, because the two viewport projects run in parallel and would fight over one round.
 */
function runTableName(): string {
  return `E2E-15-${test.info().project.name}`;
}

declare global {
  interface Window {
    /** Oscillators the page's chime has created since load (set up by the init script below). */
    __fqTones: number;
  }
}

interface TableRow {
  id: number;
  name: string;
  seats: number;
  zone: string | null;
  sortOrder: number;
  isActive: boolean;
}

interface Round {
  table: TableRow;
  sessionId: number;
  token: string;
}

const created: number[] = [];
let round: Round;

async function login(request: APIRequestContext, who: typeof OWNER) {
  const response = await request.post("/api/auth/login", { data: who });
  expect(response.status()).toBe(204);
}

async function setActive(request: APIRequestContext, table: TableRow, isActive: boolean) {
  const response = await request.put(`/api/tables/${table.id}`, { data: { ...table, isActive } });
  expect(response.ok(), await response.text()).toBeTruthy();
}

/**
 * A round left open on the run's table by an earlier interrupted run is closed first: its rounds are
 * cancelled and the round is voided, so a rerun always starts from a free table.
 */
async function voidLeftoverRound(request: APIRequestContext, table: TableRow) {
  const open: Array<{ id: number; tableName: string }> = await (
    await request.get("/api/sessions?status=Open")
  ).json();
  const leftover = open.find((session) => session.tableName === table.name);
  if (!leftover) return;
  const detail: { orders: Array<{ id: number; status: string }> } = await (
    await request.get(`/api/sessions/${leftover.id}`)
  ).json();
  for (const order of detail.orders.filter((row) => row.status !== "Cancelled")) {
    await request.post(`/api/orders/${order.id}/cancel`, { data: { reason: "e2e ticket 15: รอบค้างจากรอบก่อน" } });
  }
  const voided = await request.post(`/api/sessions/${leftover.id}/void`);
  expect(voided.ok(), await voided.text()).toBeTruthy();
}

/** Activates (creating it the first time) the run's table and opens a round on it. */
async function openRound(request: APIRequestContext): Promise<Round> {
  const tables: TableRow[] = await (await request.get("/api/tables")).json();
  const known = tables.find((row) => row.name === runTableName());
  if (known && !known.isActive) await setActive(request, known, true);
  if (!known) {
    const response = await request.post("/api/tables", { data: { name: runTableName(), seats: 4, isActive: true } });
    expect(response.ok(), await response.text()).toBeTruthy();
  }
  const reread: TableRow[] = await (await request.get("/api/tables")).json();
  const table = reread.find((row) => row.name === runTableName());
  if (!table) throw new Error(`the stack did not keep the run's table ${runTableName()}`);

  await voidLeftoverRound(request, table);

  const packages: Array<{ id: number; isActive: boolean }> = await (await request.get("/api/packages")).json();
  const pkg = packages.find((p) => p.isActive);
  if (!pkg) throw new Error("the stack has no active package to open a round with");
  const opened = await request.post("/api/sessions", {
    data: { tableId: table.id, packageId: pkg.id, adultCount: 2, childCount: 0 },
  });
  expect(opened.ok(), await opened.text()).toBeTruthy();
  const session: { id: number; qrUrl: string } = await opened.json();
  return { table, sessionId: session.id, token: session.qrUrl.split("/s/")[1] };
}

/**
 * Opens a page and waits until its live connection has answered the handshake, so an order
 * submitted afterwards is really heard. Used by every case that expects the popup (or its absence).
 */
async function openLive(page: Page, path: string) {
  const handshake = new Promise<void>((resolve) => {
    page.on("websocket", (socket) => {
      if (!socket.url().includes("/hubs/live")) return;
      socket.once("framereceived", () => resolve());
    });
  });
  await page.goto(path);
  await handshake;
}

/**
 * Sends one round from the table's QR and returns its id. The customer's answer carries no id, so
 * the round is found in the staff list (`request` holds an Owner or Kitchen cookie) as the newest
 * order of this run's table.
 */
async function submitOrder(request: APIRequestContext): Promise<number> {
  const menu: { categories: Array<{ items: Array<{ id: number; kind: string; isAvailable: boolean }> }> } = await (
    await request.get(`/api/s/${round.token}/menu`)
  ).json();
  const items = menu.categories.flatMap((category) => category.items).filter((item) => item.isAvailable);
  const buffet = items.find((item) => item.kind === "Buffet");
  const alaCarte = items.find((item) => item.kind === "AlaCarte");
  const lines = [
    ...(buffet ? [{ menuItemId: buffet.id, quantity: 2, note: "ไม่ติดมัน" }] : []),
    ...(alaCarte ? [{ menuItemId: alaCarte.id, quantity: 1, note: null }] : []),
  ];
  const response = await request.post(`/api/s/${round.token}/orders`, { data: { items: lines } });
  expect(response.status(), await response.text()).toBe(201);
  const board: Array<{ id: number; sessionId: number }> = await (await request.get("/api/orders")).json();
  const id = Math.max(...board.filter((order) => order.sessionId === round.sessionId).map((order) => order.id));
  expect(created).not.toContain(id);
  created.push(id);
  return id;
}

async function countTones(page: Page): Promise<number> {
  return page.evaluate(() => window.__fqTones);
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.__fqTones = 0;
    const original = AudioContext.prototype.createOscillator;
    AudioContext.prototype.createOscillator = function (this: AudioContext) {
      window.__fqTones += 1;
      return original.call(this);
    };
  });
});

test.beforeAll(async ({ playwright, baseURL }) => {
  const request = await playwright.request.newContext({ baseURL });
  await login(request, OWNER);
  round = await openRound(request);
  await request.dispose();
});

test.afterAll(async ({ playwright, baseURL }) => {
  if (!round) return;
  const request = await playwright.request.newContext({ baseURL });
  await login(request, OWNER);
  for (const id of created) {
    await request.post(`/api/orders/${id}/cancel`, { data: { reason: "e2e ticket 15: ป๊อปอัปออเดอร์ใหม่" } });
  }
  await request.post(`/api/sessions/${round.sessionId}/void`);
  await setActive(request, round.table, false);
  await request.dispose();
});

test.describe.configure({ mode: "serial" });

test("an owner on any screen gets the popup and one chime; ไปหน้าออเดอร์ only navigates", async ({ page }) => {
  await login(page.request, OWNER);
  await openLive(page, "/owner/tables");
  await expect(page.getByRole("heading", { name: "โต๊ะ", exact: true })).toBeVisible();
  // The browser's first interaction with this page: from here on the chime may play.
  await page.getByRole("heading", { name: "โต๊ะ", exact: true }).click();

  const orderId = await submitOrder(page.request);
  const dialog = page.getByRole("dialog", { name: `โต๊ะ ${runTableName()}` });
  await expect(dialog).toBeVisible({ timeout: 5_000 });
  await expect(dialog.getByText("มีออเดอร์ใหม่")).toBeVisible();
  await expect(dialog.getByText(/^รอบที่ \d+ · ส่ง \d\d:\d\d$/)).toBeVisible();
  await expect(dialog.getByText(/\d+ ชิ้น$/)).toBeVisible();
  await expect(dialog.getByText("กำลังเล่นเสียงแจ้งเตือน")).toBeVisible();
  await expect.poll(() => countTones(page)).toBe(TONES_PER_CHIME);

  // Nothing on it moves once it has appeared: no pulsing bell, no blinking.
  await page.waitForTimeout(600);
  const running = await dialog.evaluate((node) =>
    node.getAnimations({ subtree: true }).filter((animation) => animation.playState === "running").length,
  );
  expect(running).toBe(0);
  await page.screenshot({ path: "test-results/o20-new-order-modal.png" });

  await dialog.getByRole("button", { name: "ดูว่าลูกค้าสั่งอะไร" }).click();
  await expect(dialog.getByRole("button", { name: "ซ่อนรายการ" })).toBeVisible();
  await expect(dialog.getByText("ไม่ติดมัน")).toBeVisible();
  await expect(dialog.getByText(/^x2$/)).toBeVisible();
  await page.screenshot({ path: "test-results/o21-new-order-items.png" });

  await dialog.getByRole("button", { name: "ไปหน้าออเดอร์" }).click();
  await expect(page).toHaveURL(/\/owner\/orders$/);
  await expect(dialog).toBeHidden();
  const pending: Array<{ id: number; status: string }> = await (
    await page.request.get("/api/orders?status=Pending")
  ).json();
  expect(pending.find((order) => order.id === orderId)?.status).toBe("Pending");
  // Navigating never starts the round, so no second chime either.
  expect(await countTones(page)).toBe(TONES_PER_CHIME);
});

test("ไว้ก่อน dismisses, and a muted popup still appears but makes no sound", async ({ page }) => {
  await login(page.request, OWNER);
  await openLive(page, "/owner/menu");
  await page.getByRole("heading", { name: "เมนูอาหาร" }).click();

  await submitOrder(page.request);
  const dialog = page.getByRole("dialog", { name: `โต๊ะ ${runTableName()}` });
  await expect(dialog).toBeVisible({ timeout: 5_000 });
  await expect.poll(() => countTones(page)).toBe(TONES_PER_CHIME);

  await dialog.getByRole("button", { name: "ปิดเสียง" }).click();
  await expect(dialog.getByText("ปิดเสียงแจ้งเตือนอยู่")).toBeVisible();
  await dialog.getByRole("button", { name: "ไว้ก่อน" }).click();
  await expect(dialog).toBeHidden();
  await expect(page).toHaveURL(/\/owner\/menu$/);

  await submitOrder(page.request);
  await expect(dialog).toBeVisible({ timeout: 5_000 });
  await expect(dialog.getByRole("button", { name: "เปิดเสียง" })).toBeVisible();
  await page.waitForTimeout(500);
  expect(await countTones(page)).toBe(TONES_PER_CHIME);
});

test("before any interaction the browser holds the sound back, and the popup says so", async ({ page }) => {
  await login(page.request, OWNER);
  await openLive(page, "/owner");

  await submitOrder(page.request);
  const dialog = page.getByRole("dialog", { name: `โต๊ะ ${runTableName()}` });
  await expect(dialog).toBeVisible({ timeout: 5_000 });
  await expect(dialog.getByText(/เบราว์เซอร์ยังไม่ให้เล่นเสียง/)).toBeVisible();
  expect(await countTones(page)).toBe(0);

  // The first tap (here on ไว้ก่อน) unlocks sound; the next round chimes.
  await dialog.getByRole("button", { name: "ไว้ก่อน" }).click();
  await submitOrder(page.request);
  await expect(dialog).toBeVisible({ timeout: 5_000 });
  await expect(dialog.getByText("กำลังเล่นเสียงแจ้งเตือน")).toBeVisible();
  await expect.poll(() => countTones(page)).toBe(TONES_PER_CHIME);
});

test("a round the kitchen started or finished leaves the popup alone", async ({ page }) => {
  await login(page.request, OWNER);
  await openLive(page, "/owner");
  // Arm the chime, so the count below measures chimes rather than the browser's autoplay block.
  await page.locator("h1").first().click();

  const orderId = await submitOrder(page.request);
  const dialog = page.getByRole("dialog", { name: `โต๊ะ ${runTableName()}` });
  await expect(dialog).toBeVisible({ timeout: 5_000 });

  // The kitchen advances it to Ready on another device; Ready is terminal, so it is no longer new.
  const advanced = await page.request.post(`/api/orders/${orderId}/status`, { data: { status: "Preparing" } });
  expect(advanced.ok(), await advanced.text()).toBeTruthy();
  await expect(dialog).toBeVisible();
  const readied = await page.request.post(`/api/orders/${orderId}/status`, { data: { status: "Ready" } });
  expect(readied.ok(), await readied.text()).toBeTruthy();
  await expect(dialog).toBeHidden({ timeout: 5_000 });
  // Closing on its own is not a second chime.
  expect(await countTones(page)).toBe(TONES_PER_CHIME);
});

test("a Kitchen account never gets the popup", async ({ page }) => {
  await login(page.request, KITCHEN);
  await openLive(page, "/kitchen");
  await expect(page.getByRole("heading", { name: "จอครัว" })).toBeVisible();

  await submitOrder(page.request);
  // The round reaches the kitchen board live, and no new-order dialog opens alongside it.
  await expect(page.getByText(runTableName()).first()).toBeVisible({ timeout: 5_000 });
  await page.waitForTimeout(1_000);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("มีออเดอร์ใหม่")).toHaveCount(0);

  // Reaching the layout's owner frame is impossible for this account anyway.
  await page.goto("/owner/tables");
  await expect(page).toHaveURL(/\/kitchen$/);
});
