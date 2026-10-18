import { readFileSync } from "node:fs";
import { join } from "node:path";
import {
  type APIRequestContext,
  type Browser,
  type BrowserContext,
  type Locator,
  type Page,
  type WebSocket,
  expect,
  test,
} from "@playwright/test";

// Ticket 27: the spec's one end-to-end flow (seam 2) plus the UI quality gate, on a running stack with the demo on.
//
// Everything interesting is driven through the product's own UI, through the one origin the stack serves
// (Caddy in front of the API and the web app, so the auth cookie and the SignalR socket are same-origin):
//
//   demo-login as Owner → open a free table → a fresh browser context "scans" the QR the dialog renders (its
//   pixels are decoded, as a phone camera would) → orders หมูสามชั้นสไลซ์ ×2 and ชาไทยเย็น ×1 from the menu →
//   the round lands in the kitchen board's รอรับ within 5 s without a reload → เริ่มทำ → พร้อมเสิร์ฟ →
//   the customer's ออเดอร์ tab lists the items with no status → the Owner takes cash →
//   the customer's screen turns into ขอบคุณที่ใช้บริการ by itself.
//
// Alongside the flow, on the same real screens:
// - the customer screens at 390×844 have no horizontal scroll (measured: `scrollWidth <= innerWidth`, and a
//   sideways scroll attempt leaves `scrollX` at 0);
// - the four status colours (Pending / Preparing / Ready / Cancelled, badge form) are the page's own token pairs
//   with a WCAG 2.x contrast ratio of at least 4.5, and the badges the board paints really use those pairs;
// - the customer's ordering controls are at least 44×44 px;
// - a fresh card's alert pulse runs normally and stops under `prefers-reduced-motion` (no running animation,
//   the halo stays as a static ring).
//
// The run takes a free table of the real floor and leaves it free again: the flow ends by paying the bill, and
// a run that fails halfway closes the round it opened in `afterAll` (cancel what is still open, then pay what is
// Ready, else void). Bot rounds are never touched.
//
// The flow uses two browser contexts of its own (a desktop Owner and a 390×844 phone), so it runs once, on the
// `desktop` project. It needs `GET /api/demo/status` to answer `enabled: true` (the demo login is its entry).

test.skip(({ viewport }) => (viewport?.width ?? 0) < 1280, "the flow drives its own phone and desktop contexts");

declare global {
  interface Window {
    /** `jsQR`'s browser build, injected per page — the decoder this file trusts the pixels to. */
    jsQR: (data: Uint8ClampedArray, width: number, height: number) => { data: string } | null;
    /** Set once a screen is ready; still being there later proves the document was never reloaded. */
    __fq27?: string;
  }
}

/** The spec's dishes, by the seeded menu's names and the category each one is listed under. */
const MENU_ITEMS = [
  { name: "หมูสามชั้นสไลซ์", category: "หมู", quantity: 2 },
  { name: "ชาไทยเย็น", category: "เครื่องดื่ม", quantity: 1 },
] as const;

/** Every label a round's status can carry anywhere in the product; the customer must see none of them. */
const STATUS_WORDS = ["รอรับ", "กำลังทำ", "พร้อมเสิร์ฟ", "เสิร์ฟแล้ว", "รอเสิร์ฟ", "ยกเลิก"];

const PHONE = { width: 390, height: 844 } as const;
const MIN_TARGET_PX = 44;
const WCAG_AA_TEXT = 4.5;
const ARRIVAL_BUDGET_MS = 5_000;

/** `jsQR`'s browser build, injected into the Owner's page so the QR is read from its rendered pixels. */
const JSQR_SOURCE = readFileSync(join(__dirname, "../node_modules/jsqr/dist/jsQR.js"), "utf8");

const STATUS_KEYS = ["pending", "preparing", "ready", "cancel"] as const;
type StatusKey = (typeof STATUS_KEYS)[number];
interface ColourPair {
  ink: string;
  soft: string;
}

let demoOn = false;
/** The round this run opened and has not paid yet; `afterAll` closes it if the flow stopped halfway. */
let openedSessionId: number | null = null;

test.beforeAll(async ({ playwright, baseURL }) => {
  const request = await playwright.request.newContext({ baseURL });
  const status = await request.get("/api/demo/status");
  const body: { enabled?: unknown } = status.ok() ? await status.json() : {};
  demoOn = body.enabled === true;
  await request.dispose();
});

test.afterAll(async ({ playwright, baseURL }) => {
  if (openedSessionId === null) return;
  const request = await playwright.request.newContext({ baseURL });
  await closeLeftoverRound(request, openedSessionId);
  await request.dispose();
});

/**
 * Only for a run that failed halfway: the round this run opened is closed the way the shop would close it, so the
 * table is free for the next run. A round that is no longer open is left alone.
 */
async function closeLeftoverRound(request: APIRequestContext, sessionId: number) {
  const login = await request.post("/api/demo/login", { data: { role: "Owner" } });
  expect(login.status()).toBe(204);
  type Detail = { status: string; orders: Array<{ id: number; status: string }>; bill: { total: number } };
  let detail: Detail = await (await request.get(`/api/sessions/${sessionId}`)).json();
  if (detail.status !== "Open") return;
  for (const order of detail.orders.filter((row) => row.status === "Pending" || row.status === "Preparing")) {
    await request.post(`/api/orders/${order.id}/cancel`, { data: { reason: "e2e ticket 27: รอบค้างจากการรันที่ล้ม" } });
  }
  detail = await (await request.get(`/api/sessions/${sessionId}`)).json();
  if (detail.orders.some((row) => row.status === "Ready")) {
    await request.post(`/api/sessions/${sessionId}/payment`, {
      data: { method: "Cash", amountReceived: detail.bill.total },
    });
  } else {
    await request.post(`/api/sessions/${sessionId}/void`);
  }
}

/**
 * Resolves once the next `/hubs/live` socket the page opens has answered the SignalR handshake, so a round sent
 * afterwards is really heard (otherwise the test would race the connection, not the product).
 */
function nextHubHandshake(page: Page): Promise<void> {
  return new Promise<void>((resolve) => {
    const onSocket = (socket: WebSocket) => {
      if (!socket.url().includes("/hubs/live")) return;
      page.off("websocket", onSocket);
      socket.once("framereceived", () => resolve());
    };
    page.on("websocket", onSocket);
  });
}

/** The QR the dialog renders, rasterized by the browser and decoded with jsQR — the URL a phone would open. */
async function scanQr(dialog: Locator): Promise<string> {
  const svg = dialog.locator('svg[width="222"]');
  await expect(svg).toHaveCount(1);
  const decoded = await svg.evaluate(async (node) => {
    const source = new XMLSerializer().serializeToString(node);
    const image = new Image();
    await new Promise<void>((resolve, reject) => {
      image.onload = () => resolve();
      image.onerror = () => reject(new Error("the rendered QR could not be rasterized"));
      image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`;
    });
    const size = 512;
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext("2d")!;
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, size, size);
    context.imageSmoothingEnabled = false;
    context.drawImage(image, 0, 0, size, size);
    const pixels = context.getImageData(0, 0, size, size);
    return window.jsQR(pixels.data, pixels.width, pixels.height)?.data ?? null;
  });
  expect(decoded, "the QR the dialog renders decoded to nothing").not.toBeNull();
  return decoded!;
}

/**
 * Opens the first free table the overview offers. If another client takes that table between the grid and the
 * submit (the product answers with its own message), the dialog is dismissed and the next free table is tried.
 */
async function openFreeTable(page: Page): Promise<{ tableName: string; qrDialog: Locator }> {
  const freeCards = page
    .getByRole("listitem")
    .filter({ has: page.getByRole("button", { name: "เปิดโต๊ะ", exact: true }) });
  await expect(freeCards.first(), "the floor has no free table to open").toBeVisible();

  for (let attempt = 0; attempt < 3; attempt += 1) {
    const card = freeCards.first();
    const tableName = (await card.locator("b").first().innerText()).trim();
    await card.getByRole("button", { name: "เปิดโต๊ะ", exact: true }).click();

    const openDialog = page.getByRole("dialog", { name: `เปิดโต๊ะ ${tableName}`, exact: true });
    await expect(openDialog).toBeVisible();
    // A package is preselected (the first active one) and the guest counts default to the frame's 2 + 1.
    await expect(openDialog.getByRole("button", { pressed: true })).toHaveCount(1);
    await page.screenshot({ path: "test-results/27-o02-open.png", animations: "disabled" });
    await openDialog.getByRole("button", { name: "เปิดโต๊ะและสร้าง QR" }).click();

    const qrDialog = page.getByRole("dialog", { name: `โต๊ะ ${tableName} เปิดแล้ว`, exact: true });
    const refused = openDialog.getByRole("alert");
    await expect(qrDialog.or(refused)).toBeVisible();
    if (await qrDialog.isVisible()) return { tableName, qrDialog };

    console.log(`table ${tableName} was taken meanwhile: ${await refused.innerText()}`);
    await openDialog.getByRole("button", { name: "ยกเลิก", exact: true }).click();
  }
  throw new Error("three free tables in a row were taken before they could be opened");
}

async function phoneContext(browser: Browser, baseURL: string | undefined): Promise<BrowserContext> {
  // A fresh context: no cookie, no storage — a guest's phone. 2× matches the `design/out` customer frames.
  return browser.newContext({ baseURL, viewport: PHONE, isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
}

/** The page-level horizontal fit of a 390×844 screen, measured, plus a sideways scroll attempt. */
async function expectNoHorizontalScroll(page: Page, screen: string) {
  const fit = await page.evaluate(() => {
    window.scrollTo(200, window.scrollY);
    const measured = {
      innerWidth: window.innerWidth,
      innerHeight: window.innerHeight,
      documentScrollWidth: document.documentElement.scrollWidth,
      bodyScrollWidth: document.body.scrollWidth,
      scrollX: window.scrollX,
    };
    window.scrollTo(0, window.scrollY);
    return measured;
  });
  console.log(`viewport ${screen}: ${JSON.stringify(fit)}`);
  expect(fit.innerWidth, `${screen}: innerWidth`).toBe(PHONE.width);
  expect(fit.innerHeight, `${screen}: innerHeight`).toBe(PHONE.height);
  expect(fit.documentScrollWidth, `${screen}: documentElement.scrollWidth`).toBeLessThanOrEqual(fit.innerWidth);
  expect(fit.bodyScrollWidth, `${screen}: body.scrollWidth`).toBeLessThanOrEqual(fit.innerWidth);
  expect(fit.scrollX, `${screen}: a sideways scroll moved the page`).toBe(0);
}

/** A control's rendered box; both sides must reach the comfortable-touch minimum. */
async function expectTouchTarget(target: Locator, what: string) {
  await expect(target).toBeVisible();
  const box = await target.boundingBox();
  expect(box, `${what} has no box`).not.toBeNull();
  console.log(`touch target ${what}: ${box!.width.toFixed(1)}×${box!.height.toFixed(1)}px`);
  expect(box!.width, `${what} width`).toBeGreaterThanOrEqual(MIN_TARGET_PX);
  expect(box!.height, `${what} height`).toBeGreaterThanOrEqual(MIN_TARGET_PX);
}

type Rgb = readonly [number, number, number];

/** A computed `rgb()`/`rgba()` colour; a translucent one has no fixed ratio and is refused. */
function parseRgb(css: string): Rgb {
  const match = /^rgba?\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)(?:,\s*([\d.]+))?\)$/.exec(css.trim());
  if (!match) throw new Error(`not an sRGB colour: ${css}`);
  if (match[4] !== undefined && Number(match[4]) !== 1) throw new Error(`a translucent colour has no fixed ratio: ${css}`);
  return [Number(match[1]), Number(match[2]), Number(match[3])];
}

/** WCAG 2.x relative luminance of an sRGB colour. */
function luminance(rgb: Rgb): number {
  const [r, g, b] = rgb.map((channel) => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG 2.x contrast ratio, (L1 + 0.05) / (L2 + 0.05) with L1 the lighter. */
function contrastRatio(a: Rgb, b: Rgb): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

/**
 * The page's own status tokens, resolved by the browser: `--color-{status}-ink` on `--color-{status}-soft` is
 * exactly the pair the status badge draws (`components/ui/badge.tsx`).
 */
async function statusTokens(page: Page): Promise<Record<StatusKey, ColourPair>> {
  return page.evaluate((keys) => {
    const probe = document.createElement("span");
    document.body.append(probe);
    const pairs: Record<string, ColourPair> = {};
    for (const key of keys) {
      probe.style.color = `var(--color-${key}-ink)`;
      probe.style.backgroundColor = `var(--color-${key}-soft)`;
      const style = getComputedStyle(probe);
      pairs[key] = { ink: style.color, soft: style.backgroundColor };
    }
    probe.remove();
    return pairs as Record<StatusKey, ColourPair>;
  }, STATUS_KEYS);
}

/** The status badge a kitchen card is showing, as the browser painted it. */
async function paintedBadge(card: Locator, label: string): Promise<ColourPair> {
  const badge = card.locator('[data-slot="badge"]', { hasText: label });
  await expect(badge).toBeVisible();
  return badge.evaluate((node) => {
    const style = getComputedStyle(node);
    return { ink: style.color, soft: style.backgroundColor };
  });
}

test("owner opens a table, a phone orders from its QR, the kitchen serves it live, the owner takes cash", async ({
  page,
  browser,
  baseURL,
}) => {
  test.skip(!demoOn, "the flow enters through the demo login; run it against a stack with Demo:Enabled=true");
  test.setTimeout(120_000);

  // The owner's new-order popup (ticket 15) opens on every owner screen whenever any round arrives — the demo
  // bot orders every 20–40 s — and it is modal, so it is acknowledged with its own "ไว้ก่อน" whenever it covers
  // the page.
  await page.addLocatorHandler(page.getByRole("dialog").filter({ hasText: "มีออเดอร์ใหม่" }), async (popup) => {
    await popup.getByRole("button", { name: "ไว้ก่อน", exact: true }).click();
  });
  await page.addInitScript(JSQR_SOURCE);

  // ── Owner: demo login (frame l1), no password typed ────────────────────────────────────────────────────────
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/login");
  await page.getByRole("button", { name: /เข้าใช้แบบเจ้าของร้าน/ }).click();
  await expect(page).toHaveURL(/\/owner$/);
  await expect(page.getByRole("heading", { name: "ภาพรวม", level: 1 })).toBeVisible();
  await page.screenshot({ path: "test-results/27-o01-overview.png" });

  // ── Owner: open a free table (frames o02 → o03) and read the QR the way a phone does ──────────────────────
  const { tableName, qrDialog } = await openFreeTable(page);
  const printHref = (await qrDialog.getByRole("link", { name: "พิมพ์ QR slip" }).getAttribute("href")) ?? "";
  openedSessionId = Number(/\/print\/qr\/(\d+)$/.exec(printHref)?.[1]);
  expect(openedSessionId).toBeGreaterThan(0);
  await expect(qrDialog.getByText("สร้าง QR สำหรับรอบการนั่งนี้แล้ว · ใช้ได้จนกว่าจะปิดบิล")).toBeVisible();
  const qrUrl = await scanQr(qrDialog);
  console.log(`table ${tableName} opened (session ${openedSessionId}); its QR decodes to ${qrUrl}`);
  expect(qrUrl).toMatch(/\/s\/[^/?#]+$/);
  // The QR the shop hands out must address the origin the stack serves on (`App__PublicBaseUrl`). Comparing it
  // against the run's own baseURL makes a mis-set PUBLIC_BASE_URL a named failure instead of a mystery 404; the
  // phone then opens the pathname, so the flow stays on this run's origin either way.
  expect(new URL(qrUrl).origin, "the QR points at a different origin than this run's baseURL").toBe(new URL(baseURL!).origin);
  await page.screenshot({ path: "test-results/27-o03-qr.png", animations: "disabled" });
  await qrDialog.getByRole("button", { name: "เสร็จสิ้น", exact: true }).click();
  await expect(qrDialog).toBeHidden();

  // ── Owner: the kitchen board (frame k1), reached from the sidebar, live before anything is sent ────────────
  await page.setViewportSize({ width: 1280, height: 800 });
  const kitchenLive = nextHubHandshake(page);
  await page.getByRole("link", { name: "จอครัว" }).click();
  await expect(page).toHaveURL(/\/kitchen$/);
  await kitchenLive;
  const pendingColumn = page.getByRole("region", { name: "รอรับ", exact: true });
  await expect(pendingColumn.getByRole("heading", { name: "รอรับ" })).toBeVisible();
  await page.evaluate(() => {
    window.__fq27 = "kitchen";
  });

  // A second kitchen screen on the same Owner session, with `prefers-reduced-motion: reduce`.
  const calm = await browser.newContext({
    baseURL,
    viewport: { width: 1280, height: 800 },
    reducedMotion: "reduce",
    storageState: await page.context().storageState(),
  });
  const calmPage = await calm.newPage();
  const calmLive = nextHubHandshake(calmPage);
  await calmPage.goto("/kitchen");
  await calmLive;
  expect(await calmPage.evaluate(() => matchMedia("(prefers-reduced-motion: reduce)").matches)).toBe(true);

  // ── Customer: a fresh context opens the QR URL on a 390×844 phone (frame c1) ───────────────────────────────
  const phone = await phoneContext(browser, baseURL);
  const customer = await phone.newPage();
  const customerLive = nextHubHandshake(customer);
  await customer.goto(new URL(qrUrl).pathname);
  await expect(customer.getByText(`โต๊ะ ${tableName}`, { exact: true })).toBeVisible();
  await expect(customer.getByText("ชาบูบุฟเฟ่ต์ · สั่งอาหารผ่าน QR", { exact: true })).toBeVisible();
  await customerLive;
  await customer.evaluate(() => {
    window.__fq27 = "customer";
  });
  await customer.screenshot({ path: "test-results/27-c1-menu.png" });
  await expectNoHorizontalScroll(customer, "menu (c1)");

  // Each dish is taken from the category the shop lists it under, so a same-named dish another run added
  // elsewhere on the menu cannot stand in for it.
  const menuRow = (item: (typeof MENU_ITEMS)[number], action: "เพิ่ม" | "ลด") =>
    customer
      .locator("section")
      .filter({ has: customer.getByRole("heading", { name: item.category, exact: true }) })
      .getByRole("button", { name: `${action} ${item.name}`, exact: true });
  const [firstItem] = MENU_ITEMS;
  await expectTouchTarget(menuRow(firstItem, "เพิ่ม"), `menu row add "${firstItem.name}"`);
  await expectTouchTarget(customer.getByRole("button", { name: "เมนู", exact: true }), 'tab "เมนู"');
  await expectTouchTarget(customer.getByRole("button", { name: "ออเดอร์", exact: true }), 'tab "ออเดอร์"');

  for (const item of MENU_ITEMS) {
    for (let tap = 0; tap < item.quantity; tap += 1) await menuRow(item, "เพิ่ม").tap();
  }
  await expectTouchTarget(menuRow(firstItem, "ลด"), `menu row stepper − "${firstItem.name}"`);
  await expectTouchTarget(menuRow(firstItem, "เพิ่ม"), `menu row stepper + "${firstItem.name}"`);

  // ── Customer: the cart (frame c2) ──────────────────────────────────────────────────────────────────────────
  const cartBar = customer.getByRole("button", { name: "เปิดตะกร้า · 2 เมนู · 3 จาก 20 รายการ" });
  await expectTouchTarget(cartBar, "cart bar");
  await cartBar.tap();
  const sheet = customer.getByRole("dialog", { name: "ตะกร้า" });
  await expect(sheet.getByText("ยังไม่ได้ส่ง", { exact: true })).toBeVisible();
  await expect(sheet.getByText("3 / 20 รายการ", { exact: true })).toBeVisible();
  for (const item of MENU_ITEMS) await expect(sheet.getByText(item.name, { exact: true })).toBeVisible();
  await expect(sheet.getByText("฿45 คิดเงินแยก", { exact: true })).toBeVisible();
  const send = sheet.getByRole("button", { name: "ส่งออเดอร์" });
  await expectTouchTarget(send, 'cart "ส่งออเดอร์"');
  await expectTouchTarget(sheet.getByRole("button", { name: `เพิ่ม ${firstItem.name}`, exact: true }), `cart stepper + "${firstItem.name}"`);
  await expectTouchTarget(sheet.getByRole("button", { name: `ลด ${firstItem.name}`, exact: true }), `cart stepper − "${firstItem.name}"`);
  await customer.screenshot({ path: "test-results/27-c2-cart.png", animations: "disabled" });
  await expectNoHorizontalScroll(customer, "cart sheet (c2)");

  // ── Kitchen: the round appears in รอรับ within 5 s, through the hub, with no reload ─────────────────────────
  const cardName = `โต๊ะ ${tableName} รอบที่ 1`;
  const card = page.getByRole("article", { name: cardName, exact: true });
  const calmCard = calmPage.getByRole("article", { name: cardName, exact: true });
  const sentAt = Date.now();
  await send.tap();
  await expect(pendingColumn.getByRole("article", { name: cardName, exact: true })).toBeVisible({
    timeout: ARRIVAL_BUDGET_MS,
  });
  const arrivedMs = Date.now() - sentAt;
  console.log(`kitchen card "${cardName}" in รอรับ ${arrivedMs} ms after the tap on ส่งออเดอร์`);
  expect(arrivedMs).toBeLessThanOrEqual(ARRIVAL_BUDGET_MS);
  await expect(customer.getByText("ส่งออเดอร์แล้ว", { exact: true })).toBeVisible();
  expect(await page.evaluate(() => window.__fq27), "the kitchen board reloaded").toBe("kitchen");
  // ใหม่ is set only for a round that arrived over the hub; the board's own fetch highlights nothing.
  await expect(card.getByText("ใหม่", { exact: true })).toBeVisible();
  for (const item of MENU_ITEMS) {
    await expect(card.getByRole("listitem").filter({ hasText: item.name })).toContainText(`×${item.quantity}`);
  }

  // Reduced motion: the alert pulse runs on the normal board, and nothing at all runs on the calm one.
  const pulsing = await card.evaluate((node) =>
    node
      .getAnimations()
      .filter((animation) => animation.playState === "running")
      .map((animation) => (animation instanceof CSSAnimation ? animation.animationName : animation.constructor.name)),
  );
  console.log(`running animations on the new card (no motion preference): ${JSON.stringify(pulsing)}`);
  expect(pulsing).toContain("alert-pulse");
  await expect(calmCard.getByText("ใหม่", { exact: true })).toBeVisible({ timeout: ARRIVAL_BUDGET_MS });
  const calmRunning = await calmPage.evaluate(() =>
    document
      .getAnimations()
      .filter((animation) => animation.playState === "running")
      .map((animation) => (animation instanceof CSSAnimation ? animation.animationName : animation.constructor.name)),
  );
  console.log(`running animations on the reduced-motion board: ${JSON.stringify(calmRunning)}`);
  expect(calmRunning).toEqual([]);
  const calmHalo = await calmCard.evaluate((node) => getComputedStyle(node).boxShadow);
  console.log(`reduced-motion card halo: ${calmHalo}`);
  expect(calmHalo, "the reduced-motion card lost its static halo").toContain("7px");
  await calm.close();
  await card.scrollIntoViewIfNeeded();
  await page.screenshot({ path: "test-results/27-k1-board.png", animations: "disabled" });

  // ── Kitchen: เริ่มทำ → กำลังทำ → พร้อมเสิร์ฟ, reading each badge as it is painted ────────────────────────────
  const tokens = await statusTokens(page);
  const painted: Partial<Record<StatusKey, ColourPair>> = {};
  painted.pending = await paintedBadge(card, "รอรับ");
  await card.getByRole("button", { name: "เริ่มทำ", exact: true }).click();
  const preparingCard = page
    .getByRole("region", { name: "กำลังทำ", exact: true })
    .getByRole("article", { name: cardName, exact: true });
  await expect(preparingCard).toBeVisible();
  painted.preparing = await paintedBadge(preparingCard, "กำลังทำ");
  await preparingCard.getByRole("button", { name: "พร้อมเสิร์ฟ", exact: true }).click();
  const readyCard = page
    .getByRole("region", { name: "พร้อมเสิร์ฟ", exact: true })
    .getByRole("article", { name: cardName, exact: true });
  await expect(readyCard).toBeVisible();
  await expect(readyCard.getByText("รอพนักงานนำไปเสิร์ฟ")).toBeVisible();
  painted.ready = await paintedBadge(readyCard, "พร้อมเสิร์ฟ");
  expect(await page.evaluate(() => window.__fq27), "the kitchen board reloaded").toBe("kitchen");

  for (const key of STATUS_KEYS) {
    const ink = parseRgb(tokens[key].ink);
    const soft = parseRgb(tokens[key].soft);
    const ratio = contrastRatio(ink, soft);
    console.log(`contrast ${key}: ${tokens[key].ink} on ${tokens[key].soft} = ${ratio.toFixed(2)}:1`);
    expect(ratio, `${key} badge ink on its soft fill`).toBeGreaterThanOrEqual(WCAG_AA_TEXT);
  }
  for (const key of ["pending", "preparing", "ready"] as const) {
    expect(painted[key], `the ${key} badge on the board is not the ${key} token pair`).toEqual(tokens[key]);
  }

  // ── Customer: the ออเดอร์ tab lists what was ordered, with no status anywhere (frame c3) ───────────────────
  await customer.getByRole("button", { name: "ออเดอร์", exact: true }).tap();
  await expect(customer.getByRole("heading", { name: "ออเดอร์ของโต๊ะ" })).toBeVisible();
  await expect(customer.getByText("รวม 3 รายการ", { exact: true })).toBeVisible();
  for (const item of MENU_ITEMS) {
    await expect(customer.getByRole("listitem").filter({ hasText: item.name })).toContainText(`×${item.quantity}`);
  }
  const customerText = await customer.locator("body").innerText();
  for (const word of STATUS_WORDS) expect(customerText, `the customer sees the status "${word}"`).not.toContain(word);
  // The frame has no toast over the banner; the sent toast leaves by itself.
  await expect(customer.getByText("ส่งออเดอร์แล้ว", { exact: true })).toBeHidden({ timeout: 10_000 });
  await customer.screenshot({ path: "test-results/27-c3-orders.png" });
  await expectNoHorizontalScroll(customer, "orders tab (c3)");

  // ── Owner: back to the overview, into the table's round, take cash (frames o04 → o06 → o07) ───────────────
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.getByRole("link", { name: "ภาพรวม" }).click();
  await expect(page).toHaveURL(/\/owner$/);
  await page.getByRole("link", { name: tableName, exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/owner/sessions/${openedSessionId}$`));
  await expect(page.getByRole("heading", { name: `โต๊ะ ${tableName}`, level: 1 })).toBeVisible();
  await expect(page.getByText("ออเดอร์เสิร์ฟครบแล้ว · ปิดบิลได้")).toBeVisible();
  await page.screenshot({ path: "test-results/27-o04-session.png" });
  await page.getByRole("button", { name: "รับชำระเงิน", exact: true }).click();

  const payDialog = page.getByRole("dialog", { name: `รับชำระเงิน · โต๊ะ ${tableName}` });
  await expect(payDialog.getByRole("button", { name: "เงินสด", pressed: true })).toBeVisible();
  await payDialog.getByRole("button", { name: /^พอดี / }).click();
  const confirmCash = payDialog.getByRole("button", { name: "ยืนยันรับเงินสด" });
  await expect(confirmCash).toBeEnabled();
  await page.screenshot({ path: "test-results/27-o06-pay-cash.png", animations: "disabled" });
  await confirmCash.click();
  const paidDialog = page.getByRole("dialog", { name: "ชำระเงินสำเร็จ" });
  await expect(paidDialog).toBeVisible();
  await expect(paidDialog.getByText("เงินสด", { exact: true })).toBeVisible();
  openedSessionId = null;
  await page.screenshot({ path: "test-results/27-o07-paid.png", animations: "disabled" });

  // ── Customer: the phone turns into the thanks screen by itself (frame c6) ──────────────────────────────────
  await expect(customer.getByRole("heading", { name: "ขอบคุณที่ใช้บริการ" })).toBeVisible({ timeout: 10_000 });
  await expect(customer.getByText("ชำระเงินเรียบร้อยแล้ว", { exact: true })).toBeVisible();
  await expect(customer.getByText("QR นี้ปิดใช้งานแล้ว", { exact: true })).toBeVisible();
  expect(await customer.evaluate(() => window.__fq27), "the customer screen reloaded").toBe("customer");
  expect(await customer.locator("body").innerText(), "the thanks screen shows money").not.toMatch(/฿|บาท/);
  await customer.screenshot({ path: "test-results/27-c6-end.png" });
  await expectNoHorizontalScroll(customer, "thanks (c6)");
  await phone.close();
});
