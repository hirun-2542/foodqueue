# 11: จอครัว: ออเดอร์ใหม่เข้าทันที เสียง ไฮไลต์ และตัวนับ

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 31, 32, 33, 34, 35, 36, 40, 41) และ `../plan.md` (Step 3 (hub), Step 7 (list), Step 13); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame k1 ใน `../../design/out/`

## What to build

พนักงานครัวเปิดจอครัว (แท็บเล็ตแนวนอน) บนบอร์ดสามคอลัมน์ รอรับ / กำลังทำ / พร้อมเสิร์ฟ เห็นออเดอร์ใหม่ปรากฏทันทีผ่าน SignalR (hub เดียว กลุ่ม kitchen/owner/session/menu; ส่ง event หลังบันทึกฐานข้อมูลสำเร็จเท่านั้น) พร้อมเสียงเตือนสั้น ๆ ที่ปิดเสียงได้ และการ์ดใหม่ถูกไฮไลต์ชั่วคราว การ์ดแสดงโต๊ะ เลขรอบ รายการ จำนวน หมายเหตุเด่น เวลาที่รอและสีแดงเมื่อเกิน 10 นาที ตัวนับต่อคอลัมน์ และสถานะการเชื่อมต่อ; Owner เข้าจอครัวได้ด้วยบัญชีของตน การเชื่อมต่อ hub ของลูกค้าต้องใช้ token ของ session Open และเข้าได้เฉพาะ group ของตัวเองกับ menu

## Override ของ plan.md

plan.md บรรทัด 141: ชุดสถานะที่ใช้ในรายการเริ่มต้นไม่มี Served อีก (spec: Pending/Preparing/Ready)

## Blocked by

10 ตะกร้า ส่งออเดอร์ ลิมิต เลขรอบ และการปิดการสั่งเมื่อหมดเวลา

## Acceptance criteria

- [x] ลูกค้าส่งออเดอร์ (ticket 10) แล้วการ์ดโผล่ในคอลัมน์ "รอรับ" ภายใน 5 วินาทีโดยไม่รีเฟรช; เทสต์ seam 1 ว่า `orderCreated` ถึงกลุ่ม kitchen และ session ที่ถูกต้องเท่านั้น
- [x] hub ปฏิเสธ token ผิดหรือ session ที่ปิดแล้ว; ลูกค้าไม่ได้รับ event ของโต๊ะอื่น; Staff เข้า group ตาม role (Owner: owner+kitchen+menu, Kitchen: kitchen+menu)
- [x] เมื่อ reconnect ฝั่ง staff refetch ข้อมูลทั้งหมด และแสดงสถานะการเชื่อมต่อ
- [x] จอตรงกับ mockup k1 ยกเว้นปุ่มเลื่อนสถานะที่ทำใน ticket 12; การ์ดใหม่ใช้สี alert และ pulse (เคารพ `prefers-reduced-motion`)
- [x] เปลี่ยนผังและการ์ดภาพรวมจาก ticket 08 ให้รับ `sessionChanged`/`orderCreated` แทนการ refetch เป็นระยะ
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 11 — จอครัว: ออเดอร์ใหม่เข้าทันที เสียง ไฮไลต์ และตัวนับ

Resolution: the kitchen board is live on the single hub, the order list endpoint exists, and the realtime
group rules were proven with real WebSocket clients — which found and fixed three genuine bugs.

Evidence:

- `GET /api/orders?status=&date=` (Owner **and** Kitchen; anonymous **401**) returns `OrderDto`s oldest-first.
  The status filter defaults to Pending/Preparing/Ready and anything else — including a number or `Served` —
  answers **400** `VALIDATION_FAILED` with a Thai message. Without `date` it lists only orders of tables that
  are still open, which is the only way a Ready card can leave the board; with `date` it returns that Bangkok
  day, with a test covering the midnight boundary.
- Live delivery verified with real SignalR connections over WebSockets (kitchen account, owner account, and
  customers on two different tables, each using its real cookie or QR token). To avoid timing luck each
  client sends a barrier message after the event under test, so "did not receive" is proven by ordering, not
  by a sleep: the submitting table's customer, the kitchen and the owner each get the new order exactly once,
  and the other table's customer gets nothing.
- `orderStatusChanged` reaches kitchen and owner and **never** a session group. Ticket 12 owns the emission,
  so this was proven by calling the real notifier directly; as a control, temporarily broadcasting it to
  everyone made the test fail on exactly the customer assertion, and the change was reverted.
- Staff grouping by role: a Kitchen account receives neither `sessionChanged` nor `sessionClosed`; the owner
  receives both. Rejected connections proven as well: a garbage token, a malformed token, no token without a
  login, and both a voided and a paid session are disconnected and receive nothing, while an open session's
  token stays connected.
- Bugs found by those tests and fixed here: owners were receiving every order event **twice** (they belong to
  both the `kitchen` and `owner` groups, and sending to two groups does not de-duplicate) — kitchen+owner
  events now go to the `kitchen` group once; a connection with neither token nor staff login was silently
  accepted and has been made to disconnect; and the cart's server-clock offset had an inverted sign, which
  would have made a phone with a wrong clock show `หมดเวลาสั่งอาหาร` at the wrong moment.
- Board compared against `design/out/k1-board.png` at 1280×800: three columns `รอรับ / กำลังทำ / พร้อมเสิร์ฟ`
  with counters, the `ออเดอร์ค้าง N รอบ · ล่าช้าเกิน 10 นาที M รอบ` summary, the connection badge and the mute
  switch; cards show the table, `รอบที่ N · ส่ง HH:MM`, a wait bar that turns red past 10 minutes, the lines
  with quantities, notes highlighted, and Ready cards reading `รอพนักงานนำไปเสิร์ฟ`. There is no `Served`
  language anywhere and no countdown: wait times come from the server's clock (the response `Date` header)
  and only count up.
- The owner overview was switched from its 10-second poll to `sessionChanged`/`orderCreated`, which is what
  this ticket's acceptance list asks for, so the overview is no longer polling.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 151, Skipped: 0, Total: 151` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- Status buttons and the ⋮ cancel menu are deliberately absent here; `OrderCard` exposes an `actions` prop
  and ticket 12 fills it (it has since merged on top of this work).
- Smoke on the real `docker compose` stack through Caddy, with a Kitchen account signed in at 1280×800:
  the board rendered three live orders in `รอรับ` with their round numbers, wait rows turning red past ten
  minutes (`56 นาที`), per-column counters, the `ออเดอร์ค้าง 3 รอบ · ล่าช้าเกิน 10 นาที 3 รอบ` summary, the
  `เชื่อมต่อแล้ว` badge and the sound switch. A text audit of the rendered page found no `เสิร์ฟแล้ว` and no
  `HH:MM:SS` tick, so there is no countdown and no served step on screen.
