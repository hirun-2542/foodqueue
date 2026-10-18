# 12: ครัวเลื่อนสถานะ เริ่มทำ/พร้อมเสิร์ฟ และยกเลิกพร้อมเหตุผล

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 37, 38, 39, 102) และ `../plan.md` (Step 7 (workflow, cancel), Step 13); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame k1, k2 ใน `../../design/out/`

## What to build

พนักงานครัวกดปุ่มใหญ่ "เริ่มทำ" (Pending→Preparing) และ "พร้อมเสิร์ฟ" (Preparing→Ready) ออเดอร์ที่พร้อมแล้วแสดง "รอพนักงานนำไปเสิร์ฟ" และสถานะ Ready เป็นขั้นสุดท้ายของระบบ (ไม่มี Served) ยกเลิกออเดอร์ได้พร้อมเหตุผล (ปุ่มบนการ์ด) จากสถานะ Pending/Preparing/Ready การเลื่อนสถานะเป็น conditional update จากสถานะก่อนหน้าที่คาดไว้ ถ้ากดซ้ำหรือสองเครื่องกดพร้อมกันผลสำเร็จครั้งเดียวและอีกครั้งได้ `INVALID_STATUS_TRANSITION` และจอแจ้งว่าออเดอร์ถูกอัปเดตจากเครื่องอื่นแล้ว

## Override ของ plan.md

plan.md บรรทัด 21, 74, 141, 247: ขั้น Served และการทดสอบ 'ยกเลิกออเดอร์ที่ Served' ถูกยกเลิก (ผู้ใช้ตัดสินแล้ว) ให้ใช้เฉพาะ Pending→Preparing→Ready

## Blocked by

11 จอครัว

## Acceptance criteria

- [x] ข้ามขั้น Pending→Ready ถูกปฏิเสธ; เลื่อนต่อจาก Ready ไม่ได้; ยกเลิกออเดอร์ที่ Cancelled แล้วถูกปฏิเสธ; ยกเลิกต้องมีเหตุผล (1–200 ตัวอักษร) (เทสต์ seam 1)
- [x] ส่งการเลื่อนสถานะพร้อมกันสองคำขอ → สำเร็จหนึ่ง ปฏิเสธหนึ่ง (`INVALID_STATUS_TRANSITION`)
- [x] `orderStatusChanged` ถึง kitchen และ owner เท่านั้น ลูกค้าไม่ได้รับ (เทสต์ seam 1); การยกเลิกส่ง `orderEdited` ถึง kitchen/owner/session
- [x] ออเดอร์ที่ถูกยกเลิกไม่ถูกคิดเงิน (พิสูจน์ผ่านบิลใน ticket 16)
- [x] จอตรงกับ mockup k1 (ปุ่ม เริ่มทำ / พร้อมเสิร์ฟ, "รอพนักงานนำไปเสิร์ฟ") และ k2 (dialog ยกเลิกพร้อมเหตุผลและ chip ลัด)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 12 — ครัวเลื่อนสถานะ เริ่มทำ/พร้อมเสิร์ฟ และยกเลิกพร้อมเหตุผล

Resolution: the kitchen advances a round Pending→Preparing→Ready with a conditional update, cancelling
carries a reason, and the board is the only place either happens.

Evidence:

- Smoke on the real `docker compose` stack through Caddy, as a Kitchen account at 1280×800: pressing `เริ่มทำ`
  on the top card moved it out of `รอรับ` (3 → 2), and pressing `พร้อมเสิร์ฟ` moved it into `พร้อมเสิร์ฟ`
  (counter 1) where the card reads `รอพนักงานนำไปเสิร์ฟ` with its `⋯` control — the whole flow ran against the
  real API, not a stub, and both the status-filtered list and the board counters followed it.
