# 02: Login, roles Owner/Kitchen, auth guard

Status: ready-for-agent

Source: `../spec.md` (user stories 30, 42, 100) และ `../plan.md` (Step 3 (auth, errors), Step 11, Step 16 (ส่วน login)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame l1, l2 ใน `../../design/out/`

## What to build

เจ้าของร้านเข้าสู่ระบบด้วยอีเมล/รหัสผ่านและออกจากระบบได้ บัญชีครัวเข้าสู่ระบบได้และถูกจำกัดให้เข้าได้เฉพาะหน้าจอครัว ผู้ที่ไม่ได้ล็อกอินเปิดหน้าหลังร้านไม่ได้ (ถูกส่งไป login) บัญชี Kitchen ที่พยายามเข้าหน้า owner ถูกส่งไปจอครัว ฝั่ง API ตอบ 401 เมื่อไม่ล็อกอินและ 403 เมื่อสิทธิ์ไม่พอ (ไม่ redirect) ด้วย cookie auth ของ ASP.NET Identity มี policy `OwnerOnly` และ `KitchenAccess` บัญชีเริ่มต้นสร้างจาก config (ยังไม่มี Demo)

## Blocked by

01 Walking skeleton

## Acceptance criteria

- [ ] login ถูกต้องได้ cookie และคืน 204; รหัสผิดได้ข้อความภาษาไทยจาก ProblemDetails; logout ล้าง session; `me` คืน role
- [ ] เรียก `me` โดยไม่ล็อกอิน → 401 (ไม่ redirect) (เทสต์ seam 1); การพิสูจน์ 403 ของ Kitchen บน endpoint `OwnerOnly` ทำใน ticket 03 (endpoint OwnerOnly ตัวแรก) แต่ policy `OwnerOnly` และ `KitchenAccess` ต้องลงทะเบียนใน ticket นี้
- [ ] หน้า login ตรงกับ mockup l1 (เดสก์ท็อป) และ l2 (มือถือ) ยกเว้นส่วน Demo ซึ่งทำใน ticket 24; guard ฝั่ง frontend ทำตาม spec (ไม่ login → login, Kitchen เข้า owner → redirect ไปครัว)
- [ ] ProblemDetails ใช้ field `code` คงที่ + `message` ภาษาไทยที่แสดงตรง ๆ ได้ ตาม spec; มี layout หลังร้าน (sidebar ตาม mockup) ที่ guard ใช้ป้องกัน
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
