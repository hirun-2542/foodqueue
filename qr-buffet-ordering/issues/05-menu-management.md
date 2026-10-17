# 05: จัดการหมวด เมนู รูป และสถานะ "หมด"

Status: ready-for-agent

Source: `../spec.md` (user stories 81, 83, 84, 85, 86) และ `../plan.md` (Step 4 (Category, MenuItem, upload)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o09, o10 ใน `../../design/out/`

## What to build

เจ้าของร้านจัดการหมวดหมู่ (ลบได้เฉพาะหมวดที่ว่าง) และเมนู โดยเลือกชนิด Buffet (ระบุแพ็กเกจขั้นต่ำ ไม่มีราคา) หรือ À la carte (ระบุราคา) อัปโหลดรูป (jpeg/png/webp ≤2MB) และสลับ "หมด" ได้ทันที เมนูมีหมายเหตุอิสระต่อรายการเท่านั้น (ไม่มี option group) น้ำซุปเป็น Buffet Item แพ็กเกจที่สูงกว่าสั่งเมนูของแพ็กเกจต่ำกว่าได้ด้วยกฎ `MinPackageRank <= rank` โดยตั้งค่า MinPackageRank ที่นี่และพิสูจน์กฎใน ticket 09 ที่ลูกค้าเห็นเมนู

## Blocked by

03 ตั้งค่าร้าน, 04 จัดการแพ็กเกจบุฟเฟ่ต์

## Acceptance criteria

- [ ] เพิ่ม/แก้เมนู Buffet ที่ไม่มี MinPackageRank หรือมีราคา ≠ 0 ถูกปฏิเสธ; À la carte ที่ราคา ≤ 0 หรือมี MinPackageRank ถูกปฏิเสธ (เห็นข้อผิดพลาดในฟอร์มตาม mockup o10)
- [ ] ลบหมวดที่ยังมีเมนู → ถูกปฏิเสธ; หมวดว่างลบได้
- [ ] อัปโหลดรูปเมนูผ่านกลไกจาก ticket 03; ไฟล์ผิดชนิด/เกิน 2MB ถูกปฏิเสธ; เมนูไม่มีรูปแสดง fallback (ไอคอน/ตัวอักษร)
- [ ] สลับ "หมด" แล้วค่า `IsAvailable` เปลี่ยนทันที (การกระจาย event เป็นหน้าที่ ticket 13)
- [ ] หน้าเมนูตรงกับ mockup o09 (ตาราง สวิตช์ availability) และ o10 (drawer แก้ไข)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
