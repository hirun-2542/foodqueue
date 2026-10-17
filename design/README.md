# FoodQueue UI mockups

Modern Minimalism, Clean White + Orange. 33 static frames rendered to PNG in `out/`. No app code exists yet. All data is synthetic demo data (`DATA.md`); design rules are in `BRIEF.md`; the durable system is `../DESIGN.md`.

Rebuild: `node design/build.mjs` (all) or `node design/build.mjs c1 o04` (by id prefix). Needs Playwright Chromium; `PW_CORE` points at a `playwright-core` `index.mjs`, `ICON_DIR` defaults to `design/icons` (vendored Lucide subset). The build fails (exit 2) on clipped text, horizontal overflow or non-font glyphs. `src/_built/` is generated and safe to delete.

## Index (file in `out/`, screen, what it shows)

| File | Size | Screen | Shows |
|---|---|---|---|
| `c1-menu.png` | 390×844 | Customer: menu | Shop banner with shop name, table A1 + Gold badge, sticky categories, buffet vs à la carte rows, sold-out row, cart bar `8 / 20`; two-item tab bar (เมนู / ออเดอร์), no countdown |
| `c2-cart.png` | 390×844 | Customer: cart sheet | Cart lines, per-line notes, limit readout, "ส่งออเดอร์" (no round number) |
| `c3-orders.png` | 390×844 | Customer: orders tab | One merged list of everything the table ordered, each line with a small (36px) menu photo, quantities summed, no rounds, no status badges, no cancel control |
| `c5-states.png` | 1380×900 | Customer: edge states | Last order notice, quantity limit reached, time expired (notices only, no countdown) |
| `c6-end.png` | 1380×900 | Customer: end states | "ขอบคุณที่ใช้บริการ" (closed QR, no amounts), invalid QR |
| `k1-board.png` | 1280×800 | Kitchen display | Three status columns, new order in the alert colour with a halo, age rail over 10 minutes; buttons เริ่มทำ / พร้อมเสิร์ฟ only, ready cards show "รอพนักงานนำไปเสิร์ฟ" |
| `k2-cancel.png` | 1280×800 | Kitchen: cancel dialog | Required reason with quick chips |
| `l1-login.png` / `l2-login-mobile.png` | 1440×900 / 390×844 | Login + Demo Mode entry | Email login, role buttons, bot activity feed |
| `d1-demo.png` | 1440×1060 | Demo split-screen | Customer, kitchen and owner side by side, bot panel |
| `o01-overview.png` | 1440×900 | Owner: overview | KPIs, 12 table cards with start–end time, minutes used and time rail; Last order and "ปิดการสั่งแล้ว" states |
| `o02-open.png` / `o03-qr.png` | 1440×900 | Owner: open table | Package + head count form; session QR result with print |
| `o04-session.png` | 1440×1100 | Owner: table B2 | Rounds, adjustments, sticky bill, pay button |
| `o05-pay-promptpay.png` / `o06-pay-cash.png` / `o07-paid.png` | 1440×1100 | Owner: payment | PromptPay QR, cash with change, success + receipt number |
| `o08-orders.png` | 1440×1180 | Owner: order queue (cards) | 7 order cards with one status button each (เริ่มทำ → พร้อมเสิร์ฟ), แก้ไข / ย้ายโต๊ะ / รวมบิล on every card, new order in the alert colour, "เสร็จแล้ววันนี้" list |
| `o17-order-edit.png` | 1440×900 | Owner: edit order | Lower quantity / remove a line (with undo), reason chips, bill impact ฿1,074.60 → ฿1,021.64, cancel whole order |
| `o18-move-table.png` | 1440×900 | Owner: move table | A4 → B4 picker of free tables, what moves with it |
| `o19-merge-bill.png` | 1440×900 | Owner: merge bills | A1 + A2 merged preview, total ฿2,364.59, single payment |
| `o09-menu.png` / `o10-menu-edit.png` | 1440×900 | Owner: menu | Item table with availability switches; edit drawer with buffet/à la carte rules and a validation error |
| `o11-packages.png` | 1440×900 | Owner: packages | Package table, rank-conflict error, menu access by rank |
| `o12-tables.png` | 1440×900 | Owner: floor map | Room plan (window, kitchen, cashier, door, zones), 12 tiles with guests/seats in two colours only (ใช้งานอยู่ orange tint, ว่าง grey), no animation; selected-table info panel; header buttons แก้ไขผัง / เพิ่มโต๊ะ |
| `o22-tables-edit.png` | 1440×900 | Owner: edit floor map | Seat stepper on every tile, remove on free tiles, dashed add-table tile, side form (name, seats 1–20, zone, delete) |
| `o20-new-order-modal.png` / `o21-new-order-items.png` | 1440×900 | New-order alert | Centred modal with bell and sound indicator over the floor map; the action is "ไปหน้าออเดอร์"; collapsed, and expanded to show what the customer ordered |
| `o13-settings.png` | 1440×1120 | Owner: settings | Shop info, shop banner upload, service/VAT, order limits, live bill preview |
| `o14-reports.png` | 1440×1300 | Owner: reports | 7-day KPIs, daily and hourly bars, package and payment mix, Top 10 |
| `o15-history.png` | 1440×900 | Owner: payment history | 12 paid bills, selected bill detail, reprint |
| `o16-owner-mobile.png` | 390×844 | Owner on a phone | KPI grid, table cards, five-item tab bar |
| `p1-print.png` | 1400×1000 | 80mm print | Pre-bill, receipt, QR slip |

## Deviations from the spec
- Reports use stacked bars instead of pie charts (share reads better; same data).
- QR slip is a third printable document (the spec lists pre-bill and receipt; the per-session QR needs a slip).
- Menu items carry real photographs (30 stand-ins from Wikimedia Commons, square 320px crops); the icon tile remains only as the fallback for an item without a photo.
- Staff order flow is two buttons (เริ่มทำ, พร้อมเสิร์ฟ) and ends at พร้อมเสิร์ฟ; the spec's "เสิร์ฟแล้ว" step and its rule that payment waits for ready orders to be served no longer apply. Move table, merge bills and editing/removing items inside an order were out of scope in the spec and are now designed.
- The overview and table cards show start–end times and minutes used instead of a countdown; a table past its end time shows "ปิดการสั่งแล้ว". The session detail screens (o04–o07) show the same: a "ปิดการสั่งแล้ว" header with minutes used, and no extend-time button (extending time is not a feature).
- The pulse on new order cards is animated in the real build (`.pulse`); PNGs freeze one frame of the halo. A real build must also respect `prefers-reduced-motion`.

## Replace before shipping
Everything is synthetic: shop name, address, tax id, PromptPay number, QR token, receipt numbers, history rows, report figures. The two QR SVGs in `src/assets/` are real and scannable but encode demo values. The 30 menu photos in `src/assets/menu/` are stand-ins (licences and authors in `src/assets/menu/CREDITS.md`, CC BY / CC BY-SA need attribution if kept); a few are loose matches for the dish (for example the Thai-tea, fish-tofu and morning-glory shots) and two show a visible brand (Coca-Cola, Singha).
