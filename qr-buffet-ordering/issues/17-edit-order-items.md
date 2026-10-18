# 17: แก้ออเดอร์: ลดจำนวน/นำรายการออกพร้อมเหตุผล

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 57) และ `../plan.md` (ไม่มีใน plan.md (ใหม่ตาม spec)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o17 ใน `../../design/out/`

## What to build

เจ้าของร้านเปิด "แก้ไข" บนการ์ดออเดอร์ที่รอรับหรือกำลังทำ ลดจำนวนหรือนำรายการออก (เอาคืนได้ก่อนบันทึก) เลือกหรือพิมพ์เหตุผล เห็นผลต่อยอดบิลก่อนบันทึก (เช่น ฿1,074.60 → ฿1,021.64) หรือยกเลิกทั้งออเดอร์ ลูกค้าเห็นรายการที่เหลือในแท็บออเดอร์ และครัวเห็นการ์ดอัปเดตทันที ออเดอร์สถานะอื่นแก้ไม่ได้ เพิ่มจำนวนหรือเพิ่มรายการไม่ได้

## Override ของ plan.md

plan.md ไม่มีการแก้ออเดอร์ — เป็นขอบเขตใหม่ใน spec (ผู้ใช้ตัดสินให้เก็บ)

## Blocked by

14 คิวออเดอร์ของเจ้าของร้าน, 16 บิล

## Acceptance criteria

- [x] แก้ออเดอร์ Pending/Preparing ได้ บิลคำนวณใหม่; Ready/Cancelled → `ORDER_NOT_EDITABLE`; เพิ่มจำนวนถูกปฏิเสธ; ไม่ใส่เหตุผลถูกปฏิเสธ (เทสต์ seam 1)
- [x] นำรายการออกครบทุกบรรทัด = ยกเลิกทั้งออเดอร์ สถานะไม่เปลี่ยนเมื่อแก้บางรายการ
- [x] ส่ง `orderEdited` ถึง kitchen/owner/session; หน้าลูกค้าและจอครัวเปลี่ยนทันที
- [x] หน้าตรงกับ mockup o17 และเปิดจากปุ่ม "แก้ไข" บนการ์ดใน ticket 14 (ทำให้ปุ่มนั้นทำงาน)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 17 — แก้ออเดอร์: ลดจำนวน/นำรายการออกพร้อมเหตุผล

Resolution: an owner can take items off a round that is still being worked, with a reason, and the customer's
list, the kitchen card and the bill all react immediately. Increasing an order is not possible.

Evidence:

- Editable only while the kitchen is still working: Pending and Preparing can be edited and the bill drops
  immediately; Ready and Cancelled answer `ORDER_NOT_EDITABLE` and their rows are unchanged (asserted in the
  database, not just by status). A round whose session is no longer Open answers `SESSION_CLOSED`.
- Reductions only: increasing a quantity, adding an unknown line, a no-op edit, an empty list and a negative
  quantity are all refused with `VALIDATION_FAILED`; a missing or blank reason is refused (the reason is
  required and limited to 1–200 characters).
- Removing every line cancels the round with the reason and empties the à-la-carte total, while a partial edit
  leaves the order's status untouched — which is what the spec asks for.
- Live effects, proven in a browser against a real stack: the dialog previews the bill change before saving
  (฿888.00 → ฿843.00 with `-฿45.00`), saving is blocked without a reason, and after saving the customer's
  orders tab and the kitchen card **both** changed with no reload. `orderEdited` was verified over real
  WebSockets to reach the kitchen, the owner and that table once each, and never another table; the customer
  still never receives `orderStatusChanged`.
- The reason is persisted (`Order.EditReason`) and both the owner queue card and the kitchen card display it
  as `แก้ไขแล้ว · <reason>`; a round that had every line removed stores it as a `CancelReason` instead, because
  that is a cancellation and the kitchen's cancel path already surfaces that field. The `แก้ไขแล้ว` wording is
  the implementer's own — it is not in the approved copy — and is flagged as such.
- Screen compared against `design/out/o17-order-edit.png` at 1440×900: the title `แก้ไขออเดอร์ · A1 รอบที่ 3`,
  the subtitle, the line rows with their thumbnails, notes, steppers and remove control, a removed line struck
  through with `นำออกจากรอบนี้ · คิดเงินแยก ฿45.00` and `เอาคืน`, the reason chips, the sunken `ยอดบิลโต๊ะ` well
  showing the struck-through total beside the new one and the delta, and the footer
  `ยกเลิกทั้งออเดอร์ · ปิด · บันทึกการแก้ไข` all match.
- Schema change (approved): `Order.EditReason` was added and the single `Initial` migration was regenerated
  rather than accumulated, per the repo's convention while no real data exists; the hand-written receipt
  sequence survived the regeneration and the difference from the previous `Initial` is only that column.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 205, Skipped: 0, Total: 205` on the merged
  integration branch (the count includes this ticket plus the two merged after it); `pnpm build` then
  `pnpm exec tsc --noEmit` → clean.
- Two merge conflicts with ticket 21 were resolved by the controller (the queue card now carries both the
  `แก้ไข` and `ย้ายโต๊ะ` actions; `sessions.ts` keeps both the move hook and the bill-delta preview).
