# 06: จัดการโต๊ะ: เพิ่ม แก้ จำนวนที่นั่ง โซน ปิดใช้งาน

Status: ready-for-agent

Source: `../spec.md` (user stories 52) และ `../plan.md` (Step 4 (Table)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o22; ผังพื้นฐานของ o12 ทำใน ticket 08 ใน `../../design/out/`

## What to build

เจ้าของร้านเพิ่มและแก้โต๊ะ (ชื่อไม่ซ้ำ, จำนวนที่นั่ง 1–20, โซน) และปิดใช้งานโต๊ะผ่านโหมดแก้ไขผัง ปุ่ม × ลบ/ปิดใช้งานอยู่เฉพาะโต๊ะว่าง ตัวนับที่นั่งต่อโต๊ะมี −/+ และฟอร์มตั้งค่าโต๊ะด้านข้าง

## Override ของ plan.md

plan.md Step 4 ไม่มีฟิลด์ที่นั่ง/โซน — spec เพิ่มแล้ว ยึด spec

## Blocked by

02 Login, roles Owner/Kitchen, auth guard

## Acceptance criteria

- [ ] ที่นั่งนอก 1–20 หรือชื่อโต๊ะซ้ำ → `VALIDATION_FAILED` (เทสต์ seam 1)
- [ ] โหมดแก้ไขผังตรงกับ mockup o22 (stepper ที่นั่ง, ปุ่ม × เฉพาะโต๊ะว่าง, ช่องเพิ่มโต๊ะเส้นประ, แผงฟอร์มชื่อ/ที่นั่ง/โซน/ลบ) โดยโต๊ะว่างทุกใบเพราะยังไม่มี session
- [ ] กฎ 'ปิดใช้งานโต๊ะที่มี session Open ไม่ได้' พิสูจน์ใน ticket 07 เมื่อมี session
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
