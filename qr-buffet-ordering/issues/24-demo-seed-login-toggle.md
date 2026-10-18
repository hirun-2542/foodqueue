# 24: Demo Mode: seed, ปุ่มเข้าใช้ทันที และสวิตช์ปิด

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 104, 105, 106, 109) และ `../plan.md` (Step 10 (seed, endpoints), Step 16 (demo buttons)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame l1, l2 ใน `../../design/out/`

## What to build

ผู้ชม portfolio กดปุ่มเดียวเข้าใช้เป็นเจ้าของร้านหรือครัวโดยไม่พิมพ์รหัสผ่าน เปิดมุมมองลูกค้าที่เป็นโต๊ะจริงในระบบ และเห็นรายงานประวัติ 30 วันตั้งแต่เปิดครั้งแรก Demo เปิด/ปิดด้วย config เดียว เมื่อปิด endpoint ทั้งหมดของ demo ตอบ 404 และไม่มี seed/bot Seed กำหนดซ้ำได้ (random seed คงที่) และ idempotent เมื่อฐานข้อมูลว่าง: ผู้ใช้ owner/kitchen demo, ร้านตัวอย่าง (SC 10%, VAT 7%, PromptPay ตัวอย่าง), โต๊ะ 12 โต๊ะ (โซน A, B; 4 ที่นั่งยกเว้น B3 6 ที่นั่ง), แพ็กเกจ 3 ระดับ, เมนูราว 40 รายการใน 8 หมวด, ประวัติ 30 วันย้อนหลัง (ไม่รวมวันนี้) วันละ 20–45 บิล เวลา 11:00–21:30 สัดส่วน Cash 40/PromptPay 45/Card 15 และรายการปรับยอดราว 10%

## Blocked by

23 รายงานยอดขายและ KPI หน้าภาพรวม

## Acceptance criteria

- [x] Demo ปิด → ทุก endpoint demo 404 (เทสต์ seam 1); seed รันซ้ำไม่สร้างข้อมูลซ้ำ
- [x] seed สร้างข้อมูลผ่านบริการชุดเดียวกับ API (ไม่เขียนฐานข้อมูลตรง) ตัวเลขตามช่วงที่ spec กำหนด
- [x] ปุ่ม "เข้าใช้แบบเจ้าของร้าน/ครัว/ดูมุมมองลูกค้า" ใน l1/l2 ทำงานจริง และรายงาน 30 วัน (ticket 23) มีข้อมูลทันทีหลังเปิดครั้งแรก
- [x] ส่วน bot และ split-screen ไม่อยู่ใน ticket นี้ (25, 26)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 24 — Demo Mode: seed, ปุ่มเข้าใช้ทันที และสวิตช์ปิด

Resolution: one switch turns Demo on and off, one click signs a visitor in as the owner or the kitchen, and
the first load already carries 30 days of believable sales history. The bot and the split-screen page are
ticket 25's and 26's and were deliberately not built here.

Evidence:

- `GET /api/demo/status` reports enabled/disabled without authentication, and with Demo off **all three demo
  endpoints answer 404** — asserted by tests for the endpoints themselves rather than by not mapping them.
  `POST /api/demo/login` signs in the matching account with no password and returns the right role;
  `POST /api/demo/customer-session` returns a usable token for an Open round, opens a Gold round on a free
  table when none exists, and refuses with `TABLE_OCCUPIED` when no table is free. In a real browser all three
  buttons worked: owner → `/owner`, kitchen → `/kitchen`, customer → `/s/<43-char token>`, and
  `/owner/reports?range=30d` showed data immediately. With Demo off the card is absent entirely and the
  endpoints 404.
- The seed is deterministic (fixed `Random(42)`) and idempotent: running it again creates nothing. It only
  seeds when the table catalogue is empty. Verified against a real Postgres:
  **12 tables / 50 seats (B3 has 6), 3 packages, 8 categories, 44 menu items (36 buffet, 8 à la carte),
  1,015 paid rounds across 30 days excluding today, 3,054 orders, 98 bills with an adjustment (9.7%),
  1,015 payments, 1,015 unique receipts** in the frozen format, with 20–45 bills per day (average 33.8),
  opening windows inside 11:00–21:30 Bangkok, and a payment mix of cash 40.6% / PromptPay 44.2% / Card 15.2% —
  the spec's 40/45/15. Every seeded round is marked as a bot round.
- The history is created **through the product's own services** — open the round, place orders, advance and
  cancel them, adjust the bill, then pay it — so the validation order, the bill arithmetic and the receipt
  sequence remain the product's. Only two things were written directly, and the reason is stated rather than
  hidden: the catalogue (settings/tables/packages/categories/menu items), because those modules hold their
  own `DbContext` in their endpoint files and offer no shared service, and the back-dating of instants
  (`OpenedAt/EndsAt/ClosedAt/PaidAt` and order times), because every service reads the real clock and
  `Program.cs` is frozen so no injectable clock could be added. Money, statuses and receipt numbers all still
  come from the services.
- The seeded history feeds ticket 23 correctly: `today` 0 bills (right, because the seed excludes today),
  `7d` ฿400,955.80 over 204 bills, `30d` ฿1,869,934.64 over 978 bills with the package and payment mixes
  summing to the same revenue. The 37-bill difference from the total is the report's "today + 29 previous days"
  window versus the seed's 30 previous days, and a test pins that difference.
- Screens compared against `design/out/l1-login.png` and `l2-login-mobile.png`: the demo card with its DEMO
  MODE tag, the owner button filled against the outline kitchen/customer buttons, the adjusted button heights
  for desktop and mobile, the `หรือ` divider on mobile, and the `ดูทั้ง 3 มุมมองพร้อมกัน` link in the approved
  colour; at 390×844 there is no horizontal scroll. Two deliberate differences: the frame's "recent bot
  activity" feed is absent (that is ticket 25's live output, and inventing it would be a fake), and the brand
  still reads `FoodQueue` rather than the seeded shop name.
- No schema change: `TableSession.IsDemoBot` already existed, so `Initial` is untouched.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 236, Skipped: 0, Total: 236` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- Process note: this ticket's first attempt wrote into the **main checkout** instead of its worktree (the
  relative-path hazard, already observed three times in this session). The controller relocated the work into
  the worktree and the agent confirmed `pwd` and a clean main checkout afterwards.
