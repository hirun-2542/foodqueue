import { readFileSync } from "node:fs";
import { join } from "node:path";
import { type APIRequestContext, type Locator, type Page, expect, test } from "@playwright/test";
// The encoder under test, imported directly: its two inputs are the ID and the total, and the `null`
// cases below need no browser at all.
import { formatPromptPayId, promptPayPayload } from "../src/lib/promptpay";

declare global {
  interface Window {
    /** `jsQR`'s browser build, injected per page — the decoder this file trusts the pixels to. */
    jsQR: (data: Uint8ClampedArray, width: number, height: number) => { data: string } | null;
  }
}

/**
 * Ticket 19 — the PromptPay QR of frame `o05` on a running stack built from this branch.
 *
 * The proof this file exists for: the QR the dialog actually renders is **decoded**, and the decoded
 * account plus the decoded amount are compared against the shop's configured `promptPayId` and the
 * server's bill total. "A QR element exists" would not satisfy the ticket.
 *
 * How it decodes: the rendered `QRCodeSVG` is serialized, rasterized by the browser into a 512px
 * canvas (the same pixels the owner's phone camera sees) and read back with `jsQR` (dev dependency),
 * injected into the page as a classic script. The returned payload is then walked field by field here,
 * with the CRC recomputed locally — no PromptPay library is trusted to check its own output.
 *
 * Like the other specs that need a real round, this one uses a table of its own per viewport project
 * and switches it off again, so no real table's bill is touched.
 */

const OWNER = { email: "owner@demo.local", password: "Demo1234!" };
/** The EMVCo fields this payload must carry (spec; `plan.md` "Assumptions & contingencies"). */
const PROMPTPAY_AID = "A000000677010111";
const CURRENCY_THB = "764";
const COUNTRY_TH = "TH";
/** The screen's own formatter (`@/lib/format`), so an assertion here is about the server's number, not a copy. */
const baht = new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" });

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
  total: number;
}

/** One TLV field as the payload writes it: two-digit tag, two-digit length, then the value. */
interface Field {
  tag: string;
  value: string;
}

/**
 * Walk an EMVCo payload's top level. Lengths here are the two-digit form (`< 100`), which is all a
 * PromptPay merchant payload uses; a 3-digit form would be a malformed payload for this ticket.
 */
function parseTlv(payload: string, from = 0, to = payload.length): Field[] {
  const fields: Field[] = [];
  let index = from;
  while (index < to) {
    const tag = payload.slice(index, index + 2);
    const length = Number.parseInt(payload.slice(index + 2, index + 4), 10);
    expect(Number.isInteger(length), `field ${tag} has no two-digit length in ${payload}`).toBeTruthy();
    fields.push({ tag, value: payload.slice(index + 4, index + 4 + length) });
    index += 4 + length;
  }
  expect(index, `fields do not fill the payload exactly: ${payload}`).toBe(to);
  return fields;
}

/** The one value of `tag`, asserting the payload carries it exactly once. */
function field(fields: Field[], tag: string): string {
  const matches = fields.filter((item) => item.tag === tag);
  expect(matches, `expected exactly one field ${tag} in ${JSON.stringify(fields)}`).toHaveLength(1);
  return matches[0].value;
}

/** CRC-16/CCITT-FALSE (poly `0x1021`, init `0xFFFF`, no reflection, no final XOR) — the EMVCo `63` field. */
function crc16(text: string, crc = 0xffff): number {
  for (const character of text) {
    crc ^= character.charCodeAt(0) << 8;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc & 0x8000) !== 0 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
    }
  }
  return crc;
}

let round: Round;

/**
 * `jsQR`'s browser build, loaded once. The spec injects it into the page as a classic script because the
 * decode has to run on the query canvas inside the browser, exactly where the rendered pixels are.
 */
const JSQR_SOURCE = readFileSync(join(__dirname, "../node_modules/jsqr/dist/jsQR.js"), "utf8");

async function login(request: APIRequestContext) {
  const response = await request.post("/api/auth/login", { data: OWNER });
  expect(response.status()).toBe(204);
}

/** The QR the dialog renders, decoded from its pixels — the ticket's acceptance evidence. */
async function decodeRenderedQr(dialog: Locator): Promise<string> {
  const svg = dialog.locator('svg[role="img"]');
  await expect(svg).toHaveCount(1);
  const payload = await svg.evaluate(async (node) => {
    const source = new XMLSerializer().serializeToString(node);
    const image = new Image();
    await new Promise<void>((resolve, reject) => {
      image.onload = () => resolve();
      image.onerror = () => reject(new Error("the rendered QR could not be rasterized"));
      image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`;
    });
    // Upscaled so jsQR sees the modules a camera would; the source is vector, so nothing is lost.
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
    const decoded = window.jsQR(pixels.data, pixels.width, pixels.height);
    return decoded ? decoded.data : null;
  });
  expect(payload, "the rendered QR decoded to nothing").not.toBeNull();
  return payload!;
}

async function setActive(request: APIRequestContext, table: TableRow, isActive: boolean) {
  const response = await request.put(`/api/tables/${table.id}`, { data: { ...table, isActive } });
  expect(response.ok(), await response.text()).toBeTruthy();
}

/** Closes a round an earlier interrupted run left on the run's table, so a rerun starts from a free one. */
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
    await request.post(`/api/orders/${order.id}/cancel`, { data: { reason: "e2e ticket 19: รอบค้างจากรอบก่อน" } });
  }
  const voided = await request.post(`/api/sessions/${leftover.id}/void`);
  expect(voided.ok(), await voided.text()).toBeTruthy();
}

/**
 * Opens a round on the run's own table with one Ready order, so the bill can be closed and its total is
 * a number the spec knows from the server rather than from the design copy.
 */
async function openRound(request: APIRequestContext): Promise<Round> {
  const tableName = `E2E-19-${test.info().project.name}`;
  const tables: TableRow[] = await (await request.get("/api/tables")).json();
  const known = tables.find((row) => row.name === tableName);
  if (known && !known.isActive) await setActive(request, known, true);
  if (!known) {
    const created = await request.post("/api/tables", { data: { name: tableName, seats: 4, isActive: true } });
    expect(created.ok(), await created.text()).toBeTruthy();
  }
  const reread: TableRow[] = await (await request.get("/api/tables")).json();
  const table = reread.find((row) => row.name === tableName);
  if (!table) throw new Error(`the stack did not keep the run's table ${tableName}`);
  await voidLeftoverRound(request, table);

  const packages: Array<{ id: number; isActive: boolean }> = await (await request.get("/api/packages")).json();
  const pkg = packages.find((p) => p.isActive);
  if (!pkg) throw new Error("the stack has no active package to open a round with");
  const opened = await request.post("/api/sessions", {
    data: { tableId: table.id, packageId: pkg.id, adultCount: 2, childCount: 1 },
  });
  expect(opened.ok(), await opened.text()).toBeTruthy();
  const session: { id: number; qrUrl: string } = await opened.json();
  const token = session.qrUrl.split("/s/")[1];

  const menu: { categories: Array<{ items: Array<{ id: number; kind: string; isAvailable: boolean }> }> } = await (
    await request.get(`/api/s/${token}/menu`)
  ).json();
  const items = menu.categories.flatMap((category) => category.items).filter((item) => item.isAvailable);
  const buffet = items.find((item) => item.kind === "Buffet");
  const alaCarte = items.find((item) => item.kind === "AlaCarte");
  const submitted = await request.post(`/api/s/${token}/orders`, {
    data: {
      items: [
        ...(buffet ? [{ menuItemId: buffet.id, quantity: 2, note: null }] : []),
        ...(alaCarte ? [{ menuItemId: alaCarte.id, quantity: 1, note: null }] : []),
      ],
    },
  });
  expect(submitted.status(), await submitted.text()).toBe(201);

  const detail: { bill: { total: number }; orders: Array<{ id: number; status: string }> } = await (
    await request.get(`/api/sessions/${session.id}`)
  ).json();
  for (const order of detail.orders.filter((row) => row.status !== "Cancelled")) {
    for (const status of ["Preparing", "Ready"]) {
      const advanced = await request.post(`/api/orders/${order.id}/status`, { data: { status } });
      expect(advanced.ok(), await advanced.text()).toBeTruthy();
    }
  }
  const closed: { bill: { total: number } } = await (
    await request.get(`/api/sessions/${session.id}`)
  ).json();
  return { table, sessionId: session.id, token, total: closed.bill.total };
}

/** Opens the payment dialog on the session screen and switches to the PromptPay method. */
async function openPromptPayDialog(page: Page): Promise<Locator> {
  await page.goto(`/owner/sessions/${round.sessionId}`);
  await page.getByRole("button", { name: "รับชำระเงิน" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await dialog.getByRole("button", { name: "PromptPay" }).click();
  return dialog;
}

test.beforeEach(async ({ page }) => {
  // Every page gets the decoder before any navigation, so the tests below only deal with the QR itself.
  await page.addInitScript(JSQR_SOURCE);
});

test.beforeAll(async ({ playwright, baseURL }) => {
  const request = await playwright.request.newContext({ baseURL });
  await login(request);
  round = await openRound(request);
  await request.dispose();
});

/** Cancels a round's live orders then voids it — the only sequence that leaves the table free again. */
async function voidRound(request: APIRequestContext, sessionId: number) {
  const detail: { orders: Array<{ id: number; status: string }> } = await (
    await request.get(`/api/sessions/${sessionId}`)
  ).json();
  for (const order of detail.orders.filter((row) => row.status !== "Cancelled")) {
    await request.post(`/api/orders/${order.id}/cancel`, { data: { reason: "e2e ticket 19: เก็บรอบทิ้ง" } });
  }
  await request.post(`/api/sessions/${sessionId}/void`);
}

test.afterAll(async ({ playwright, baseURL }) => {
  if (!round) return;
  const request = await playwright.request.newContext({ baseURL });
  await login(request);
  // The paying test closes the round itself; a case that failed earlier leaves it Open, and voiding an
  // already-Paid round answers 409 rather than throwing, so both paths are safe here.
  await voidRound(request, round.sessionId).catch(() => undefined);
  await setActive(request, round.table, false);
  await request.dispose();
});

test.describe.configure({ mode: "serial" });

test("the QR the dialog renders decodes to the configured PromptPay ID and the bill total, CRC valid", async ({
  page,
}) => {
  await login(page.request);
  const settings: { promptPayId: string | null; name: string } = await (
    await page.request.get("/api/settings")
  ).json();
  expect(settings.promptPayId, "this stack has no PromptPay ID to pay with").toBeTruthy();
  expect(round.total).toBeGreaterThan(0);

  const dialog = await openPromptPayDialog(page);
  await expect(dialog.getByRole("button", { name: "ยืนยันว่าได้รับเงินแล้ว" })).toBeEnabled();
  // The caption frame `o05` prints under the QR; the dialog is captured for the image comparison too.
  await expect(dialog.getByText(`PromptPay ${formatPromptPayId(settings.promptPayId!)} · ${settings.name}`)).toBeVisible();
  await expect(dialog.getByText(/^เงินโอนเข้าบัญชีร้านโดยตรง/)).toBeVisible();

  const payload = await decodeRenderedQr(dialog);
  console.log(`decoded payload: ${payload}`);
  // The frame is 1440×1100@1; captured at the same size for the image comparison against `design/out`.
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.screenshot({ path: `test-results/o05-pay-promptpay-${test.info().project.name}.png` });

  // The CRC the payload carries is the CRC this file computes over everything before it.
  const fields = parseTlv(payload);
  const crcField = field(fields, "63");
  expect(crcField).toHaveLength(4);
  expect(Number.parseInt(crcField, 16)).toBe(crc16(payload.slice(0, payload.length - 4)));

  // The fixed EMVCo skeleton, field by field.
  expect(field(fields, "00")).toBe("01");
  expect(field(fields, "01")).toBe("12"); // dynamic: an amount is present
  expect(field(fields, "58")).toBe(COUNTRY_TH);
  expect(field(fields, "53")).toBe(CURRENCY_THB);

  // The merchant account template: the PromptPay AID and the target, as `0066…` for a Thai mobile.
  const merchant = parseTlv(field(fields, "29"));
  expect(field(merchant, "00")).toBe(PROMPTPAY_AID);
  const digits = settings.promptPayId!.replace(/\D/g, "");
  expect(field(merchant, "01")).toBe(`0066${digits.slice(1)}`);

  // The amount is this bill's total to the satang, not a placeholder.
  expect(field(fields, "54")).toBe(round.total.toFixed(2));

  console.log(
    `decoded fields: ${fields
      .map((item) => `${item.tag}=${item.tag === "29" ? JSON.stringify(parseTlv(item.value)) : item.value}`)
      .join(" ")}`,
  );
});

test("no PromptPay ID: the option is closed with a message and no QR is rendered", async ({ page }) => {
  await login(page.request);
  // The stack this suite runs against has an ID (the test above needs one), so the unconfigured shop is
  // simulated at the network edge: the screen must react to the server saying `null`, not to a test flag.
  await page.route("**/api/settings", async (route) => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...(await response.json()), promptPayId: null } });
  });

  await page.goto(`/owner/sessions/${round.sessionId}`);
  await page.getByRole("button", { name: "รับชำระเงิน" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();

  await expect(dialog.getByRole("button", { name: "PromptPay" })).toBeDisabled();
  await expect(dialog.getByText("PromptPay: ยังไม่ได้ตั้งค่า PromptPay ID")).toBeVisible();
  // Never an empty or placeholder QR.
  await expect(dialog.locator('svg[role="img"]')).toHaveCount(0);
});

test("the encoder refuses to invent a payload: no ID or no positive total yields null", () => {
  expect(promptPayPayload(null, 1441.83)).toBeNull();
  expect(promptPayPayload("", 1441.83)).toBeNull();
  expect(promptPayPayload("   ", 1441.83)).toBeNull();
  expect(promptPayPayload("-", 1441.83)).toBeNull();
  expect(promptPayPayload("0812345678", 0)).toBeNull();
  expect(promptPayPayload("0812345678", -1)).toBeNull();
  expect(promptPayPayload("0812345678", Number.NaN)).toBeNull();
  // With both inputs present it always carries the amount, as a dynamic payload must.
  const payload = promptPayPayload("081-234-5678", 1441.83)!;
  expect(field(parseTlv(payload), "54")).toBe("1441.83");
  expect(formatPromptPayId("081-234-5678")).toBe("081-234-5678");
  expect(formatPromptPayId("0812345678")).toBe("081-234-5678");
});

// Last: it closes the run's round, which every case above needs Open.
test("confirming a PromptPay payment closes the bill the cash way: receipt number, token dead", async ({ page }) => {
  await login(page.request);
  const dialog = await openPromptPayDialog(page);
  await decodeRenderedQr(dialog);

  await dialog.getByRole("button", { name: "ยืนยันว่าได้รับเงินแล้ว" }).click();

  // The same success state cash reaches: frame `o07` inside the dialog, with the server's receipt number.
  await expect(dialog.getByRole("heading", { name: "ชำระเงินสำเร็จ" })).toBeVisible();
  const receipt = await dialog.locator("span.font-mono").filter({ hasText: /^R\d{8}-\d{6}$/ }).innerText();
  expect(receipt).toMatch(/^R\d{8}-\d{6}$/);
  // PromptPay takes the exact total as received, with no change — nothing is tendered.
  const received = dialog.getByText("รับเงิน").locator("xpath=following-sibling::span[1]");
  await expect(received).toHaveText(baht.format(round.total));
  const change = dialog.getByText("เงินทอน").locator("xpath=following-sibling::span[1]");
  await expect(change).toHaveText(baht.format(0));

  const detail: {
    status: string;
    payment: { method: string; amountReceived: number; change: number; receiptNo: string; total: number };
  } = await (await page.request.get(`/api/sessions/${round.sessionId}`)).json();
  expect(detail.status).toBe("Paid");
  expect(detail.payment.method).toBe("PromptPay");
  expect(detail.payment.amountReceived).toBe(round.total);
  expect(detail.payment.change).toBe(0);
  expect(detail.payment.receiptNo).toBe(receipt);

  // The round's QR stops ordering the moment the bill is paid.
  const refused = await page.request.post(`/api/s/${round.token}/orders`, {
    data: { items: [{ menuItemId: 1, quantity: 1 }] },
  });
  expect(refused.status()).toBe(409);
  expect((await refused.json()).code).toBe("SESSION_CLOSED");
});
