# 19: ชำระ PromptPay: QR ตามยอดบิล

Status: ready-for-agent

Source: `../spec.md` (user stories 71, 73) และ `../plan.md` (Step 8 (PromptPay), Step 14); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o05 ใน `../../design/out/`

## What to build

เจ้าของร้านเลือก PromptPay เห็น QR ที่สร้างฝั่ง frontend จาก PromptPay ID ที่ตั้งไว้กับยอดรวมของบิล ลูกค้าสแกนจ่าย แล้วเจ้าของร้านกดยืนยันรับเงิน (บันทึกด้วยมือ ไม่มีการยืนยันอัตโนมัติ) ถ้ายังไม่ตั้ง PromptPay ID ตัวเลือกถูกปิดและแสดงข้อความแจ้ง

## Blocked by

18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [ ] QR ที่แสดงถอดได้ PromptPay ID และยอดตรงกับบิล (ตรวจด้วยการถอดรหัส payload)
- [ ] ไม่มี PromptPay ID → ปิดตัวเลือกพร้อมข้อความ ไม่แสดง QR เปล่า
- [ ] ยืนยันรับเงินแล้วใช้เส้นทางปิดบิลเดียวกับ ticket 18 (ได้เลขใบเสร็จ token ใช้ไม่ได้) ตรงกับ mockup o05
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
