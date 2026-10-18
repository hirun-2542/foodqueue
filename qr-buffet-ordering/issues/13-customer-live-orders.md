# 13: รายการของโต๊ะฝั่งลูกค้าแบบ real-time ออฟไลน์/reconnect และเมนูหมดทันที

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 11, 20, 21, 22, 25, 26) และ `../plan.md` (Step 12 (orders tab, hub client)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame c3 ใน `../../design/out/`

## What to build

ลูกค้าเปิดแท็บ "ออเดอร์" เห็นรายการทั้งหมดที่โต๊ะสั่งรวมเป็นรายการเดียว (รวมที่เพื่อนสั่ง จำนวนรวมต่อเมนู หมายเหตุ รูปเล็ก ไม่แยกรอบ ไม่มีสถานะ ไม่มียอดบิล) ที่อัปเดตเองเมื่อเพื่อนส่งออเดอร์หรือร้านแก้/ยกเลิกรายการ (รายการที่ถูกยกเลิกหายไป) ป้าย "หมด" อัปเดตทันทีเมื่อร้านสลับ (`menuItemChanged`) หน้าจอแสดงสถานะ "ออฟไลน์" เมื่อการเชื่อมต่อหลุดและ refetch เองเมื่อกลับมา

## Override ของ plan.md

plan.md บรรทัด 208 (Tab ออเดอร์พร้อม badge สถานะ และ Tab ยอดรวม) ถูกแทนด้วยรายการรวมที่ไม่มีสถานะ/ยอด

## Blocked by

12 ครัวเลื่อนสถานะ เริ่มทำ/พร้อมเสิร์ฟ และยกเลิกพร้อมเหตุผล

## Acceptance criteria

- [x] เครื่องลูกค้าสองเครื่องของ token เดียวกัน: เครื่อง A ส่งออเดอร์ → เครื่อง B เห็นในแท็บออเดอร์โดยไม่รีเฟรช; ตะกร้าของแต่ละเครื่องแยกกัน
- [x] ออเดอร์ที่ถูกยกเลิกไม่ปรากฏในรายการของลูกค้า; `orderStatusChanged` ไม่ถูกส่งถึงลูกค้า (เทสต์ seam 1) และหน้าจอลูกค้าไม่มีสถานะรายรอบ
- [x] Owner สลับเมนูหมด → `menuItemChanged` ถึงกลุ่ม menu และหน้าลูกค้าเปลี่ยนเป็นสีเทาพร้อมป้ายหมดโดยไม่รีเฟรช (เทสต์ seam 1 + smoke)
- [x] ตัดเน็ตแล้วเห็น "ออฟไลน์"; ต่อกลับแล้วข้อมูลถูกต้องโดยไม่ต้องรีเฟรชหน้า
- [x] หน้าตรงกับ mockup c3
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 13 — รายการของโต๊ะฝั่งลูกค้าแบบ real-time ออฟไลน์/reconnect และเมนูหมดทันที

Resolution: the customer's orders tab shows the whole table's items in one list with no rounds, no status and
no bill, updates live, and survives a dropped connection.

Evidence:

- `GET /api/s/{token}/orders` collapses the table's orders into one list per (item, note), ordered by when each
  was first ordered, each row carrying the menu item's image. Verified through seam-1 tests that assert the
  response body, not the query.
- Aggregation proven: two orders of the same item sum into a single row, while a line carrying a note stays on
  its own row (which is what the `c3-orders.png` frame shows); a cancelled order's lines disappear, as do
  lines that were removed or reduced to zero.
- The payload's keys are exactly `items` and per row `menuItemId, name, kind, quantity, note?, imagePath?` —
  there is no status, no round number, no price-and-total, and therefore no bill a customer could read. A
  test asserts the absence of those keys rather than trusting the mapper.
- Token scoping: one token never sees another table's items; an unknown token gives `SESSION_NOT_FOUND` and a
  paid session gives `SESSION_CLOSED`.
- Sold-out updates are instant: the availability `PATCH` (and the `PUT`, but only when availability or active
  state actually changed) now emits `menuItemChanged`, and a test proves a customer connection and a kitchen
  connection both receive it while a rename-only edit emits nothing.
- A two-phone smoke run on a private stack: phone B showed the table list growing within ~100 ms of phone A
  submitting, with no reload; the two phones' carts stayed separate (`fq-cart-{token}`); cancelling an order
  through the kitchen reduced B's list again; flipping an item to unavailable greyed B's row with `หมด`
  instantly; and after the connection was cut B showed `ออฟไลน์ · ข้อมูลอาจไม่ใหม่` and then re-synced itself
  on reconnect with no page navigation.
- Screen compared against `design/out/c3-orders.png` at the same 780×1688 size: a pixel diff found only text
  anti-aliasing and font-width differences — rows, card, thumbnails, header, `รวม N รายการ` and the tab bar all
  line up.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 167, Skipped: 0, Total: 167` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
