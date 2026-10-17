# 02: Login, roles Owner/Kitchen, auth guard

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 30, 42, 100) และ `../plan.md` (Step 3 (auth, errors), Step 11, Step 16 (ส่วน login)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame l1, l2 ใน `../../design/out/`

## What to build

เจ้าของร้านเข้าสู่ระบบด้วยอีเมล/รหัสผ่านและออกจากระบบได้ บัญชีครัวเข้าสู่ระบบได้และถูกจำกัดให้เข้าได้เฉพาะหน้าจอครัว ผู้ที่ไม่ได้ล็อกอินเปิดหน้าหลังร้านไม่ได้ (ถูกส่งไป login) บัญชี Kitchen ที่พยายามเข้าหน้า owner ถูกส่งไปจอครัว ฝั่ง API ตอบ 401 เมื่อไม่ล็อกอินและ 403 เมื่อสิทธิ์ไม่พอ (ไม่ redirect) ด้วย cookie auth ของ ASP.NET Identity มี policy `OwnerOnly` และ `KitchenAccess` บัญชีเริ่มต้นสร้างจาก config (ยังไม่มี Demo)

## Blocked by

01 Walking skeleton

## Acceptance criteria

- [x] login ถูกต้องได้ cookie และคืน 204; รหัสผิดได้ข้อความภาษาไทยจาก ProblemDetails; logout ล้าง session; `me` คืน role
- [x] เรียก `me` โดยไม่ล็อกอิน → 401 (ไม่ redirect) (เทสต์ seam 1); การพิสูจน์ 403 ของ Kitchen บน endpoint `OwnerOnly` ทำใน ticket 03 (endpoint OwnerOnly ตัวแรก) แต่ policy `OwnerOnly` และ `KitchenAccess` ต้องลงทะเบียนใน ticket นี้
- [x] หน้า login ตรงกับ mockup l1 (เดสก์ท็อป) และ l2 (มือถือ) ยกเว้นส่วน Demo ซึ่งทำใน ticket 24; guard ฝั่ง frontend ทำตาม spec (ไม่ login → login, Kitchen เข้า owner → redirect ไปครัว)
- [x] ProblemDetails ใช้ field `code` คงที่ + `message` ภาษาไทยที่แสดงตรง ๆ ได้ ตาม spec; มี layout หลังร้าน (sidebar ตาม mockup) ที่ guard ใช้ป้องกัน
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 02 — Login, roles Owner/Kitchen, auth guard

Resolution: endpoints, role policies, account provisioning, staff shell and both front-end guards are in
place and were proven first through seam 1, then through the real browser against the compose stack.

Evidence:

- `POST /api/auth/login` with a seeded credential answers **204** and sets the `fq_auth` cookie; a wrong
  password answers **400** with `{"code":"VALIDATION_FAILED", "message":"อีเมลหรือรหัสผ่านไม่ถูกต้อง",
  ...}` and the UI shows that Thai text in its alert area rather than a generic error. `POST /api/auth/logout`
  clears it, after which `GET /api/auth/me` is **401** again. `me` returns
  `{"email":"owner@demo.local","displayName":"เจ้าของร้าน","role":"Owner"}`.
- Seam 1 (`backend/tests/FoodQueue.Api.Tests/AuthTests.cs`, `cd backend && dotnet test` →
  `Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11`): unauthenticated `me` → 401 with no
  `Location` header (a status, not a redirect); the `owner`/`kitchen` credentials provision exactly once
  even when the app starts twice against the same database; Kitchen is **403** on an `OwnerOnly` endpoint
  and **200** on a `KitchenAccess` one, Owner is 200 on both. The `OwnerOnly` endpoint used for that check
  is registered from the test project itself, so the policy is exercised without inventing product surface.
- The `OwnerOnly` and `KitchenAccess` policies are registered in `Program.cs`; the first real `OwnerOnly`
  endpoint (settings) arrives with ticket 03, which is exactly where the ticket defers its 403 proof.
- Front end: `src/app/(staff)/layout.tsx` sends an unauthenticated visitor to `/login` and a Kitchen
  account out of `/owner/*` to `/kitchen`, driven by the `role` from `me`; the shell renders no protected
  screen until that answer is known, so nothing flashes. Verified in a real browser on the compose stack:
  `pnpm exec playwright test` → `10 passed` (the two guard specs plus the smoke specs, desktop and 390×844);
  a temporary acceptance spec additionally proved `/owner` → `/login` when anonymous, a real form login as
  Owner landing on `/owner`, and the Kitchen account landing on `/kitchen` and being bounced back there from
  `/owner` — `6 passed`, spec deleted afterwards.
- `/login` matches the approved frames: rendered at 1440×900 and 390×844 and compared as images against
  `design/out/l1-login.png` and `l2-login-mobile.png` (brand block, Thai labels, primary button, helper
  line, layout). The demo card visible in `l1` belongs to ticket 24 and is deliberately absent here.
- Accounts come from configuration (`DemoSeed:OwnerEmail` / `KitchenEmail` / `Password`) through an
  idempotent startup task, so ticket 24 can reuse the same accounts instead of inventing another scheme.
