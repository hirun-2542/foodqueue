# 21: ย้ายโต๊ะ: ย้ายทั้ง session ไปโต๊ะว่าง

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 54) และ `../plan.md` (ไม่มีใน plan.md (ใหม่ตาม spec)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o18 ใน `../../design/out/`

## What to build

เจ้าของร้านเลือก "ย้ายโต๊ะ" จากการ์ดออเดอร์ เลือกโต๊ะปลายทางจากโต๊ะว่างเท่านั้น (โต๊ะที่มีลูกค้าเลือกไม่ได้) ระบบย้ายทั้ง session (ออเดอร์ บิล รายการปรับยอด และเวลานั่ง) โดย QR/token เดิมใช้ได้ต่อ โต๊ะเดิมว่างทันที

## Override ของ plan.md

plan.md บรรทัด 119 ('ย้ายโต๊ะและรวมโต๊ะไม่อยู่ในขอบเขต') ถูกยกเลิกสำหรับการย้ายโต๊ะ (ผู้ใช้ตัดสินให้เก็บ)

## Blocked by

14 คิวออเดอร์ของเจ้าของร้าน

## Acceptance criteria

- [x] ย้ายไปโต๊ะที่มี session Open → `TABLE_OCCUPIED`; ย้ายสำเร็จแล้ว token เดิมสั่งได้ และเห็นข้อมูลชุดเดิมที่โต๊ะใหม่ (เทสต์ seam 1)
- [x] โต๊ะเดิมแสดงว่างบนผังทันที และการ์ดครัวแสดงชื่อโต๊ะใหม่ (`sessionChanged`)
- [x] หน้าตรงกับ mockup o18 (ตัวเลือกโต๊ะปลายทาง สรุปสิ่งที่ย้าย) และเปิดจากปุ่ม "ย้ายโต๊ะ" ใน ticket 14
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 21 — ย้ายโต๊ะ: ย้ายทั้ง session ไปโต๊ะว่าง

Resolution: an owner can move a whole round to another free table, and everything that belongs to the round
travels with it while the QR keeps working.

Evidence:

- The move is guarded: an occupied destination, the session's own table, a disabled table and an unknown
  table are all refused, and two simultaneous moves onto the same free table produce exactly one success and
  one `TABLE_OCCUPIED`. The second case is also enforced by the database's partial unique index, whose
  violation is mapped to the same code, so the check cannot be lost to a race. Paid and voided sessions are
  refused with `SESSION_CLOSED`; a session past its `EndsAt` still moves (only new ordering is closed).
- The whole round moves: id, token, `qrUrl`, `openedAt`/`endsAt`, orders, adjustments and the bill stay
  byte-identical apart from the table name. After the move the old table is free and re-openable, the new one
  refuses a second open, the token still orders (a new round lands on the kitchen board under the new table),
  and the customer's header shows the new table name.
- `sessionChanged` reaches the owner and the customer's own session group and nobody else: the kitchen and a
  bystander table receive nothing, and the kitchen's existing cards take the new table name with their status
  unchanged (cancelled rounds are not re-announced).
- Screens: the `ย้ายโต๊ะ` action appears on the owner queue cards and the dialog matches
  `design/out/o18-move-table.png` — only free tiles are selectable, occupied tiles are disabled and show their
  head count, the session's own tile is dashed and marked `โต๊ะเดิม`, submit stays disabled until a pick, and
  the dialog explains what moves. A race on screen was handled: another device opening the destination while
  the dialog was open greyed the tile and disabled submit without closing the dialog. At 390×844 the dialog
  is capped to the viewport and scrolls.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 174, Skipped: 0, Total: 174` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- No schema change was needed: orders, adjustments, bill and times already hang off the session, so only
  `TableId` moves and `Initial` is untouched.
