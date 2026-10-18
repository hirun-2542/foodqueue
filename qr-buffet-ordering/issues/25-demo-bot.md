# 25: Bot จำลองลูกค้าสั่งอาหารและปิดบิลเอง

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 107, 110) และ `../plan.md` (Step 10 (bot)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec

## What to build

เมื่อเปิด Demo มี bot วนทุก 20–40 วินาที ดูแลให้มี session ของ bot เปิดอย่างน้อย 3 โต๊ะ สั่งอาหาร 2–6 รายการ เลื่อนสถานะออเดอร์ที่ค้างเกิน 120 วินาที (ถึง Ready) และจ่ายเงินสดให้ session ที่หมดเวลาและไม่มีออเดอร์ Pending/Preparing ผ่านบริการชุดเดียวกับที่ endpoint ใช้ (กฎและ event เหมือนของจริง) แตะเฉพาะ session ที่ถูกทำเครื่องหมายเป็นของ bot error ในรอบหนึ่งต้อง log แล้วทำรอบถัดไปต่อ

## Blocked by

24 Demo Mode

## Acceptance criteria

- [x] เรียกผ่านบริการชุดเดียวกัน ไม่เขียนฐานข้อมูลตรง (เทสต์ seam 1: ได้ event ครบ)
- [x] ไม่แตะ session ที่ไม่ใช่ของ bot (โต๊ะที่คนจริงเปิด) และเปิดโต๊ะที่คนจริงเปิดอยู่ไม่ได้
- [x] รอบที่เกิดข้อผิดพลาดถูก log และรอบถัดไปทำงานต่อ; ปิด Demo แล้วไม่มี bot
- [x] ในการรันจริง เห็นการ์ดจากบอทเข้าจอครัวและป๊อปอัป owner (ticket 15)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 25 — Bot จำลองลูกค้าสั่งอาหารและปิดบิลเอง

Resolution: with Demo on, a simulated customer keeps rounds open, orders, moves them through the kitchen and
pays them — entirely through the same services the API uses, so its behaviour is indistinguishable from a real
round except that it only ever touches its own. Building it surfaced a real startup race that is now fixed.

Evidence:

- **Same services, not direct writes.** Every cycle action goes through `SessionService.OpenAsync`, the order
  creation path, the workflow transitions, and `PaymentService.PayAsync`, so the validation order, the bill
  arithmetic, the receipt sequence and the live events are the product's own. A test asserts an `orderCreated`
  was emitted (recording notifier) and that the rows it produces obey the same rules.
- **It only touches its own rounds.** Rounds it opens are marked as bot rounds. Tests prove a human's expired
  round with a stale order is neither advanced nor paid, that it cannot take a table a human holds (four tables
  held by humans leaves the bot only two), and — a deliberate decision — that it never touches a round that was
  merged into someone else's bill, because advancing or paying that would disturb another bill's
  "orders in progress"; it opens a fresh round instead of counting that one towards its three.
- **A cycle is one method**, `RunCycleAsync`, so the tests drive the whole cycle synchronously without a single
  sleep; the 20–40 s loop is only `Task.Delay` around it. The cycle reads the clock once, judges staleness from
  each order's own `UpdatedAt` and expiry from the round's `EndsAt` — both set through data by the tests, the
  same way every other test in the suite handles time. Its `Random` is seeded (2025, distinct from the seeder's
  42) and injectable, and it enumerates tables and rounds by ordering rather than by drawing random numbers, so
  a cycle is reproducible.
- **Failure is contained:** a deliberately flaky notifier makes one cycle throw; the error is logged at Error
  level naming the step, the remaining steps of that cycle still run, and the next cycle places orders
  normally. With Demo off the bot does not run at all, asserted both through the switch and by the loop's own
  guard.
- **Observed live in a browser** (private stack: Caddy plus a real API and Next build, Demo on): the kitchen
  board showed eleven bot rounds with real items (`A3 รอบที่ 3 · ส่ง 12:12 ×1 โค๊ก ×5 น้ำอัดลม`) and their cards
  moved รอรับ → กำลังทำ → พร้อมเสิร์ฟ **without a reload**; the owner's new-order popup from ticket 15 fired on
  its own with the real copy (`มีออเดอร์ใหม่ โต๊ะ A1 รอบที่ 5 · ส่ง 12:17 Gold · 2 ผู้ใหญ่ 1 เด็ก · 6 ชิ้น` plus
  `ยังมีออเดอร์รอรับอีก 3 รอบ`) and a chime; and the bot closed a round in cash with a receipt
  (`R20261018-000940`, total = received = ฿2,562.33, change 0.00) which then showed on the overview KPI.
- **Startup race found and fixed (a real defect, not a test artefact).** On the first real run the API failed
  with `TABLE_OCCUPIED`: the bot began working while the demo seeder was still writing history, because the
  startup-task list is awaited as a registration rather than as a hosted service, so the bot's own hosted
  service started immediately and the two competed for tables. The bot now starts on the application-started
  event — which is guaranteed to be after the seeder — and stops on application-stopping. It had to register
  its hosted service from its own feature file, because `Program.cs` is frozen and offers no hook for that;
  this is reported rather than hidden.
- No schema change was needed: the bot-round marker already exists, so `Initial` is untouched.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 246, Skipped: 0, Total: 246` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
