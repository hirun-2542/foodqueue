import { type APIRequestContext, type FrameLocator, type Page, expect, test } from "@playwright/test";

// Ticket 26: the split screen (frame d1-demo).
//
// The page is one document holding three real ones: the customer's phone, the kitchen board and the owner's
// overview, each an iframe of the real route at its designed viewport. So this file drives the panes the way a
// viewer does — it taps "+" on a dish inside the customer pane and sends the round from that pane's cart —
// and then asserts the other two panes changed **without a reload**: every document of the page counts its own
// `DOMContentLoaded` into the top window (all panes are same-origin), and the count is compared before and
// after. A reload anywhere — the page, or any pane — moves it.
//
// The demo page signs in as the demo Owner itself (`POST /api/demo/login`) and takes a customer round from
// `POST /api/demo/customer-session`; both are the ticket-24 endpoints, reused rather than re-implemented. The
// round the viewer gets is a bot round (ticket 24's seeder and endpoints open rounds with `isDemoBot`), which
// is also why the bot panel reports the viewer's own order.
//
// Demo Mode is one server switch, so this file asks the stack it runs against which side of the switch it is on
// and runs only the half that applies: the flow and the frame comparison on a stack started with the demo on,
// and the "not offered, not functional" cases on a stack started with `DEMO_ENABLED=false`. Run it against
// both to exercise both halves — there is no second source of truth to keep in sync.
//
// The frame is a 1440x1060 desktop board, so this file runs on the `desktop` project only.

test.skip(({ viewport }) => (viewport?.width ?? 0) < 1280, "the split screen is a desktop board");

type PaneName = "customer" | "kitchen" | "owner";

interface BoardOrder {
  id: number;
  number: number;
  tableName: string;
  sessionId: number;
  status: string;
}

/** How many documents this page has loaded; the panes report into the top window from their own init script. */
declare global {
  interface Window {
    __fqLoads?: number;
  }
}

/** `GET /api/demo/status` is 404 exactly when `Demo:Enabled=false`; that is the switch, read from the server. */
let demoOn = false;

test.beforeAll(async ({ playwright, baseURL }) => {
  const request = await playwright.request.newContext({ baseURL });
  const status = await request.get("/api/demo/status");
  demoOn = status.ok() && (await status.json()).enabled === true;
  await request.dispose();
});

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.addEventListener("DOMContentLoaded", () => {
      window.top!.document.defaultView!.__fqLoads = (window.top!.__fqLoads ?? 0) + 1;
    });
  });
});

/** The pane's own document. Each pane declares itself with `data-pane`, so captions stay free to change. */
function pane(page: Page, name: PaneName): FrameLocator {
  return page.frameLocator(`iframe[data-pane="${name}"]`);
}

async function loadedDocuments(page: Page): Promise<number> {
  return page.evaluate(() => window.__fqLoads ?? 0);
}

/**
 * The staff order board as the page's own Owner cookie sees it — the same answer the kitchen pane is drawn
 * from, so the round this file submitted can be named exactly rather than guessed.
 */
async function board(request: APIRequestContext): Promise<BoardOrder[]> {
  const response = await request.get("/api/orders");
  expect(response.ok(), await response.text()).toBeTruthy();
  return response.json();
}

/**
 * Opens `/demo` and waits until all three panes and the page's own hub connection are up, so an order sent
 * afterwards is really heard. Returns the table the left pane was handed.
 */
async function openSplitScreen(page: Page): Promise<{ table: string }> {
  const handshakes = new Set<string>();
  const ready = new Promise<void>((resolve) => {
    page.on("websocket", (socket) => {
      if (!socket.url().includes("/hubs/live")) return;
      socket.once("framereceived", () => {
        handshakes.add(socket.url());
        if (handshakes.size >= 4) resolve();
      });
    });
  });

  await page.goto("/demo");
  await expect(page.getByRole("heading", { name: "บอทกำลังทำงาน" })).toBeVisible({ timeout: 30_000 });
  const caption = page.getByText(/^ลูกค้า · โต๊ะ (\S+)$/);
  await expect(caption).toBeVisible({ timeout: 30_000 });
  const table = (await caption.textContent())!.replace("ลูกค้า · โต๊ะ ", "").trim();

  // Each pane has drawn its own real screen.
  await expect(pane(page, "customer").getByText("รวมในบุฟเฟ่ต์").first()).toBeVisible({ timeout: 30_000 });
  await expect(pane(page, "kitchen").getByRole("heading", { name: "จอครัว" })).toBeVisible({ timeout: 30_000 });
  await expect(pane(page, "kitchen").getByText("เชื่อมต่อแล้ว")).toBeVisible({ timeout: 30_000 });
  await expect(pane(page, "owner").getByRole("heading", { name: "ภาพรวม" })).toBeVisible({ timeout: 30_000 });
  await expect(pane(page, "owner").getByRole("heading", { name: "โต๊ะ", exact: true })).toBeVisible({
    timeout: 30_000,
  });
  await ready;

  return { table };
}

test("an order sent from the customer pane reaches the kitchen pane and the owner popup without a reload", async ({
  page,
}) => {
  test.skip(!demoOn, "this stack runs with the demo off");
  await page.setViewportSize({ width: 1440, height: 1060 });
  const { table } = await openSplitScreen(page);

  const customer = pane(page, "customer");
  const kitchen = pane(page, "kitchen");
  const owner = pane(page, "owner");
  const loadsBefore = await loadedDocuments(page);
  expect(loadsBefore).toBeGreaterThanOrEqual(4);
  const highestBefore = Math.max(0, ...(await board(page.request)).map((order) => order.id));

  // Order from the left pane, through its own UI: the first orderable dish, then its cart.
  await customer.getByRole("button", { name: /^เพิ่ม / }).first().click();
  await customer.getByRole("button", { name: /^เปิดตะกร้า/ }).click();
  await customer.getByRole("button", { name: "ส่งออเดอร์" }).click();
  await expect(customer.getByText("ส่งออเดอร์แล้ว")).toBeVisible({ timeout: 10_000 });

  // The round it created, named by the server rather than guessed.
  let mine: BoardOrder | undefined;
  await expect
    .poll(async () => {
      mine = (await board(page.request)).find((order) => order.id > highestBefore);
      return mine?.number ?? null;
    })
    .not.toBeNull();
  if (!mine) throw new Error("the submitted round never reached the staff board");

  // The kitchen pane grew a card for that exact round, marked ใหม่ — with no document navigated.
  const card = kitchen.getByRole("article", { name: `โต๊ะ ${table} รอบที่ ${mine.number}` });
  await expect(card).toBeVisible({ timeout: 10_000 });
  await expect(card.getByText("ใหม่")).toBeVisible();

  // The owner pane's own popup (ticket 15) fired inside its document.
  const popup = owner.getByRole("dialog", { name: `โต๊ะ ${table}` });
  await expect(popup).toBeVisible({ timeout: 10_000 });
  await expect(popup.getByText("มีออเดอร์ใหม่")).toBeVisible();
  await expect(popup.getByText(new RegExp(`^รอบที่ ${mine.number} · ส่ง \\d\\d:\\d\\d$`))).toBeVisible();

  // The bot panel reports the same event: it filters the product's events by `isDemoBot`, and the round the
  // demo handed this viewer is a bot round.
  await expect(page.getByRole("list", { name: "กิจกรรมล่าสุดจากบอท" }).getByText("สั่งรอบที่")).toBeVisible({
    timeout: 10_000,
  });

  // Nothing was navigated: the page and all three panes are still the documents they were.
  expect(await loadedDocuments(page)).toBe(loadsBefore);

  // Leave the demo floor as it was found: the round this file created is cancelled again.
  const cancelled = await page.request.post(`/api/orders/${mine.id}/cancel`, {
    data: { reason: "e2e ticket 26: ออเดอร์จากหน้า split-screen" },
  });
  expect(cancelled.ok(), await cancelled.text()).toBeTruthy();
});

test("the page matches frame d1-demo at its own size", async ({ page }) => {
  test.skip(!demoOn, "this stack runs with the demo off");
  await page.setViewportSize({ width: 1440, height: 1060 });
  await openSplitScreen(page);
  await page.waitForTimeout(1_000);
  await page.screenshot({ path: "test-results/d1-demo.png" });
});

test("with the demo off the page is neither offered nor functional", async ({ page }) => {
  test.skip(demoOn, "this stack runs with the demo on");

  // The login screen may not offer it: no demo card, and therefore no link to this page.
  await page.goto("/login");
  await expect(page.getByRole("heading", { name: "เข้าสู่ระบบ" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "ทดลองใช้งาน" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: /ดูทั้ง 3 มุมมองพร้อมกัน/ })).toHaveCount(0);

  // And typing the URL does not work either: no panes, no bot panel, no live connection.
  const sockets: string[] = [];
  page.on("websocket", (socket) => sockets.push(socket.url()));
  await page.goto("/demo");
  await expect(page.getByText("ไม่มีโหมดเดโมในเซิร์ฟเวอร์นี้")).toBeVisible();
  await expect(page.locator("iframe")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "บอทกำลังทำงาน" })).toHaveCount(0);
  // Nothing more loads after the page itself: the refusal has no panes to frame and no connection to open.
  const loaded = await loadedDocuments(page);
  await page.waitForTimeout(500);
  expect(await loadedDocuments(page)).toBe(loaded);
  expect(sockets.filter((url) => url.includes("/hubs/live"))).toHaveLength(0);
});
