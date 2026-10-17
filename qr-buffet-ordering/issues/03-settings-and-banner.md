# 03: ตั้งค่าร้าน: ข้อมูลร้าน SC/VAT ลิมิต Last order และภาพแบนเนอร์

Status: ready-for-agent

Source: `../spec.md` (user stories 65, 87, 88, 89, 90) และ `../plan.md` (Step 4 (settings), Step 2 (Settings)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o13 ใน `../../design/out/`

## What to build

เจ้าของร้านเปิดหน้าตั้งค่า กรอกชื่อร้าน ที่อยู่ โทร เลขผู้เสียภาษี PromptPay ID, Service charge %, VAT % (0–30, เริ่ม 0), ลิมิตต่อเมนูต่อรอบ (เริ่ม 5), ลิมิตรวมต่อรอบ (เริ่ม 20), นาทีเตือน Last order (เริ่ม 15) และอัปโหลดภาพแบนเนอร์ร้าน (jpeg/png/webp ไม่เกิน 2MB) ระบบเก็บเป็น settings เดียวของร้านและปฏิเสธค่านอกช่วง ตัวอย่างบิลสดในหน้าตั้งค่าคำนวณตามค่า SC/VAT ที่กรอก กลไกอัปโหลดไฟล์ที่นี่ใช้ซ้ำกับรูปเมนูใน ticket 05

## Blocked by

02 Login, roles Owner/Kitchen, auth guard

## Acceptance criteria

- [ ] บันทึกแล้วโหลดใหม่ค่าตรงเดิม; ลิมิตนอก 1–99 หรือ VAT/SC นอก 0–30 → `VALIDATION_FAILED` ข้อความไทย (เทสต์ seam 1)
- [ ] อัปโหลดแบนเนอร์ที่ไม่ใช่ jpeg/png/webp หรือเกิน 2MB ถูกปฏิเสธ; ไฟล์ที่ผ่านดึงกลับมาแสดงได้
- [ ] Kitchen เรียก settings → 403
- [ ] หน้าตั้งค่าตรงกับ mockup o13 ส่วนข้อมูลร้าน แบนเนอร์ SC/VAT ลิมิต และ Last order (ตัวอย่างบิลสดแสดงตามค่าที่กรอก)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
