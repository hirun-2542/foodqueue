# 07: เปิดโต๊ะ + QR เฉพาะ session + ยกเลิกโต๊ะ (void)

Status: ready-for-agent

Source: `../spec.md` (user stories 44, 45, 47, 49, 50, 53) และ `../plan.md` (Step 5 (ไม่รวม extend), Step 14 (dialog เปิดโต๊ะ/QR)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o02, o03 ใน `../../design/out/`

## What to build

เจ้าของร้านเปิดโต๊ะว่างโดยเลือกแพ็กเกจและจำนวนผู้ใหญ่/เด็ก ระบบสร้าง Table Session ที่ snapshot ชื่อ/ราคา/rank ของแพ็กเกจ มี Session Token สุ่มเดาไม่ได้ (256 บิต, base64url 43 ตัว) `EndsAt = OpenedAt + DurationMinutes` และแสดง QR (ลิงก์ `/s/<token>`) บนจอ โต๊ะหนึ่งมี session Open ได้ทีละหนึ่ง (partial unique index) ยกเลิกโต๊ะที่เปิดผิดได้เมื่อยังไม่มีออเดอร์ที่ไม่ถูกยกเลิก ไม่มีการต่อเวลา

## Override ของ plan.md

plan.md บรรทัด 116 (`POST /api/sessions/{id}/extend`) และบรรทัด 220 (ปุ่มต่อเวลา 15/30/60) ถูกยกเลิก — ไม่มีการต่อเวลา (ผู้ใช้ตัดสินแล้ว)

## Blocked by

04 จัดการแพ็กเกจบุฟเฟ่ต์, 06 จัดการโต๊ะ

## Acceptance criteria

- [ ] เปิดโต๊ะซ้ำที่ยังมี session Open → `TABLE_OCCUPIED` (เทสต์ seam 1 และด้วยการเปิดพร้อมกันสองคำขอ index ในฐานข้อมูลกันได้)
- [ ] เปิดโต๊ะด้วยแพ็กเกจที่ปิดใช้งาน หรือโต๊ะที่ปิดใช้งาน ถูกปฏิเสธ
- [ ] ปิดใช้งานโต๊ะที่มี session Open ถูกปฏิเสธ (ปิดช่อง ticket 06)
- [ ] void session ที่ไม่มีออเดอร์สำเร็จ → สถานะ Voided โต๊ะว่าง; ที่มีออเดอร์ที่ไม่ถูกยกเลิก → `SESSION_HAS_ORDERS` (เทสต์ seed ออเดอร์เข้าฐานข้อมูลทดสอบตรง ๆ จนกว่า ticket 10 จะมีเส้นทางสั่งจริง)
- [ ] ผลลัพธ์ตรงกับ mockup o02 (ฟอร์มเปิดโต๊ะ) และ o03 (QR ผลลัพธ์); QR สแกนได้จริงจากมือถือเมื่อตั้ง base URL เป็น IP ใน LAN
- [ ] ไม่มี endpoint หรือปุ่ม 'ต่อเวลา' (ตรงกับ mockup o04)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
