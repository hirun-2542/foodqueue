# 11: จอครัว: ออเดอร์ใหม่เข้าทันที เสียง ไฮไลต์ และตัวนับ

Status: ready-for-agent

Source: `../spec.md` (user stories 31, 32, 33, 34, 35, 36, 40, 41) และ `../plan.md` (Step 3 (hub), Step 7 (list), Step 13); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame k1 ใน `../../design/out/`

## What to build

พนักงานครัวเปิดจอครัว (แท็บเล็ตแนวนอน) บนบอร์ดสามคอลัมน์ รอรับ / กำลังทำ / พร้อมเสิร์ฟ เห็นออเดอร์ใหม่ปรากฏทันทีผ่าน SignalR (hub เดียว กลุ่ม kitchen/owner/session/menu; ส่ง event หลังบันทึกฐานข้อมูลสำเร็จเท่านั้น) พร้อมเสียงเตือนสั้น ๆ ที่ปิดเสียงได้ และการ์ดใหม่ถูกไฮไลต์ชั่วคราว การ์ดแสดงโต๊ะ เลขรอบ รายการ จำนวน หมายเหตุเด่น เวลาที่รอและสีแดงเมื่อเกิน 10 นาที ตัวนับต่อคอลัมน์ และสถานะการเชื่อมต่อ; Owner เข้าจอครัวได้ด้วยบัญชีของตน การเชื่อมต่อ hub ของลูกค้าต้องใช้ token ของ session Open และเข้าได้เฉพาะ group ของตัวเองกับ menu

## Override ของ plan.md

plan.md บรรทัด 141: ชุดสถานะที่ใช้ในรายการเริ่มต้นไม่มี Served อีก (spec: Pending/Preparing/Ready)

## Blocked by

10 ตะกร้า ส่งออเดอร์ ลิมิต เลขรอบ และการปิดการสั่งเมื่อหมดเวลา

## Acceptance criteria

- [ ] ลูกค้าส่งออเดอร์ (ticket 10) แล้วการ์ดโผล่ในคอลัมน์ "รอรับ" ภายใน 5 วินาทีโดยไม่รีเฟรช; เทสต์ seam 1 ว่า `orderCreated` ถึงกลุ่ม kitchen และ session ที่ถูกต้องเท่านั้น
- [ ] hub ปฏิเสธ token ผิดหรือ session ที่ปิดแล้ว; ลูกค้าไม่ได้รับ event ของโต๊ะอื่น; Staff เข้า group ตาม role (Owner: owner+kitchen+menu, Kitchen: kitchen+menu)
- [ ] เมื่อ reconnect ฝั่ง staff refetch ข้อมูลทั้งหมด และแสดงสถานะการเชื่อมต่อ
- [ ] จอตรงกับ mockup k1 ยกเว้นปุ่มเลื่อนสถานะที่ทำใน ticket 12; การ์ดใหม่ใช้สี alert และ pulse (เคารพ `prefers-reduced-motion`)
- [ ] เปลี่ยนผังและการ์ดภาพรวมจาก ticket 08 ให้รับ `sessionChanged`/`orderCreated` แทนการ refetch เป็นระยะ
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
