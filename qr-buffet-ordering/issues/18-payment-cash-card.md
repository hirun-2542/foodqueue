# 18: ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

Status: ready-for-agent

Source: `../spec.md` (user stories 27, 29, 69, 70, 72, 74, 75, 76, 77) และ `../plan.md` (Step 8 (payment, receipts), Step 14 (dialog ชำระ)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o06, o07, c6 ใน `../../design/out/`

## What to build

เจ้าของร้านรับชำระเงินสด (กรอกเงินที่ได้รับ เห็นเงินทอน) หรือบัตร (กดยืนยัน) ระบบปิดบิลได้เมื่อ session Open และไม่มีออเดอร์ Pending/Preparing (ออเดอร์ Ready ไม่ขวาง) สำเร็จแล้ว session เป็น Paid ได้เลขใบเสร็จ `R` + วันที่ไทย + `-` + ลำดับ 6 หลักจาก sequence ของฐานข้อมูล, Payment หนึ่งรายการต่อ session และ token ใช้สั่งไม่ได้ทันที ลูกค้าเห็นหน้า "ขอบคุณที่ใช้บริการ" (ไม่แสดงยอดเงิน) และ QR เก่าสั่งไม่ได้ กันการกดจ่ายซ้ำ

## Override ของ plan.md

plan.md บรรทัด 156 (และการทดสอบบรรทัด 248): เงื่อนไข 'ออเดอร์ต้อง Served ก่อนปิดบิล' ถูกแทนด้วย 'ไม่มี Pending/Preparing' (ผู้ใช้ตัดสินแล้ว)

## Blocked by

16 บิล

## Acceptance criteria

- [ ] Cash ไม่พอ → `INSUFFICIENT_AMOUNT`; จ่ายซ้ำ → `ALREADY_PAID`; มีออเดอร์ Pending/Preparing → `ORDERS_IN_PROGRESS` พร้อมบอกว่าออเดอร์ไหนค้าง; เหลือแต่ Ready จ่ายได้ (เทสต์ seam 1)
- [ ] จ่ายสำเร็จได้เลขใบเสร็จตามรูปแบบไม่ซ้ำ (จ่ายพร้อมกันสองคำขอได้ใบเสร็จใบเดียว) และ token สั่งไม่ได้อีก (`SESSION_CLOSED`); hub ตัดการเชื่อมต่อลูกค้าและส่ง `sessionClosed`
- [ ] หลังเลย EndsAt ยังจ่ายได้; Kitchen เรียก payment → 403
- [ ] หน้าตรงกับ mockup o06 (เงินสด) และ o07 (สำเร็จ + เลขใบเสร็จ) และฝั่งลูกค้า c6 (ขอบคุณ ไม่มียอด)
- [ ] ตัวเลือก PromptPay แสดงแต่ถูกปิดพร้อมข้อความจนกว่า ticket 19 จะเสร็จ (ห้ามแสดง QR เปล่า)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
