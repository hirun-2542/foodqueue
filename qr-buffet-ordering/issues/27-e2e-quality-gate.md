# 27: E2E หนึ่ง flow, วิวพอร์ตมือถือ และคุณภาพรวม

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 103) และ `../plan.md` (Step 17, Verification); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame ทุกเฟรมใน design/out เทียบกับหน้าที่สร้าง (ตรวจเฉพาะที่ ticket ก่อนหน้าเปิดทำงานแล้ว) ใน `../../design/out/`

## What to build

มีเทสต์ end-to-end หนึ่ง flow (seam 2) ที่พิสูจน์ว่า UI, Caddy, cookie และ SignalR ทำงานรวมกันบน stack จริงที่เปิด demo และตรวจรวมทั้งระบบตามข้อกำหนด UI: ภาษาไทยอ่านง่าย สีสถานะผ่านคอนทราสต์ WCAG AA ปุ่มกดสบายบนมือถือ เคารพ `prefers-reduced-motion`

## Blocked by

15 ป๊อปอัปออเดอร์ใหม่พร้อมเสียงสำหรับเจ้าของร้านทุกหน้า, 17 แก้ออเดอร์, 19 ชำระ PromptPay, 20 พิมพ์ QR slip ใบแจ้งยอด ใบเสร็จ และประวัติการชำระ/พิมพ์ซ้ำ, 21 ย้ายโต๊ะ, 22 รวมบิลหลายโต๊ะ ชำระครั้งเดียว, 26 หน้า split-screen ลูกค้า ครัว เจ้าของร้านเคียงกัน

## Acceptance criteria

- [x] Playwright: Demo-login Owner → เปิดโต๊ะว่าง → ลูกค้า (browser context ใหม่) เปิด QR URL สั่งหมูสามชั้น ×2 และชาไทย ×1 → การ์ดโผล่ใน "รอรับ" ของจอครัวภายใน 5 วินาที → เลื่อนจน "พร้อมเสิร์ฟ" → หน้าออเดอร์ของลูกค้าแสดงรายการที่สั่ง (ไม่มีสถานะ) → Owner รับชำระเงินสด → หน้าลูกค้าแสดง "ขอบคุณที่ใช้บริการ"
- [x] วิวพอร์ต 390×844 ของหน้าลูกค้าไม่มี horizontal scroll
- [x] สีสถานะ Pending/Preparing/Ready/Cancelled ผ่านคอนทราสต์ AA (ตรวจด้วยเครื่องมือ ไม่ใช่ด้วยสายตา); ปุ่มสำคัญบนมือถือขนาดแตะได้สบาย; pulse หยุดเมื่อ `prefers-reduced-motion`
- [x] ชุดเทสต์ทั้งหมด (seam 1 + seam 2) ผ่านจากเครื่องว่างด้วยคำสั่งที่บันทึกใน README; รายงานตัวเลขผลจริง
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 27 — E2E หนึ่ง flow, วิวพอร์ตมือถือ และคุณภาพรวม

Resolution: one Playwright flow drives the whole product through its own UI on a real docker-compose stack with
Demo on — the Owner opens a free table, a **fresh browser context** scans the QR and orders, the kitchen card
arrives over the hub inside the budget with no reload, the owner takes cash and the customer's phone turns into
the thanks screen by itself — and the same run measures the mobile viewport, the four status colours, the touch
targets and `prefers-reduced-motion`.

Evidence:

- **The flow** (`frontend/e2e/flow.spec.ts`): demo-login Owner (`POST /api/demo/login`, no password) →
  `เปิดโต๊ะ` on the first free table of the real floor → the rendered QR is **decoded from its pixels** (the
  `QRCodeSVG` is serialized, rasterized to a 512px canvas and read with `jsQR`), and the decoded URL is
  `http://localhost:8090/s/<43 chars>` — the run's own origin, asserted against the config's `baseURL` so a
  mis-set `PublicBaseUrl` fails by name instead of sending the probe at another stack. A **new browser context**
  (390×844, `isMobile`, `hasTouch`, no cookies, no storage) opens that URL and orders หมูสามชั้นสไลซ์ ×2 +
  ชาไทยเย็น ×1 by tapping the menu rows and the cart. The card appeared in `รอรับ` **188 ms** after
  `ส่งออเดอร์` (budget 5 s) and asked-for **401 ms** on the first run, marked `ใหม่` — a mark only a hub-delivered
  round gets — while a sentinel written into the kitchen document before the order was still there afterwards, so
  nothing reloaded. `เริ่มทำ` → `พร้อมเสิร์ฟ` moved the card and it read `รอพนักงานนำไปเสิร์ฟ`; the customer's
  `ออเดอร์` tab then listed the same two lines with the right quantities and the owner took cash, after which the
  phone showed `ขอบคุณที่ใช้บริการ` / `ชำระเงินเรียบร้อยแล้ว` / `QR นี้ปิดใช้งานแล้ว` **without a reload** (sentinel
  intact) and with no `฿` or `บาท` anywhere on it.
- **No status leaks to the customer**: the whole customer document's text is searched for `รอรับ`, `กำลังทำ`,
  `พร้อมเสิร์ฟ`, `เสิร์ฟแล้ว`, `รอเสิร์ฟ`, `ยกเลิก` — none present. The run leaves the floor as it found it: the flow
  pays its own bill, and an `afterAll` closes the round if the flow stopped halfway (cancel what is still open,
  then pay what is Ready, else void), so no table is left occupied.
- **390×844 has no horizontal scroll, measured**: on the menu, the cart sheet, the orders tab and the thanks
  screen `innerWidth` is exactly 390, `documentElement.scrollWidth` and `body.scrollWidth` are both 390, and a
  `window.scrollTo(200, …)` leaves `scrollX` at 0.
- **Contrast, computed not eyeballed**: the page's own tokens (`--color-{pending,preparing,ready,cancel}-ink`
  over the matching `-soft`) are resolved by the browser and run through a WCAG 2.x relative-luminance function in
  the test: pending **6.17:1**, preparing **5.91:1**, ready **6.43:1**, cancel **5.66:1** — all ≥ 4.5. The three
  badges the board actually paints were additionally read back from the DOM and compared with those token pairs,
  so the assertion is about the pixels on the card, not about the stylesheet. The cancel pair is the one
  `components/ui/badge.tsx` uses for `variant="cancelled"`; the flow does not produce a cancelled card, so that
  pair is verified from the resolved tokens only, and this is stated rather than implied.
- **Touch targets ≥ 44px, measured per control**: menu-row add **44.0×44.0**, menu-row stepper −/+ 44.0×44.0,
  cart-bar stepper −/+ 44.0×44.0, tab `เมนู` / `ออเดอร์` 195.0×59.0, cart bar 358.0×52.0, `ส่งออเดอร์` 358.0×52.0.
- **`prefers-reduced-motion`**: a second kitchen context (`reducedMotion: "reduce"`, same session) shows the same
  new card with **zero** running animations, while the normal board's card reports exactly `alert-pulse`; the calm
  card keeps its static halo (`… rgb(232, 161, 0) 0px 0px 0px 7px`).
- **A real defect fixed on the way in, verified against the rendered screen** — the class merger did not know
  FoodQueue's own type scale. `frontend/src/lib/utils.ts` was `export { cn } from "cn"` and 15 files imported `cn`
  from `"cn"` directly, so `cn("text-sm text-muted-foreground", "text-label")` produced `text-sm text-label`:
  `cn` read `text-label` as a *colour* and dropped `text-muted-foreground` while keeping the wrong size. Measured
  on the running stack before the fix: the cart sheet's `ยังไม่ได้ส่ง` rendered **14px in `rgb(28, 25, 23)`**
  instead of **13px in `rgb(107, 101, 96)`, and the open-table dialog's title rendered 16px/16px instead of
  24px/32.4px. After `createCn({ extend: { theme: { text: [...] } } })` in `lib/utils.ts` and repointing every
  import: `text-muted-foreground text-label`, **13px**, `rgb(107, 101, 96)`; the dialog title **24px / 32.4px**.
  All 15 direct `from "cn"` imports are gone (0 remain), so there is one merger, not two.
- The same change made the customer's add control match frame c1: in `design/src/frames/c1-menu.html` a dish not
  in the cart is a quiet white `.addbtn` and only the stepper's `+` is orange (orange's four jobs). The menu rows
  now measure **44.0×44.0 px** where they measured **40.0×40.0** before — the 40px circle sits centred inside a
  44px button, so the frame's size is kept and the touch area is not.
- **Numbers from this branch** (`/home/novanod/Documents/ProjectPort/FoodQueue` @ `67f3a7e`, against a private
  `docker compose -p fq27` stack on :8090 whose volumes were **destroyed and recreated** so the demo seeder ran
  from empty — the shared stack on :8080 was never touched):
  `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 246, Skipped: 0, Total: 246`;
  `pnpm build` → compiled, `pnpm exec tsc --noEmit` → clean, `pnpm lint` → 0 findings over 108 files;
  `cd frontend && pnpm exec playwright test` → **33 passed, 7 skipped, 0 failed, 0 flaky** in 40.4 s with the
  default worker count. The 7 skips are the viewport-project skips the desktop-only and demo-switch specs
  declare, not failures.
- Two specs had to be repaired for that run to be green, and both repairs were verified red-then-green against
  a stack old enough to expose them:
  - `e2e/edit-order.spec.ts` asserted a fixture rather than the product — "a fresh database has no service
    charge and no VAT", so `total − ฿45.00`. On a demo stack that is false: SC 10% + VAT 7% are then applied to
    the removed line, and the old assertion fails with `Expected 711.81, Received 703.85`. It now checks the
    dialog's preview against the server's own bill after the same edit is saved
    (`expect(after.bill.total).toBeCloseTo(previewTotal, 2)`), which holds on a fresh database and on the seeded
    one. Its seeded dishes also moved into one reused `E2E 17` category that each run switches off again,
    instead of adding a new `หมูสามชั้นสไลซ์` per run.
  - `e2e/demo-split-screen.spec.ts` matched two accessible names as substrings, so it passed only while the demo
    stack was young. `getByRole("article", { name: "โต๊ะ A1 รอบที่ 1" })` also matches `รอบที่ 10`, `11` … (on the
    aged stack it resolved to **9** elements and the retries failed with a strict-mode violation), and the bot
    panel's `getByText("สั่งรอบที่")` matched every older line at once (2 elements). Both are now named exactly —
    `exact: true` on the card, and `${table} สั่งรอบที่ ${round}` on the panel line — and the spec passes on the
    aged stack (2 passed, 4 skipped) where it previously failed all three attempts.
  The two `new-order-popup.spec.ts` sound cases, previously retry-passing under load, passed first time in the
  final clean run (and 20/20 on their own with `--repeat-each=2` at 1 worker; the file already documents
  `--workers=1` because the popup is shop-wide).
- The README's documented commands are unchanged and are exactly the two lines above.
