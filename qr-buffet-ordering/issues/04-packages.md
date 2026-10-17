# 04: จัดการแพ็กเกจบุฟเฟ่ต์

Status: ready-for-agent

Source: `../spec.md` (user stories 80, 82) และ `../plan.md` (Step 4 (Package)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o11 ใน `../../design/out/`

## What to build

เจ้าของร้านสร้างและแก้แพ็กเกจหลายระดับ (ชื่อ, Rank ไม่ซ้ำ, ราคาผู้ใหญ่, ราคาเด็ก, ระยะเวลาเป็นนาที) และปิดใช้งานแพ็กเกจแทนการลบเพื่อรักษาประวัติการขาย

## Blocked by

02 Login, roles Owner/Kitchen, auth guard

## Acceptance criteria

- [ ] สร้างแพ็กเกจ 3 ระดับตามตัวอย่าง (Silver/Gold/Platinum) แล้วเห็นในตารางแพ็กเกจ; Rank ซ้ำถูกปฏิเสธพร้อมข้อความไทย (เทสต์ seam 1 + เห็นใน UI)
- [ ] ปิดใช้งานแพ็กเกจแล้วเลือกเปิดโต๊ะไม่ได้ (พิสูจน์ใน ticket 07) และไม่มี endpoint ลบ
- [ ] หน้าแพ็กเกจตรงกับ mockup o11 (ตาราง ข้อผิดพลาด rank ชน)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
