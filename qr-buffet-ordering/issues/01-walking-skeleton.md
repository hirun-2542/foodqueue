# 01: Walking skeleton: compose, single origin, test harness, domain docs

Status: ready-for-agent

Source: `../spec.md` (user stories 111) และ `../plan.md` (Step 0, 1 (และโครงของ Step 2/11)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec

## What to build

ผู้พัฒนารัน `docker compose up` คำสั่งเดียวแล้วเปิด `http://localhost:8080` เห็นหน้าเว็บ (Next.js) และเรียก `/api` กับ `/hubs` ผ่าน origin เดียวกันได้ โดยมี Postgres, API (ASP.NET Core 10 Minimal APIs) และ Caddy ทำงานครบ มี health endpoint ที่ตอบเมื่อ API ต่อฐานข้อมูลได้ migration `Initial` apply เองตอนสตาร์ท และมี harness ทดสอบทั้งสอง seam ของ spec (xUnit + Testcontainers + in-process host; Playwright) พร้อมเทสต์ตัวอย่างที่ผ่าน เอกสารโดเมนที่ spec กำหนด (GLOSSARY และ ADR 2 ฉบับ) ถูกสร้างพร้อมงานนี้

## Blocked by

None (can start immediately)

## Acceptance criteria

- [ ] `docker compose up` จากเครื่องว่างแล้ว `http://localhost:8080` ตอบหน้าเว็บ, `/api/health` ตอบ 200 เมื่อฐานข้อมูลพร้อม, และ `/hubs` ผ่าน Caddy รับ WebSocket ได้ (พิสูจน์ด้วยการเชื่อมต่อจริง)
- [ ] มีโปรเจกต์ API เดียวกับโปรเจกต์ทดสอบเดียว แบ่งโมดูลตามฟีเจอร์ตาม spec; เงินเป็น decimal(10,2), เวลาเป็น UTC timestamptz, enum เก็บเป็น string
- [ ] ข้อตกลง migration: มี migration ชื่อ `Initial` ตัวเดียว และ ticket ถัดไปต้อง regenerate (ลบแล้วสร้างใหม่) แทนการสะสม migration จนกว่าจะมีข้อมูลที่ต้องรักษา; ข้อตกลงนี้เขียนไว้ใน README
- [ ] เทสต์ backend หนึ่งเคสผ่านบน Postgres จริงผ่าน Testcontainers (ปิด Demo ในเทสต์) และเทสต์ Playwright หนึ่งเคสเปิดหน้าแรกผ่าน Caddy ผ่าน
- [ ] สร้าง GLOSSARY.md ตามรายการคำใน spec (หัวข้อ 'เอกสารโดเมน') และ ADR 2 ฉบับ (QR ต่อ session แทน QR ถาวร; single origin ผ่าน Caddy) โดยไม่ใส่รายละเอียด implementation
- [ ] README บอกวิธีรัน ทดสอบ และตั้ง base URL ของ QR เป็น IP ใน LAN (ไม่ใช่ localhost) สำหรับสแกนด้วยมือถือจริง
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
