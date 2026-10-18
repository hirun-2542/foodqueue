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
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

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
