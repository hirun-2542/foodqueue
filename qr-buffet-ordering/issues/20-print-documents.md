# 20: พิมพ์ QR slip ใบแจ้งยอด ใบเสร็จ และประวัติการชำระ/พิมพ์ซ้ำ

Status: ready-for-agent

Source: `../spec.md` (user stories 46, 67, 78, 79) และ `../plan.md` (Step 15, Step 14 (history)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame p1, o15 ใน `../../design/out/`

## What to build

เจ้าของร้านพิมพ์ QR slip (โต๊ะ แพ็กเกจ จำนวนคน เวลาเปิด–หมด + QR) ใบแจ้งยอดก่อนรับเงิน และใบเสร็จ (ชื่อร้าน ที่อยู่ โทร เลขผู้เสียภาษี เลขใบเสร็จ รายการ วิธีจ่าย รับเงิน ทอน) ผ่าน browser print กระดาษ 80mm ไม่มี driver พิเศษ ใบเสร็จพิมพ์ได้เฉพาะ session Paid และเปิดบิลที่จ่ายแล้วจากหน้าประวัติเพื่อพิมพ์ใบเสร็จซ้ำได้ ไม่มี kitchen ticket

## Blocked by

18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [ ] สามหน้า `/print/{qr|prebill|receipt}/[sessionId]` ตรงกับ mockup p1 (CSS paper 80mm, พิมพ์เป็น PDF ตรวจความกว้างได้)
- [ ] ใบเสร็จของ session ที่ยังไม่ Paid ถูกปฏิเสธ; ใบแจ้งยอดแสดงยอดเดียวกับบิลที่ตัวคำนวณให้
- [ ] หน้าประวัติตรงกับ mockup o15 (รายการบิลที่ชำระแล้ว รายละเอียดบิล พิมพ์ซ้ำ)
- [ ] ปุ่มพิมพ์ใน o03 (QR slip) และ o04 (ใบแจ้งยอด) ทำงานจริง
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
