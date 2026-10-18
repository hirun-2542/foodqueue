# 14: คิวออเดอร์ของเจ้าของร้าน: กรอง สถานะ และยกเลิกพร้อมเหตุผล

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 55, 56, 58) และ `../plan.md` (Step 7, Step 14 (`/owner/orders`)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o08 ใน `../../design/out/`

## What to build

เจ้าของร้านเปิดหน้าออเดอร์เห็นออเดอร์ของวันนี้เป็นการ์ดคิว กรองตามสถานะ (รอรับ/กำลังทำ/พร้อมเสิร์ฟ) เห็นจำนวนต่อสถานะและจำนวนที่ล่าช้าเกินกำหนด ยกเลิกออเดอร์พร้อมเหตุผลได้เหมือนครัว กดเริ่มทำ/พร้อมเสิร์ฟได้ด้วยบัญชีเจ้าของร้าน และดูจอครัวด้วยบัญชีตัวเองได้ ส่วนรายการ "เสร็จแล้ววันนี้" แสดงออเดอร์ที่พร้อมเสิร์ฟ/ยกเลิก

## Blocked by

12 ครัวเลื่อนสถานะ เริ่มทำ/พร้อมเสิร์ฟ และยกเลิกพร้อมเหตุผล

## Acceptance criteria

- [x] หน้าออเดอร์ตรงกับ mockup o08 (การ์ดคิว ปุ่มสถานะเดียวต่อการ์ด การ์ดใหม่สี alert); ปุ่ม แก้ไข/ย้ายโต๊ะ/รวมบิล บนการ์ดทำงานใน ticket 17/21/22 ตามลำดับ — ใน ticket นี้ไม่แสดงปุ่มเหล่านั้นจนกว่า ticket นั้นจะเสร็จ
- [x] Owner ยกเลิกออเดอร์พร้อมเหตุผลสำเร็จด้วยกฎเดียวกับครัว (ใช้บริการร่วม) และ Kitchen ไม่เห็นเส้นทางของ owner
- [x] รายการและตัวนับอัปเดตเองผ่าน hub (ไม่รีเฟรช)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 14 — คิวออเดอร์ของเจ้าของร้าน: กรอง สถานะ และยกเลิกพร้อมเหตุผล

Resolution: the owner's order queue exists, filters by status with live counters, advances and cancels with
the owner's own account, and shows today's finished rounds.

Evidence:

- `/owner/orders` compared against `design/out/o08-orders.png`: the filter chips with their counts, the
  per-status and late counters, status-coloured cards each carrying a single status action, and a newly
  arriving card in the alert colour with `ใหม่`; the chips went `ทั้งหมด 9 → 10` with no reload when another
  order was submitted.
- Live cross-device update: a Kitchen login moved the same order to Preparing and the owner's card and
  counters followed without a reload, because the queue reuses the kitchen board's own query and live merge.
- The owner's own account can advance an order (`เริ่มทำ` then `พร้อมเสิร์ฟ`) and the round then appears in
  `เสร็จแล้ววันนี้`; cancelling a round with a reason removed its card and added
  `B3 รอบที่ 2 · วัตถุดิบหมด: … ยกเลิก` to the list. The cancellation reuses ticket 12's shared dialog and
  endpoint, so the rule lives in one place.
- Kitchen is kept out: a Kitchen login opening `/owner/orders` lands on `/kitchen`, the queue heading never
  renders for it, and the nav entry is owner-only.
- The page text contains no `เสิร์ฟแล้ว`; Ready rounds read `พร้อมเสิร์ฟ`.
- The `แก้ไข` / `ย้ายโต๊ะ` / `รวมบิล` buttons are deliberately absent — ticket 14's own acceptance list says
  they appear only once tickets 17/21/22 exist. There is a single labelled mount block naming each of them
  and the endpoint it will call.
- Backend: no production change was required — ticket 11's `GET /api/orders` already served both lists. A
  seam-1 test now covers the comma-separated status filter and the rule that a paid table's rounds leave the
  queue while remaining in that day's list, which no test covered before.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 167, Skipped: 0, Total: 167` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
