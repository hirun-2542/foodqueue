# 04: จัดการแพ็กเกจบุฟเฟ่ต์

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 80, 82) และ `../plan.md` (Step 4 (Package)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o11 ใน `../../design/out/`

## What to build

เจ้าของร้านสร้างและแก้แพ็กเกจหลายระดับ (ชื่อ, Rank ไม่ซ้ำ, ราคาผู้ใหญ่, ราคาเด็ก, ระยะเวลาเป็นนาที) และปิดใช้งานแพ็กเกจแทนการลบเพื่อรักษาประวัติการขาย

## Blocked by

02 Login, roles Owner/Kitchen, auth guard

## Acceptance criteria

- [x] สร้างแพ็กเกจ 3 ระดับตามตัวอย่าง (Silver/Gold/Platinum) แล้วเห็นในตารางแพ็กเกจ; Rank ซ้ำถูกปฏิเสธพร้อมข้อความไทย (เทสต์ seam 1 + เห็นใน UI)
- [x] ปิดใช้งานแพ็กเกจแล้วเลือกเปิดโต๊ะไม่ได้ (พิสูจน์ใน ticket 07) และไม่มี endpoint ลบ
- [x] หน้าแพ็กเกจตรงกับ mockup o11 (ตาราง ข้อผิดพลาด rank ชน)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 04 — จัดการแพ็กเกจบุฟเฟ่ต์

Resolution: packages are created, edited and deactivated through an `OwnerOnly` API with no delete path,
and the screen matches the approved frame.

Evidence:

- Three tiers (Silver 299/149/90, Gold 399/199/90, Platinum 549/279/120) can be created and appear in the
  table ordered by rank, both through the API (`201` with the created rows) and on `/owner/packages`.
- A duplicate `Rank` is refused with **409** `VALIDATION_FAILED` and a Thai message that names the clash
  and suggests a free rank, e.g. `ระดับนี้ซ้ำกับ Gold · เลือกระดับ 4 แทน`; the same error is shown under the
  rank field in the edit card. Covered by a seam-1 test and visible in the screen capture.
- There is no delete endpoint: `DELETE /api/packages` and `DELETE /api/packages/{id}` both answer **405**,
  asserted in a test together with a database assertion that the row is still present. Deactivation happens
  through `PUT` (`IsActive=false`, row retained) which is what keeps sales history intact.
- Further validation covered by tests: rank outside 1–99, empty name, adult price ≤ 0, child price above
  the adult price or below zero, and a duration of 0 or beyond 601 all answer 400 `VALIDATION_FAILED` with a
  Thai message and write nothing; an unknown id answers 404.
- Authorization: anonymous **401**, Kitchen **403** on all three verbs.
- Screen compared as an image at the frame's own 1440×900 viewport against `design/out/o11-packages.png`:
  rank discs, bold names, right-aligned ฿ prices, `90 นาที`, the availability switch, `3 ระดับ · …`
  subtitle, `เพิ่มแพ็กเกจ`, the `แก้ไขแพ็กเกจ · Gold` card with its `ระดับ 2` badge, price boxes, `เวลา`,
  `ใช้งาน` with `แสดงในหน้าเปิดโต๊ะ`, the danger `ปิดใช้งาน` button, and the red rank error all match.
- Known, intentional differences, none of which is behaviour the ticket asks for: the sidebar shows only the
  routes that exist today (the frame's other nav entries arrive with their own tickets); the frame's sort
  chevrons are decorative and no sorting is specified, so they were not added; the frame's menu-count ladder
  shows the honest "not yet available — waits for ticket 05" state rather than the frame's seeded counts,
  because the counts are derived from a real `/api/menu-items` query that ticket 05 introduces.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23`;
  `cd frontend && pnpm build` → routes emitted, then `pnpm exec tsc --noEmit` → clean.
