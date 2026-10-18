# 20: พิมพ์ QR slip ใบแจ้งยอด ใบเสร็จ และประวัติการชำระ/พิมพ์ซ้ำ

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 46, 67, 78, 79) และ `../plan.md` (Step 15, Step 14 (history)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame p1, o15 ใน `../../design/out/`

## What to build

เจ้าของร้านพิมพ์ QR slip (โต๊ะ แพ็กเกจ จำนวนคน เวลาเปิด–หมด + QR) ใบแจ้งยอดก่อนรับเงิน และใบเสร็จ (ชื่อร้าน ที่อยู่ โทร เลขผู้เสียภาษี เลขใบเสร็จ รายการ วิธีจ่าย รับเงิน ทอน) ผ่าน browser print กระดาษ 80mm ไม่มี driver พิเศษ ใบเสร็จพิมพ์ได้เฉพาะ session Paid และเปิดบิลที่จ่ายแล้วจากหน้าประวัติเพื่อพิมพ์ใบเสร็จซ้ำได้ ไม่มี kitchen ticket

## Blocked by

18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [x] สามหน้า `/print/{qr|prebill|receipt}/[sessionId]` ตรงกับ mockup p1 (CSS paper 80mm, พิมพ์เป็น PDF ตรวจความกว้างได้)
- [x] ใบเสร็จของ session ที่ยังไม่ Paid ถูกปฏิเสธ; ใบแจ้งยอดแสดงยอดเดียวกับบิลที่ตัวคำนวณให้
- [x] หน้าประวัติตรงกับ mockup o15 (รายการบิลที่ชำระแล้ว รายละเอียดบิล พิมพ์ซ้ำ)
- [x] ปุ่มพิมพ์ใน o03 (QR slip) และ o04 (ใบแจ้งยอด) ทำงานจริง
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 20 — พิมพ์ QR slip ใบแจ้งยอด ใบเสร็จ และประวัติการชำระ/พิมพ์ซ้ำ

Resolution: all three documents print on 80mm paper through the browser, a receipt is only obtainable for a
paid round, and the history screen can reopen and reprint one.

Evidence:

- `GET /api/sessions/{id}/print-data?doc=qr|prebill|receipt` returns the settings block, the round's identity,
  its times, the calculator's bill and — when the round is paid — the payment with its receipt number. An
  unpaid round answers `200` for the QR slip and the pre-bill but **409 `SESSION_CLOSED`** for a receipt, so a
  document that would have to invent a receipt number cannot be produced. A paid round's QR slip is refused
  the same way, because the round is over. An invalid document name answers 400. Kitchen 403, anonymous 401.
- One calculator: the pre-bill total returned by the API equals the total the session endpoint reports for the
  same round (`1441.83` both), asserted in a test rather than against a constant.
- Print media measured in the built CSS and in the browser: **paper 80.00 mm, content 72.00 mm, 12 px,
  numbers right-aligned, toolbar hidden while printing**; `@page { size: 80mm auto; margin: 0 }` sits at the
  top level of the stylesheet (nesting it inside `@media print` is ignored by Chromium, which the implementer
  found and fixed). On screen the paper is 302 px wide, and at 390 px the history table becomes a list with no
  horizontal scroll.
- Numbers reconciled by hand and then read back from the API: the `p1-print` B2 example (Gold 2 adults +
  1 child, two ฿89 beers, a ฿50 adjustment, SC 10%, VAT 7%) gives subtotal ฿1,225.00, service ฿122.50, VAT
  ฿94.33, total **฿1,441.83**; a 3-adult Gold receipt gives **฿1,408.87**; the history footer's three
  receipts sum to ฿3,521.59. A round paid at 23:30 Bangkok and one at 00:10 Bangkok land on different days.
- Screens compared against `design/out/p1-print.png` and `o15-history.png`. Differences found by that
  comparison were fixed rather than accepted: the QR URL line overflowed the 72mm column and is now truncated
  like the frame, and the history's payment column clipped `PromptPay` until it was widened.
- The `พิมพ์ QR slip` button ticket 07 had linked to a route that did not exist now resolves, and the session
  screen's print button reads `พิมพ์ใบเสร็จ` for a paid round and `พิมพ์ใบแจ้งยอด` otherwise — placed at the
  mount point ticket 16 left for it.
- No kitchen ticket exists, and the entity model was not touched, so `Initial` needed no regeneration.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 211, Skipped: 0, Total: 211` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.

### Smoke on the real stack (2026-10-18)

Resolution: driven with my own eyes on a real `docker compose` stack (`fqsm`, Caddy origin
`http://localhost:8091`), all three documents print on 80 mm paper through the browser, an unpaid round's
receipt is refused by the server, a paid round can be closed and reprinted from the history screen, and the
paper width measured both in the built CSS/`@page` and in the rendered layout is 80.00 mm with 72.00 mm
content. **Only acceptance box 5 (the final `- [ ]`, the smoke-run box) is marked proven here** — boxes 1–4
were already ticked by the implementer and this smoke corroborates but does not re-claim them.

Evidence:

- **Round opened through the owner UI, not the API.** `/owner` → `เปิดโต๊ะ` on the free `B1` card → dialog
  `เปิดโต๊ะ B1` showed the three packages (`Silver อันดับ 1 ผู้ใหญ่ ฿299 · เด็ก ฿149 · 90 นาที`, `Gold อันดับ 2
  … ฿399/฿199·90 นาที`, `Platinum อันดับ 3 … ฿549/฿279 · 120 นาที`), the default `2` adults `1` child and
  `ค่าบุฟเฟ่ต์ 2 × ฿299 + 1 × ฿149 ฿747.00`. Ticking **Gold** worked; `เปิดโต๊ะและสร้าง QR` created round
  **`943`** on `B1` and the `o03` dialog read `โต๊ะ B1 เปิดแล้ว` … `Gold · 90 นาที` / `2 ผู้ใหญ่ 1 เด็ก` /
  `เปิด 14:58 · หมด 16:28`, with the real `qrUrl` **`http://localhost:8091/s/Ip_V6Cmv_dcJhj4UHlfZIWdulOChfaomKRWx4cOJX38`**
  (the 8091 origin, i.e. `PUBLIC_BASE_URL` reached the container, not the developer's `8080`).
- **`/print/qr/943` driven in the browser at the Caddy origin.** Title `พิมพ์ QR slip · FoodQueue`; toolbar
  `กระดาษความร้อน 80 มม. · พิมพ์จากเบราว์เซอร์ได้ทันที ไม่ต้องติดตั้งไดรเวอร์ · ไม่มีใบสั่งครัว` with
  `พิมพ์อีกครั้ง`; paper body `โต๊ะ B1 | Gold · 2 ผู้ใหญ่ 1 เด็ก | เปิด 14:58 | หมดเวลา 16:28 | สแกนเพื่อสั่งอาหาร |
  …/s/Ip_V6Cmv_dcJhj4UH… | QR ใช้ได้จนถึงปิดบิล…` (the QR line really is truncated, as the frame draws), the
  limit line `ลิมิต 5 ต่อเมนู · 20 รายการต่อรอบ` and `พิมพ์เมื่อ 18 ต.ค. 2569 14:59`. `window.print()` fired
  **1** time on load (the page opens the print box itself, as designed).
- **Paper width — built CSS.** Fetching the print stylesheet the page actually loads
  (`http://localhost:8091/_next/static/chunks/3_au96ylolksn.css`) finds the rule **at the top level of the
  sheet, not nested inside `@media print`**: `@page{size:80mm auto;margin:0}@media print{.print-screen{bac…`.
  `@media print` then sets `.paper{width:80mm;margin:0 auto;padding:0 4mm 6mm}`.
- **Paper width — rendered layout.** With `emulateMedia({media:'print'})` on the live page,
  `getComputedStyle(.paper).width` is **`302.359px` = 80.00 mm** (302.359375 px ÷ 96 × 25.4 = **79.9993 mm**),
  padding `0px 15.1181px 22.6772px` (4 mm sides + 6 mm bottom), so content = 272.123 px = **71.9993 mm**;
  the toolbar computes to `display:none` and the paper `box-shadow` to `none`. On screen the same page is
  `302px` wide with `padding: 20px 16px 24px`, `font-size: 12px`, which is the frame's paper.
- **The same numbers on all three documents.** QR slip: screen `302px`/print `302.359px`. Pre-bill screen
  `302px` (`20px 16px 24px`), print `302.359px` → content **71.999mm**. Receipt screen `302px`, print
  `302.359px` → content **71.999mm**. Nothing differed per document.
- **PDF proof of the 80 mm paper.** `page.pdf({width:'80mm', preferCSSPageSize:true})` for each document →
  `pdfinfo` reports **`Page size: 227.04 x 792 pts`** for the QR slip, the pre-bill and the receipt
  (227.04 pt = 80.09 mm; 80 mm = 226.77 pt — Chromium rounds to 227.04 pt at 72 dpi mapping). The same call
  **without** `preferCSSPageSize` yields `612 x 792 pts (letter)`, which is what shows the 80 mm comes from
  the page's own `@page` rule and not from the driver.
- **Receipt REFUSED for an unpaid round — API.** Round `943`, still Open: `…/print-data?doc=qr` → **200**,
  `doc=prebill` → **200**, `doc=receipt` → **409 `SESSION_CLOSED`** with `title` = `detail` =
  `บิลนี้ยังไม่ได้ชำระเงิน จึงพิมพ์ใบเสร็จไม่ได้`; `doc=bogus` → **400 `VALIDATION_FAILED`**
  `เอกสารที่พิมพ์ได้คือ qr, prebill หรือ receipt`. Anonymous (no cookie) → **401** on all three plus
  `/api/sessions/history`; a **Kitchen** login (204 on `/api/demo/login`) → **403**.
- **Receipt REFUSED for an unpaid round — through the browser.** Navigating to `/print/receipt/944` (a second
  fresh unpaid round I opened on `B6`, total `1173.47`) rendered
  `พิมพ์ใบเสร็จไม่ได้ | บิลนี้ยังไม่ได้ชำระเงิน จึงพิมพ์ใบเสร็จไม่ได้ | ลองอีกครั้ง | กลับหน้าภาพรวม`, with
  **`.paper` count 0** and **0** `window.print()` calls — no paper, no print box, nothing invented.
- **One calculator: pre-bill total == the bill the session endpoint reports.** Unpaid: session **`943`**
  reported `bill.total 1173.47` and `…/print-data?doc=prebill` reported `1173.47`
  (subtotal `997`, SC `99.70`, VAT `76.77`). I then added an adjustment **through the UI** on that page
  (`ค่าปรับอาหารเหลือ` `+50` → `เพิ่ม`): both endpoints moved to **`1232.32`** together
  (`subtotal 1047.00`, `ค่าบริการ 10% 104.70`, `VAT 7% 80.62`, `adjustmentTotal 50`, `id 96`). The pre-bill
  paper read `ผู้ใหญ่ 2 × ฿399 ฿798.00 | เด็ก 1 × ฿199 ฿199.00 | ค่าปรับอาหารเหลือ +฿50.00 |
  ยอดก่อนค่าบริการ ฿1,047.00 | ค่าบริการ 10% ฿104.70 | VAT 7% ฿80.62 | รวมทั้งสิ้น ฿1,232.32`, under the header
  `ใบแจ้งยอด (ยังไม่ใช่ใบเสร็จ)` and the footer `สแกนเพื่อชำระ | PromptPay 0812345678` — a pre-bill, not a
  receipt.
- **Cash payment through the owner UI.** `/owner/sessions/943` → `รับชำระเงิน` → dialog
  `รับชำระเงิน · โต๊ะ B1 | Gold | ยอดที่ต้องชำระ ฿1,232.32 | ค่าบุฟเฟ่ต์ ฿997.00 · เมนูคิดเงินแยก ฿0.00 ·
  ปรับยอด +฿50.00 · ค่าบริการและ VAT ฿185.32 … พอดี ฿1,232.32`. Entered `2000` (the dialog offered
  `พอดี ฿1,232.32 / ฿1,500 / ฿2,000` chips) and pressed `ยืนยันรับเงินสด` →
  `ชำระเงินสำเร็จ | โต๊ะ B1 · ปิดบิลแล้ว ลูกค้าสั่งเพิ่มไม่ได้อีก | เลขที่ใบเสร็จ R20261018-000940 | วิธีจ่าย เงินสด |
  รับเงิน ฿2,000.00 | เงินทอน ฿767.68`. The API confirms `payment {method:"Cash", amountReceived:2000,
  change:767.68, receiptNo:"R20261018-000940", total:1232.32}` on a round now `status:"Paid"`.
- **`/print/receipt/943` for the paid round.** Title `พิมพ์ใบเสร็จ · FoodQueue`; `ใบเสร็จรับเงิน | เลขที่ |
  R20261018-000940 | วันเวลา | 18 ต.ค. 2569 15:00 | โต๊ะ B1 · Gold | 2 ผู้ใหญ่ 1 เด็ก | เปิด | 14:58` plus the
  shop block `ชาบูเดโม่ / 99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110 / โทร 02-000-0000`, `วิธีจ่าย เงินสด | รับเงิน ฿2,000.00 |
  เงินทอน ฿767.68`, `window.print()` fired **1** time. Reloading the page (a reprint) rendered the *same*
  receipt number `R20261018-000940` and `/api/sessions/history` still answered **1** row — a reprint makes no
  second receipt.
- **A paid round's QR slip is refused.** `…/print-data?doc=qr` for `943` after payment → **409
  `SESSION_CLOSED`** `ปิดบิลแล้ว QR ของรอบนี้ใช้สั่งอาหารไม่ได้อีก จึงไม่พิมพ์ QR slip`; `doc=prebill` and
  `doc=receipt` → **200**, and the receipt answers `payment.receiptNo R20261018-000940`. The session screen's
  own button also flipped: it rendered two `a` elements labelled `พิมพ์ใบเสร็จ` with `href="/print/receipt/943"`
  (header + bill card) instead of the earlier `พิมพ์ใบแจ้งยอด`.
- **`o15` history lists the paid bill and offers the reprint.** `/owner/history` rendered
  `ประวัติการชำระ | อาทิตย์ 18 ต.ค. 2569 · 15:02 | ทั้งหมด 1 | เงินสด 1 | PromptPay 0 | บัตร 0 | 18 ต.ค. 2569 |
  บิลที่ชำระแล้ว | 1 รายการ · รวม ฿1,232.32` and the table row
  `R20261018-000940  B1  15:00  3  Gold  เงินสด  ฿1,232.32`, with the footer `แสดง 1 จาก 1 บิลของวันนี้ |
  เรียงจากเวลาชำระล่าสุด`; the right-hand detail panel carried `ชำระแล้ว` and a link whose href is
  **`/print/receipt/943`** whose label is `พิมพ์ใบเสร็จซ้ำ` — one reprint link, pointing at the real print
  route. `GET /api/sessions/history` returned exactly that one bill.
- **Screens compared against `design/out/p1-print.png`, `o15-history.png`** — the paper's 302 px width,
  centred shop block, dashed rules, right-aligned tabular numbers, the 185 px QR, and the history's two-column
  table + detail layout are the frame's.
- **What this smoke did NOT prove (said rather than asserted):** the ticket also mentions print media at a
  real thermal printer and a 390 px history list layout (`md:hidden` list under 768 px). I exercised neither:
  no physical printer exists here, and I measured the history only at 1440 px. `pdfinfo`'s 227.04 pt is
  Chromium's rounding of the 80 mm sheet, which is a browser-measured width rather than a hardware one.
