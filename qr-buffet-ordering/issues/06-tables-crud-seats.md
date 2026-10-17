# 06: จัดการโต๊ะ: เพิ่ม แก้ จำนวนที่นั่ง โซน ปิดใช้งาน

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 52) และ `../plan.md` (Step 4 (Table)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o22; ผังพื้นฐานของ o12 ทำใน ticket 08 ใน `../../design/out/`

## What to build

เจ้าของร้านเพิ่มและแก้โต๊ะ (ชื่อไม่ซ้ำ, จำนวนที่นั่ง 1–20, โซน) และปิดใช้งานโต๊ะผ่านโหมดแก้ไขผัง ปุ่ม × ลบ/ปิดใช้งานอยู่เฉพาะโต๊ะว่าง ตัวนับที่นั่งต่อโต๊ะมี −/+ และฟอร์มตั้งค่าโต๊ะด้านข้าง

## Override ของ plan.md

plan.md Step 4 ไม่มีฟิลด์ที่นั่ง/โซน — spec เพิ่มแล้ว ยึด spec

## Blocked by

02 Login, roles Owner/Kitchen, auth guard

## Acceptance criteria

- [x] ที่นั่งนอก 1–20 หรือชื่อโต๊ะซ้ำ → `VALIDATION_FAILED` (เทสต์ seam 1)
- [x] โหมดแก้ไขผังตรงกับ mockup o22 (stepper ที่นั่ง, ปุ่ม × เฉพาะโต๊ะว่าง, ช่องเพิ่มโต๊ะเส้นประ, แผงฟอร์มชื่อ/ที่นั่ง/โซน/ลบ) โดยโต๊ะว่างทุกใบเพราะยังไม่มี session
- [x] กฎ 'ปิดใช้งานโต๊ะที่มี session Open ไม่ได้' พิสูจน์ใน ticket 07 เมื่อมี session
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 06 — จัดการโต๊ะ: เพิ่ม แก้ จำนวนที่นั่ง โซน ปิดใช้งาน

Resolution: tables can be created, edited and deactivated by the owner, seats and names are validated in
Thai, and disabling is a soft state change with no destructive route. The floor map itself (live occupancy)
belongs to ticket 08 and was deliberately not implemented.

Evidence:

- Round-trip create/read/update works, including a rename onto a table's own name (accepted) and onto
  another table's name (refused).
- Seats outside 1–20 (`0`, `-3`, `21`, `99`) answer **400** `VALIDATION_FAILED` on both POST and PUT with a
  Thai message, and a failed PUT does not overwrite the stored row; the boundaries 1 and 20 are accepted. A
  duplicate name answers **400** `VALIDATION_FAILED` with `ชื่อโต๊ะ` in the message. Two concurrent creates of
  the same name produce exactly one 201 and one 400 (the database unique index is caught as `23505`).
- Disable is `IsActive=false`: the row remains, is still listable, holds its data and can be re-enabled.
  Disabling a table that has an Open session is refused with **409** `TABLE_OCCUPIED` — the rule the ticket
  defers to this point. `DELETE /api/tables/{id}` answers **405** and the row survives, asserted in a test.
  `GET /api/tables/overview` also answers **405**, proving ticket 08's endpoint was not stubbed with fake data.
- Authorization: anonymous **401**, Kitchen **403** on all three verbs, with no database effect.
- Screen compared against `design/out/o22-tables-edit.png` at 1440×900: the seat stepper on every tile, the ×
  control only on free tiles, the dashed `เพิ่มโต๊ะ / วางในผัง` slot, the zone blocks (โซน A then โซน B), the
  ครัว band and หน้าต่าง / ทางเข้า / แคชเชอร์ markers, the selected-tile outline, and the side form with
  `ชื่อโต๊ะ`, `จำนวนคนต่อโต๊ะ (ที่นั่ง)` and its large −/4/+ stepper, `โซน`, `ลบโต๊ะ A1`, and the
  `ลบได้เฉพาะโต๊ะที่ว่าง` hint all match, with Thai wording taken verbatim from the approved frames.
- Browser round-trip through the real UI changed A1 to 6 seats and disabled A2, confirmed in the database
  (12 active tables / 50 seats after restoring), proving the screen drives the API rather than a mock.
- Differences from the frame, all consequences of data this ticket cannot see: the occupancy legend reads
  `ใช้งานอยู่ 0 / ว่าง 12` and every tile is the free grey fill, because which tables have guests comes from
  ticket 08's overview endpoint; and the sidebar brand shows `FoodQueue / ระบบสั่งอาหารผ่าน QR` with only the
  routes that exist. The frame's `ลากโต๊ะเพื่อย้ายตำแหน่ง` hint was not implemented because the spec fixes
  positions by zone and order with no drag-and-drop.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 60, Skipped: 0, Total: 60` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
