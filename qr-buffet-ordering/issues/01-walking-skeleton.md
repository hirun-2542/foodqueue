# 01: Walking skeleton: compose, single origin, test harness, domain docs

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 111) และ `../plan.md` (Step 0, 1 (และโครงของ Step 2/11)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec

## What to build

ผู้พัฒนารัน `docker compose up` คำสั่งเดียวแล้วเปิด `http://localhost:8080` เห็นหน้าเว็บ (Next.js) และเรียก `/api` กับ `/hubs` ผ่าน origin เดียวกันได้ โดยมี Postgres, API (ASP.NET Core 10 Minimal APIs) และ Caddy ทำงานครบ มี health endpoint ที่ตอบเมื่อ API ต่อฐานข้อมูลได้ migration `Initial` apply เองตอนสตาร์ท และมี harness ทดสอบทั้งสอง seam ของ spec (xUnit + Testcontainers + in-process host; Playwright) พร้อมเทสต์ตัวอย่างที่ผ่าน เอกสารโดเมนที่ spec กำหนด (GLOSSARY และ ADR 2 ฉบับ) ถูกสร้างพร้อมงานนี้

## Blocked by

None (can start immediately)

## Acceptance criteria

- [x] `docker compose up` จากเครื่องว่างแล้ว `http://localhost:8080` ตอบหน้าเว็บ, `/api/health` ตอบ 200 เมื่อฐานข้อมูลพร้อม, และ `/hubs` ผ่าน Caddy รับ WebSocket ได้ (พิสูจน์ด้วยการเชื่อมต่อจริง)
- [x] มีโปรเจกต์ API เดียวกับโปรเจกต์ทดสอบเดียว แบ่งโมดูลตามฟีเจอร์ตาม spec; เงินเป็น decimal(10,2), เวลาเป็น UTC timestamptz, enum เก็บเป็น string
- [x] ข้อตกลง migration: มี migration ชื่อ `Initial` ตัวเดียว และ ticket ถัดไปต้อง regenerate (ลบแล้วสร้างใหม่) แทนการสะสม migration จนกว่าจะมีข้อมูลที่ต้องรักษา; ข้อตกลงนี้เขียนไว้ใน README
- [x] เทสต์ backend หนึ่งเคสผ่านบน Postgres จริงผ่าน Testcontainers (ปิด Demo ในเทสต์) และเทสต์ Playwright หนึ่งเคสเปิดหน้าแรกผ่าน Caddy ผ่าน
- [x] สร้าง GLOSSARY.md ตามรายการคำใน spec (หัวข้อ 'เอกสารโดเมน') และ ADR 2 ฉบับ (QR ต่อ session แทน QR ถาวร; single origin ผ่าน Caddy) โดยไม่ใส่รายละเอียด implementation
- [x] README บอกวิธีรัน ทดสอบ และตั้ง base URL ของ QR เป็น IP ใน LAN (ไม่ใช่ localhost) สำหรับสแกนด้วยมือถือจริง
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 01 — Walking skeleton

Resolution: all seven acceptance items are demonstrated on the running stack; the two items the ticket
splits into separate checks (container reachability, WebSocket path) were proved with real clients, not by
inspection.

Evidence, in the order of the acceptance list:

- `docker compose up --build -d` from the repo root brings up `db`, `api`, `web`, `caddy`.
  `docker compose ps` → `db Up (healthy) · api Up (healthy) · web Up · caddy Up`;
  `http://localhost:8080/api/health` → `{"status":"ok"}`;
  `http://localhost:8080/login` renders the Next.js login screen through Caddy.
- `/hubs/live` accepted a real WebSocket through Caddy: a throwaway Node probe using the app's own
  `@microsoft/signalr` client reported `{"ok":true,"state":"Connected","transport":"WebSocketTransport"}`.
  The probe was deleted after the run.
- Shape of the repo: one API project `backend/src/FoodQueue.Api` (feature modules behind
  `IEndpointModule`) plus one test project `backend/tests/FoodQueue.Api.Tests`; money is `decimal(10,2)`,
  time is `DateTimeOffset` in UTC (timestamptz) and enums are persisted as strings — see
  `Data/AppDbContext.cs` (`HasPrecision(10, 2)`, `HasConversion<string>()`).
- Migration convention is written in `README.md` under "ข้อตกลง migration": a single `Initial`
  migration, regenerated rather than accumulated until real data exists.
- Seam 1 demonstrably runs on a real Postgres: `cd backend && dotnet test` →
  `Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11, Duration: 3 s` on a Testcontainers
  `postgres:17` with `Demo:Enabled=false`.
- Domain docs exist: `GLOSSARY.md` (15 terms from the spec's list) and `docs/adr/0001-session-scoped-qr.md`,
  `docs/adr/0002-single-origin-caddy.md`, both one paragraph of rationale with no implementation detail.
- `README.md` documents how to run, how to test both seams, and that `PUBLIC_BASE_URL` must be set to a
  LAN IP (not localhost) for a real phone to scan the QR.
- Layer coverage: schema (`Initial` migration creates 18 tables, `receipt_no_seq` and the partial unique
  index `IX_TableSessions_TableId ... WHERE Status = 'Open'` — all confirmed live via `psql`), API
  (`/api/health` + hub), UI (`/login` matching `design/out/l1-login.png` and `l2-login-mobile.png`, both
  captured and compared as images); seam-2 smoke: `pnpm exec playwright test` → `10 passed`.
- Note: `application NOT RUN` does not apply to this ticket — the whole stack was exercised.

Note on the ticket's own wording: it says "เทสต์ Playwright หนึ่งเคสเปิดหน้าแรกผ่าน Caddy" — that is
`frontend/e2e/smoke.spec.ts`, which passes at both viewports; the suite has since grown to 10 cases
across tickets 01–02.
