import { type APIRequestContext, type Locator, type Page, expect, test } from "@playwright/test";

// Ticket 17: the owner edits a round from the queue (frame o17) on a stack built from this branch.
// The run seeds its own table, package and menu, submits a round through the customer API, edits it in
// the browser, and cancels/voids everything afterwards — the demo data is left as found. Its dishes live in
// one "E2E 17" category that every run reuses, and they are switched off again at the end, so a customer on a
// demo stack never sees a second หมูสามชั้นสไลซ์.
//
// The dialog's preview is checked against the server's own bill once the same edit is saved, so the numbers
// hold whatever service charge and VAT the shop has (the demo seed has 10% and 7%; a fresh database has none).
// The frame is 1440x900 and the owner's screens are desktop/tablet ones, so this file runs on the `desktop`
// project only; the 390x844 project belongs to the customer screens (tickets 09/13).
test.skip(({ viewport }) => (viewport?.width ?? 0) < 1200, "the owner's screens are desktop/tablet sized");

const OWNER = { email: "owner@demo.local", password: "Demo1234!" };
const KITCHEN = { email: "kitchen@demo.local", password: "Demo1234!" };
const TABLE = "E2E-17";
const ALA_CARTE_PRICE = 45;
const MENU_CATEGORY = "E2E 17";

interface MenuItemRow {
  id: number;
  categoryName: string;
  name: string;
  isActive: boolean;
}

interface Item {
  id: number;
  name: string;
  quantity: number;
}

interface Order {
  id: number;
  status: string;
  cancelReason: string | null;
  editReason: string | null;
  items: Item[];
}

interface SessionDetail {
  id: number;
  qrUrl: string;
  bill: { alaCarteTotal: number; subtotal: number; total: number };
  orders: Order[];
}

async function login(request: APIRequestContext, who: typeof OWNER) {
  expect((await request.post("/api/auth/login", { data: who })).status()).toBe(204);
}

/** The run's own table, activated (creating it the first time). */
async function ownTable(request: APIRequestContext) {
  const tables: Array<{ id: number; name: string; isActive: boolean; seats: number; zone: string | null; sortOrder: number }> =
    await (await request.get("/api/tables")).json();
  const found = tables.find((row) => row.name === TABLE);
  if (found) {
    if (!found.isActive) await request.put(`/api/tables/${found.id}`, { data: { ...found, isActive: true } });
    return found;
  }
  const created = await request.post("/api/tables", { data: { name: TABLE, seats: 4, zone: "E2E", isActive: true } });
  expect(created.ok(), await created.text()).toBeTruthy();
  return created.json();
}

/** A round an interrupted run left open would block the table: cancel its rounds and void it. */
async function closeLeftovers(request: APIRequestContext) {
  const open: Array<{ id: number; tableName: string }> = await (
    await request.get("/api/sessions?status=Open")
  ).json();
  for (const session of open.filter((row) => row.tableName === TABLE)) {
    const detail: SessionDetail = await (await request.get(`/api/sessions/${session.id}`)).json();
    for (const order of detail.orders.filter((row) => row.status !== "Cancelled")) {
      await request.post(`/api/orders/${order.id}/cancel`, { data: { reason: "e2e 17: รอบค้างจากรอบก่อน" } });
    }
    await request.post(`/api/sessions/${session.id}/void`);
  }
}

async function openRound(request: APIRequestContext, tableId: number): Promise<SessionDetail> {
  const packages: Array<{ id: number; isActive: boolean }> = await (await request.get("/api/packages")).json();
  let packageId = packages.find((row) => row.isActive)?.id;
  if (packageId === undefined) {
    const created = await request.post("/api/packages", {
      data: { name: "E2E Package", rank: 1, adultPrice: 399, childPrice: 199, durationMinutes: 90, isActive: true },
    });
    expect(created.ok(), await created.text()).toBeTruthy();
    const pkg: { id: number } = await created.json();
    packageId = pkg.id;
  }
  const opened = await request.post("/api/sessions", {
    data: { tableId, packageId, adultCount: 2, childCount: 0 },
  });
  expect(opened.ok(), await opened.text()).toBeTruthy();
  return opened.json();
}

/**
 * The dishes of frame o17: two buffet dishes (free inside the package) and one à la carte drink (฿45), in the
 * run's own category. A dish an earlier run left behind is switched back on rather than created again.
 */
async function seedMenu(request: APIRequestContext) {
  const categories: Array<{ id: number; name: string }> = await (await request.get("/api/categories")).json();
  const category: { id: number } =
    categories.find((row) => row.name === MENU_CATEGORY) ??
    (await (await request.post("/api/categories", { data: { name: MENU_CATEGORY } })).json());
  const existing: Array<MenuItemRow & { categoryId: number }> = await (await request.get("/api/menu-items")).json();
  const item = async (name: string, kind: "Buffet" | "AlaCarte", price: number) => {
    const data = {
      name,
      categoryId: category.id,
      kind,
      price,
      isAvailable: true,
      isActive: true,
      ...(kind === "Buffet" ? { minPackageRank: 1 } : {}),
    };
    const found = existing.find((row) => row.categoryId === category.id && row.name === name);
    const saved = found
      ? await request.put(`/api/menu-items/${found.id}`, { data })
      : await request.post("/api/menu-items", { data });
    expect(saved.ok(), await saved.text()).toBeTruthy();
    if (found) return found.id;
    const created: { id: number } = await saved.json();
    return created.id;
  };
  return {
    pork: await item("หมูสามชั้นสไลซ์", "Buffet", 0),
    mushrooms: await item("เห็ดรวม", "Buffet", 0),
    tea: await item("ชาไทยเย็น", "AlaCarte", ALA_CARTE_PRICE),
  };
}

/** Switches off every dish of the run's category (including ones earlier runs left on), so customers never see them. */
async function hideMenu(request: APIRequestContext) {
  const items: MenuItemRow[] = await (await request.get("/api/menu-items")).json();
  for (const row of items.filter((item) => item.categoryName === MENU_CATEGORY && item.isActive)) {
    const saved = await request.put(`/api/menu-items/${row.id}`, { data: { ...row, isActive: false } });
    expect(saved.ok(), await saved.text()).toBeTruthy();
  }
}

async function submitRound(
  request: APIRequestContext,
  session: SessionDetail,
  menu: { pork: number; mushrooms: number; tea: number },
) {
  const response = await request.post(`/api/s/${session.qrUrl.split("/s/")[1]}/orders`, {
    data: {
      items: [
        { menuItemId: menu.pork, quantity: 3, note: "ไม่ติดมัน" },
        { menuItemId: menu.mushrooms, quantity: 1, note: null },
        { menuItemId: menu.tea, quantity: 1, note: null },
      ],
    },
  });
  expect(response.status(), await response.text()).toBe(201);
  const board: Array<{ id: number; sessionId: number }> = await (await request.get("/api/orders")).json();
  return Math.max(...board.filter((order) => order.sessionId === session.id).map((order) => order.id));
}

const detailOf = async (request: APIRequestContext, sessionId: number): Promise<SessionDetail> =>
  (await request.get(`/api/sessions/${sessionId}`)).json();

/** The bill block of the dialog (the sunken well): the "before" total (only once something changed), the "after" total and the difference. */
function billBlock(dialog: Locator) {
  const block = dialog.locator("div.bg-sunken").last();
  return {
    before: block.locator("span.line-through"),
    after: block.locator("span.tabular-nums").last(),
    delta: block.locator("span.text-ready-ink"),
  };
}

/** Opens the owner's queue, taps แก้ไข on the round's card and waits for the dialog. */
async function openEditDialog(page: Page, number: number) {
  // The page has its own cookie jar: sign it in as the owner (the API fixture stays on its own context).
  await login(page.request, OWNER);
  await page.goto("/owner/orders");
  const card = page.getByRole("article", { name: `โต๊ะ ${TABLE} รอบที่ ${number}` });
  await card.waitFor({ timeout: 15_000 });
  await card.getByRole("button", { name: `แก้ไขออเดอร์ ${TABLE} รอบที่ ${number}` }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByText(`แก้ไขออเดอร์ · ${TABLE} รอบที่ ${number}`).waitFor();
  return { card, dialog };
}

const baht = (text: string | null) => Number((text ?? "").replace(/[^\d.]/g, ""));

test.describe.configure({ mode: "serial" });

test("reducing a line updates the bill, the card, the customer and the kitchen", async ({ page, request, browser }) => {
  await login(request, OWNER);
  const table = await ownTable(request);
  await closeLeftovers(request);
  const session = await openRound(request, table.id);
  const menu = await seedMenu(request);
  const orderId = await submitRound(request, session, menu);
  const before = await detailOf(request, session.id);
  const token = session.qrUrl.split("/s/")[1];

  // The customer's orders tab and the kitchen board are open before the edit: both must change from
  // the hub alone, without a reload (the screen snapshot of the round is only for the assertion below).
  const customerContext = await browser.newContext();
  const customerPage = await customerContext.newPage();
  await customerPage.goto(`/s/${token}`);
  await customerPage.getByRole("button", { name: "ออเดอร์", exact: true }).click();
  await expect(customerPage.getByText("ชาไทยเย็น")).toBeVisible();

  const kitchenContext = await browser.newContext();
  const kitchenPage = await kitchenContext.newPage();
  await login(kitchenPage.request, KITCHEN);
  await kitchenPage.goto("/kitchen");
  const kitchenCard = kitchenPage.locator("article", { hasText: TABLE }).first();
  await kitchenCard.waitFor({ timeout: 10_000 });
  await expect(kitchenCard.getByText("ชาไทยเย็น")).toBeVisible();

  const { card, dialog } = await openEditDialog(page, 1);
  // Nothing changed yet: the dialog shows the server's total, with no before → after pair.
  expect(baht(await billBlock(dialog).after.textContent())).toBeCloseTo(before.bill.total, 2);
  await expect(billBlock(dialog).before).toHaveCount(0);

  // Frame o17's state: หมูสามชั้นสไลซ์ ×3 → ×2 and ชาไทยเย็น taken out (เอาคืน puts it back).
  await dialog.locator("li", { hasText: "หมูสามชั้นสไลซ์" }).getByRole("button", { name: "ลด หมูสามชั้นสไลซ์" }).click();
  await dialog.locator("li", { hasText: "ชาไทยเย็น" }).getByRole("button", { name: "นำ ชาไทยเย็น ออก" }).click();
  await expect(dialog.getByRole("button", { name: "เอาคืน", exact: true })).toBeVisible();
  expect(baht(await billBlock(dialog).before.textContent())).toBeCloseTo(before.bill.total, 2);
  // The preview is the bill this edit will leave (checked against the server once it is saved, below).
  const previewTotal = baht(await billBlock(dialog).after.textContent());
  expect(previewTotal).toBeLessThan(before.bill.total);
  const previewDelta = (before.bill.total - previewTotal).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  await expect(billBlock(dialog).delta).toHaveText(`-฿${previewDelta}`);
  // The design frame is 1440x900@1, so the comparison screenshot is taken at exactly that size.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: "test-results/o17-order-edit.png" });

  // Back out of the removal the way the frame offers it, then take it out again for real.
  await dialog.getByRole("button", { name: "เอาคืน", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "เอาคืน", exact: true })).toHaveCount(0);

  // No reason, no save: the dialog blocks it and says why (the server refuses it too — seam 1).
  await expect(dialog.getByText("ต้องระบุเหตุผลในการแก้ไข")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "บันทึกการแก้ไข" })).toBeDisabled();

  await dialog.getByRole("button", { name: "ลูกค้าเอาออก" }).click();
  await dialog.getByRole("button", { name: "นำ ชาไทยเย็น ออก" }).click();
  await dialog.getByRole("button", { name: "บันทึกการแก้ไข" }).click();
  await expect(dialog).toHaveCount(0, { timeout: 10_000 });

  const after = await detailOf(request, session.id);
  expect(after.bill.alaCarteTotal).toBe(0);
  expect(after.bill.subtotal).toBe(before.bill.subtotal - ALA_CARTE_PRICE);
  expect(after.bill.total).toBeCloseTo(previewTotal, 2);
  const edited = after.orders.find((order) => order.id === orderId)!;
  expect(edited.status).toBe("Pending");
  expect(edited.editReason).toBe("ลูกค้าเอาออก");
  expect(edited.items.find((item) => item.name === "หมูสามชั้นสไลซ์")?.quantity).toBe(2);
  expect(edited.items.some((item) => item.name === "ชาไทยเย็น")).toBe(false);

  // The owner's card, the customer's list and the kitchen board all changed without a reload.
  await expect(card.getByText("แก้ไขแล้ว · ลูกค้าเอาออก")).toBeVisible();
  await expect(customerPage.getByText("ชาไทยเย็น")).toHaveCount(0);
  await expect(customerPage.locator("li", { hasText: "หมูสามชั้นสไลซ์" }).getByText("×2")).toBeVisible();
  await expect(kitchenCard.getByText("แก้ไขแล้ว · ลูกค้าเอาออก")).toBeVisible();
  await expect(kitchenCard.getByText("ชาไทยเย็น")).toHaveCount(0);
  await expect(kitchenCard.locator("li", { hasText: "หมูสามชั้นสไลซ์" }).getByText("×2")).toBeVisible();

  const remaining: { items: Item[] } = await (await request.get(`/api/s/${token}/orders`)).json();
  expect(remaining.items.map((item) => [item.name, item.quantity])).toEqual([
    ["หมูสามชั้นสไลซ์", 2],
    ["เห็ดรวม", 1],
  ]);

  // ยกเลิกทั้งออเดอร์ sends the same write with every line removed: the whole round is cancelled.
  await login(request, OWNER);
  const { dialog: cancelling } = await openEditDialog(page, 1);
  await cancelling.getByRole("button", { name: "ลูกค้าเอาออก" }).click();
  await cancelling.getByRole("button", { name: "ยกเลิกทั้งออเดอร์" }).click();
  await expect(cancelling).toHaveCount(0, { timeout: 10_000 });

  const closed = await detailOf(request, session.id);
  const cancelled = closed.orders.find((order) => order.id === orderId)!;
  expect(cancelled.status).toBe("Cancelled");
  expect(cancelled.cancelReason).toBe("ลูกค้าเอาออก");
  expect(closed.bill.alaCarteTotal).toBe(0);

  // Cleanup: the run's own round, table and dishes go back to how they were found.
  expect((await request.post(`/api/sessions/${session.id}/void`)).ok()).toBeTruthy();
  expect((await request.put(`/api/tables/${table.id}`, { data: { ...table, isActive: false } })).ok()).toBeTruthy();
  await hideMenu(request);
  await customerContext.close();
  await kitchenContext.close();
});

test("a Ready round offers no แก้ไข and the API answers ORDER_NOT_EDITABLE", async ({ page, request }) => {
  await login(request, OWNER);
  const table = await ownTable(request);
  await closeLeftovers(request);
  const session = await openRound(request, table.id);
  const menu = await seedMenu(request);
  const orderId = await submitRound(request, session, menu);

  await login(request, KITCHEN);
  await request.post(`/api/orders/${orderId}/status`, { data: { status: "Preparing" } });

  // The Preparing round offers แก้ไข: openEditDialog just tapped it (the open dialog hides the card
  // from the accessibility tree, so the visible check happens after the reload below).
  const { card } = await openEditDialog(page, 1);

  await request.post(`/api/orders/${orderId}/status`, { data: { status: "Ready" } });
  await page.reload();
  await card.waitFor({ timeout: 10_000 });
  await expect(card.getByText("รอพนักงานนำไปเสิร์ฟ")).toBeVisible();
  await expect(card.getByRole("button", { name: `แก้ไขออเดอร์ ${TABLE} รอบที่ 1` })).toHaveCount(0);

  // The server refuses the same edit the hidden button would have sent (as the owner, not the kitchen).
  await login(request, OWNER);
  const line = (await detailOf(request, session.id)).orders.find((order) => order.id === orderId)!.items[0];
  const refused = await request.post(`/api/orders/${orderId}/edit`, {
    data: { reason: "ลูกค้าเอาออก", items: [{ itemId: line.id, quantity: 0 }] },
  });
  expect(refused.status()).toBe(409);
  expect((await refused.json()).code).toBe("ORDER_NOT_EDITABLE");

  expect((await request.post(`/api/orders/${orderId}/cancel`, { data: { reason: "e2e 17: เก็บรอบทดสอบ" } })).ok()).toBeTruthy();
  expect((await request.post(`/api/sessions/${session.id}/void`)).ok()).toBeTruthy();
  expect((await request.put(`/api/tables/${table.id}`, { data: { ...table, isActive: false } })).ok()).toBeTruthy();
  await hideMenu(request);
});
