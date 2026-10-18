# 16: บิล: ตัวคำนวณ การปรับยอด และหน้ารายละเอียด session

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 60, 61, 62, 63, 64, 66) และ `../plan.md` (Step 8 (calculator, adjustments), Step 14 (`/owner/sessions/[id]`)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o04 ใน `../../design/out/`

## What to build

มีตัวคำนวณบิลเป็นฟังก์ชันบริสุทธิ์จุดเดียว: ค่าบุฟเฟ่ต์ = ผู้ใหญ่×ราคา snapshot + เด็ก×ราคา snapshot; à la carte = Σ ราคา×จำนวน ของออเดอร์ที่ไม่ Cancelled; subtotal รวมการปรับยอด; service/vat ปัด 2 ตำแหน่ง AwayFromZero; total ใช้ร่วมทุกที่ เจ้าของร้านเปิดหน้ารายละเอียด session เห็นรายการออเดอร์ ปรับยอดทั้งบวกและลบพร้อมเหตุผล ลบรายการปรับยอดที่ใส่ผิด และเห็นบิลแยกบรรทัด (ค่าบุฟเฟ่ต์ผู้ใหญ่/เด็ก, เมนูคิดเงินแยก, ปรับยอด, ค่าบริการ, VAT, รวม) ที่อัปเดตทันที ปรับยอดทำได้เฉพาะ session Open และห้ามทำให้ subtotal ติดลบ

## Override ของ plan.md

plan.md บรรทัด 220: ปุ่มต่อเวลาในหน้ารายละเอียด session ถูกยกเลิก

## Blocked by

12 ครัวเลื่อนสถานะ เริ่มทำ/พร้อมเสิร์ฟ และยกเลิกพร้อมเหตุผล

## Acceptance criteria

- [x] เทสต์ seam 1 ผ่าน API บิล (ไม่มี unit test แยก): ราคา snapshot (แก้ราคาแพ็กเกจทีหลังไม่กระทบ), ออเดอร์ที่ยกเลิกไม่นับ, subtotal 1000.05 กับ SC 10% ได้ service 100.01, ปรับยอดจนติดลบ → `NEGATIVE_SUBTOTAL`
- [x] ตัวอย่าง session A1 ของ DATA (Gold 2 ผู้ใหญ่ + ชาไทย + โค้ก, SC 10%, VAT 7%) ได้ยอดรวมตามบรรทัดบิลที่ระบุใน design/DATA.md
- [x] หน้าตรงกับ mockup o04 (หัวหน้าแสดง "ปิดการสั่งแล้ว" และนาทีที่ใช้ไปเมื่อเลย EndsAt ใช้ข้อมูลเดียวกับ ticket 08; ไม่มีปุ่มต่อเวลา); ปุ่ม แก้ไขออเดอร์ อยู่ใน ticket 17 และ ปุ่มชำระเงินอยู่ใน ticket 18
- [x] หน้าลูกค้าไม่แสดงบิลหรือยอดรวมของโต๊ะ (ไม่มีบิลฝั่งลูกค้า)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 16 — บิล: ตัวคำนวณ การปรับยอด และหน้ารายละเอียด session

Resolution: adjustments are a real, guarded operation on top of the single bill calculator, and the session
detail screen shows the itemised bill that the calculator produces.

Evidence:

- `design/DATA.md`'s A1 example was reproduced through the real API (Gold, 2 adults at ฿399, the three rounds
  listed there, SC 10%, VAT 7%) and reconciled line by line: buffet 798.00 + ชาไทยเย็น 2 × ฿45 = 90.00 +
  โค้ก 25.00 → subtotal **913.00**, service **91.30**, VAT **70.30**, total **1,074.60**.
- The spec's rounding case: subtotal 1000.05 at SC 10% gives service exactly **100.01**; adding VAT 7% gives
  VAT 77.00 and total 1,177.06.
- Snapshot immunity: after raising the package price to 459/229, an already-open session still bills 997.00
  while a newly opened one bills 1,147.00.
- Cancelled orders are not billed (à-la-carte drops accordingly, and the cancelled round leaves the total),
  and a positive adjustment (+50) followed by a negative one (−120) moved the subtotal as expected; deleting
  the −120 restored it. Each successful change emitted exactly one `sessionChanged`, and a refused change
  emitted nothing.
- Guard rails: an adjustment that would drive the subtotal below zero is refused with `NEGATIVE_SUBTOTAL` and
  stores nothing, including through a delete (−299.01 on a ฿299 bill refused; exactly −299 accepted); a Paid
  or Voided session is refused with `SESSION_CLOSED`; a session past `EndsAt` can still be adjusted and paid,
  which is the spec's time rule; empty or over-long reasons, zero amounts and more than 2 decimals are
  `VALIDATION_FAILED`; another table's adjustment cannot be deleted through this route.
- The customer never sees a bill: `GET /api/s/{token}` carries no bill, total or adjustment fields, and no
  amount appears on the customer screen.
- Screen compared against `design/out/o04-session.png` with B2 seeded through the real API — the bill matched
  the frame exactly (798 + 199 + 178 + 50 → subtotal 1,225.00, service 122.50, VAT 94.33, total **1,441.83**),
  the header shows `ปิดการสั่งแล้ว` with `ใช้ไป 107 นาที` and a red rail, and there is no extend button.
  In the browser, adding −20 updated the bill immediately, removing it restored the total, and entering −5000
  surfaced the Thai `NEGATIVE_SUBTOTAL` message as a toast. At 390 px there is no horizontal scroll.
- The `แก้ไขออเดอร์` and payment buttons are deliberately absent (tickets 17 and 18); the screen leaves one
  named mount point for each.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 167, Skipped: 0, Total: 167` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
