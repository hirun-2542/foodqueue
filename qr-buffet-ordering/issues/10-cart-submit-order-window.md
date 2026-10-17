# 10: ตะกร้า ส่งออเดอร์ ลิมิต เลขรอบ และการปิดการสั่งเมื่อหมดเวลา

Status: ready-for-agent

Source: `../spec.md` (user stories 3, 4, 12, 13, 14, 15, 16, 17, 18, 19, 23, 24) และ `../plan.md` (Step 6 (create order), Step 12); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame c2, c5 ใน `../../design/out/`

## What to build

ลูกค้าเพิ่ม/ลดจำนวนด้วย +/− ใส่หมายเหตุต่อรายการ (≤100 ตัวอักษร) เห็นจำนวนรวมเทียบลิมิตต่อรอบ ตะกร้าเก็บในเครื่อง (ต่อ token) แยกกันในแต่ละมือถือและอยู่ต่อหลังรีเฟรช กด "ส่งออเดอร์" แล้วได้ข้อความยืนยัน ตะกร้าล้าง และเมื่อเหลือเวลาไม่เกินค่า Last order เห็นประกาศ "Last order" (ข้อความเท่านั้น) เมื่อเลย EndsAt เห็น "หมดเวลาสั่งอาหาร" และปุ่มสั่งถูกปิด ฝั่ง server ตรวจตามลำดับที่ spec กำหนดเสมอ ด้วยเวลาเซิร์ฟเวอร์ (ลูกค้าได้ค่า offset ชดเชยนาฬิกามือถือที่เพี้ยน) เลขรอบรันต่อ session

## Override ของ plan.md

plan.md Step 12 บรรทัด 202–207: นับถอยหลัง `mm:ss`, toast "ส่งออเดอร์รอบที่ N" ถูกแทนด้วยข้อความยืนยันที่ไม่มีเลขรอบ และประกาศ Last order แบบข้อความ

## Blocked by

09 ลูกค้าสแกน QR เข้าเมนูตามแพ็กเกจ

## Acceptance criteria

- [ ] ลำดับตรวจ: session Open → ยังไม่เลย EndsAt → รายการไม่ว่าง → ต่อเมนู (รวมบรรทัดซ้ำ) ≤ ลิมิต → รวม ≤ ลิมิต → เมนู active → IsAvailable → Buffet ในสิทธิ์; ทุกกรณีมีเทสต์ seam 1 ที่ได้ `code` ตรงตาม spec (`SESSION_CLOSED`, `SESSION_EXPIRED`, `EMPTY_ORDER`, `QTY_LIMIT_EXCEEDED`, `ORDER_ITEMS_LIMIT_EXCEEDED`, `ITEM_NOT_FOUND`, `ITEM_UNAVAILABLE`, `ITEM_NOT_IN_PACKAGE`)
- [ ] สั่งสำเร็จสองรอบได้เลขรอบ 1 และ 2 (max+1 ใน transaction เดียว retry หนึ่งครั้งเมื่อชน unique); หน้าจอลูกค้าไม่แสดงเลขรอบ
- [ ] ตั้ง EndsAt ผ่านข้อมูลทดสอบ (ไม่ sleep) แล้วสั่ง → `SESSION_EXPIRED`; หน้าจอแสดง c5 "หมดเวลาสั่งอาหาร" และ Last order ตามค่าที่ตั้งใน ticket 03 โดยไม่มีตัวนับถอยหลัง
- [ ] ข้อความภาษาไทยชัดเจนเมื่อสั่งไม่ได้ (เกินลิมิต หมดเวลา เมนูหมด เมนูไม่อยู่ในแพ็กเกจ)
- [ ] หน้าจอตรงกับ mockup c2 (cart sheet) และ c5 (Last order, ครบลิมิต, หมดเวลา)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
