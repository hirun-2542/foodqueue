# 26: หน้า split-screen ลูกค้า ครัว เจ้าของร้านเคียงกัน

Status: ready-for-agent

Source: `../spec.md` (user stories 108) และ `../plan.md` (Step 16 (split-screen)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame d1 ใน `../../design/out/`

## What to build

ผู้ชมเปิดหน้าเดียวที่แสดงมุมมองลูกค้า ครัว และเจ้าของร้านเคียงกัน (พร้อมแผง bot) เห็นข้อมูลไหลข้ามบทบาทแบบ real-time เมื่อลูกค้าสั่งหรือครัวเลื่อนสถานะ

## Blocked by

13 รายการของโต๊ะฝั่งลูกค้าแบบ real-time ออฟไลน์/reconnect และเมนูหมดทันที, 25 Bot จำลองลูกค้าสั่งอาหารและปิดบิลเอง

## Acceptance criteria

- [ ] หน้าตรงกับ mockup d1 (สามมุมมอง + แผง bot) ใช้ Demo login/customer session จาก ticket 24 และ bot จาก ticket 25
- [ ] สั่งจากมุมมองลูกค้าแล้วการ์ดโผล่ที่ครัวและป๊อปอัปที่ owner ในหน้าเดียวโดยไม่รีเฟรช
- [ ] Demo ปิด → หน้านี้ไม่ถูกเสนอ/ไม่ทำงาน
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
