# 08: ผังโต๊ะและภาพรวมโต๊ะ: สองสถานะ เวลาเริ่ม–สิ้นสุด นาทีที่ใช้ไป

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 43, 48) และ `../plan.md` (Step 5 (overview), Step 14 (`/owner`)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o12, o01, o16 ใน `../../design/out/`

## What to build

เจ้าของร้านเห็นผังโต๊ะทั้งหมดในสองสถานะ (ใช้งานอยู่ / ว่าง) ไม่มีการกะพริบ เลือกโต๊ะแล้วเห็นแผงข้อมูล (แพ็กเกจ จำนวนคน เวลาเริ่ม–สิ้นสุด ใช้ไปแล้วกี่นาที ออเดอร์ค้างครัวกี่รอบ ยอดสะสม) และหน้าภาพรวมมีการ์ดของทุกโต๊ะที่แสดงเวลาเริ่ม–สิ้นสุด (`18:55–20:25`) นาทีที่ใช้ไป (`ใช้ไป 47 นาที`) ป้าย Last order เมื่อเหลือไม่เกินค่าที่ตั้ง และป้าย "ปิดการสั่งแล้ว" เมื่อเลย EndsAt ไม่มีตัวนับถอยหลัง ข้อมูลมาจาก overview endpoint (Kitchen เรียกได้) และอัปเดตเองเมื่อ session เปลี่ยน

## Override ของ plan.md

plan.md บรรทัด 217 (`/owner` มีการ์ดเวลา/นับถอยหลัง) ถูกแทนด้วยเวลาเริ่ม–สิ้นสุด + นาทีที่ใช้ไป (ผู้ใช้ตัดสินแล้ว)

## Blocked by

07 เปิดโต๊ะ + QR เฉพาะ session + ยกเลิกโต๊ะ (void)

## Acceptance criteria

- [x] ผังและการ์ดตรงกับ mockup o12 และ o01 (การ์ดภาพรวม) และมือถือ o16; ผังมีเพียงสองสี ไม่มีแอนิเมชัน; โต๊ะที่มีออเดอร์รอรับยังเป็นสีใช้งานอยู่และมีบรรทัด "ออเดอร์รอรับ" (ผูกกับข้อมูลจริงเมื่อมีออเดอร์ใน ticket 10/11)
- [x] overview คืนเวลาเริ่ม/สิ้นสุดและนาทีที่ใช้ไปคำนวณจากเวลาเซิร์ฟเวอร์ ไม่เรียกเวลาปัจจุบันระหว่าง render; เทสต์ด้วยการตั้ง EndsAt เป็นอดีต (ไม่ sleep) ว่าสถานะ 'ปิดการสั่งแล้ว' ปรากฏ
- [x] เปิดโต๊ะจาก ticket 07 แล้วผังและการ์ดเปลี่ยนเป็น 'ใช้งานอยู่' (ใช้ refetch เป็นระยะจนกว่า ticket 11 จะมี hub; ticket 11 เปลี่ยนเป็น event)
- [x] ส่วน KPI ของหน้าภาพรวมไม่อยู่ใน ticket นี้ (ticket 23)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 08 — ผังโต๊ะและภาพรวมโต๊ะ: สองสถานะ เวลาเริ่ม–สิ้นสุด นาทีที่ใช้ไป

Resolution: the owner's overview cards and the floor map were built on a new overview endpoint that
computes the elapsed time on the server, and the whole thing was verified on the running stack.

Evidence:

- `GET /api/tables/overview` (under `KitchenAccess`) returns every active table with its Open session.
  Verified live on the running stack: `A1 | session 1 | Gold | minutes 8 | waiting 3 | total 1226.43`, and
  anonymous access answers **401**. `minutesUsed` is computed server-side at request time; the client never
  derives it from its own clock and never renders `Date.now()`.
- Seam-1 tests (7) cover: a free table reports no session fields; an occupied one reports its package,
  guests, times, minutes and total; `minutesUsed` comes from data (backdated `OpenedAt`, never a sleep);
  `ordersWaiting` excludes both Ready and Cancelled; a session past `EndsAt` still appears (only new ordering
  closes — the round is still open); Kitchen is allowed and anonymous is 401. The running-total assertions
  compare against that session's own `GET /api/sessions/{id}` bill rather than a constant, so the test states
  the real contract (overview total == the bill seam) and does not depend on the shop's SC/VAT at that moment.
- Cards (`design/out/o01-overview.png`, `design/out/o16-owner-mobile.png`) match: the table name with the
  package badge, `2 ผู้ใหญ่ 1 เด็ก`, the `HH:MM–HH:MM` range in tabular figures, `ใช้ไป 8 นาที`, the time
  rail with its mark, the `ค้างครัว 3` chip and `฿1,226.43`, plus the `ทั้งหมด / กำลังใช้ / ว่าง` counters.
- No countdown exists anywhere on either screen: a text audit of the rendered pages found no `เหลือ N`
  pattern and no `H:MM:SS`, and the `Last order` notice is text-only.
- Floor map (`design/out/o12-tables.png`) has exactly **two** states — `ใช้งานอยู่` and `ว่าง` — with the room
  markers (ครัว / หน้าต่าง / ทางเข้า / แคชเชอร์), the seat pip line `3/4`, the `ออเดอร์รอรับ` line on a tile
  that stays in the in-use colour, and the side panel (`แพ็กเกจ`, `ผู้นั่ง`, `เวลาใช้โต๊ะ 07:03–08:33`,
  `ใช้ไปแล้ว 8 นาที`, `ยอดบิล`). An animation audit found **0** animated elements, so nothing pulses on the
  map. Ticket 06's edit mode still works beside it, now with real occupancy.
- Opening a table through ticket 07's flow flips its card from free to in use — confirmed by opening a table
  through the API while `/owner` was open and watching the card change without a reload.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 137, Skipped: 0, Total: 137` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- Deliberately absent here: the KPI band belongs to ticket 23, and the `ดูรายละเอียด` / `ย้ายโต๊ะ` / `รวมบิล`
  actions are inert placeholders that tickets 16/21/22 activate. The overview currently polls every 10 s;
  ticket 11 replaces that with hub events.
