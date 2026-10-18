# 22: รวมบิลหลายโต๊ะ ชำระครั้งเดียว

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 68) และ `../plan.md` (ไม่มีใน plan.md (ใหม่ตาม spec)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o19 ใน `../../design/out/`

## What to build

เจ้าของร้านเลือกโต๊ะหลักและรวมโต๊ะอื่นที่ยังไม่ปิดบิลเข้าบิลเดียวได้ แม้ครัวยังทำออเดอร์ไม่เสร็จ ลูกค้าแต่ละโต๊ะยังสั่งผ่าน QR เดิมได้ (ออเดอร์ใหม่เข้าบิลรวม ตาม EndsAt ของ session ตัวเอง) บิลรวมคิด subtotal ของทุก session แล้วคำนวณ SC/VAT ครั้งเดียว ชำระครั้งเดียวที่ session หลัก ใบเสร็จใบเดียว เมื่อชำระ ทุก session กลายเป็น Paid ทุก token สั่งไม่ได้และโต๊ะทั้งหมดว่าง ยกเลิกการรวมไม่ได้

## Override ของ plan.md

plan.md บรรทัด 119: รวมโต๊ะอยู่นอกขอบเขต — ถูกยกเลิกสำหรับการรวมบิล (ผู้ใช้ตัดสินให้เก็บ)

## Blocked by

14 คิวออเดอร์ของเจ้าของร้าน, 18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [x] ตัวอย่างใน mockup o19 ให้ยอดตรง: 913.00 + 1,096.00 = 2,009.00; service 200.90; vat 154.69; total 2,364.59 (เทสต์ seam 1)
- [x] ชำระที่ session หลักแล้วทุก session ที่รวมเป็น Paid ได้ใบเสร็จใบเดียว และ token ทุกอันสั่งไม่ได้อีก
- [x] ในรายงาน (ticket 23) บิลรวมนับเป็นหนึ่งบิล จำนวนลูกค้ารวมทุก session และแพ็กเกจนับตาม session หลัก (สมมติฐานของ spec)
- [x] หน้าตรงกับ mockup o19 และเปิดจากปุ่ม "รวมบิล" ใน ticket 14
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 22 — รวมบิลหลายโต๊ะ ชำระครั้งเดียว

Resolution: several open rounds can be combined into one bill that is paid once, and the combined bill is
produced by the same calculator that every other screen uses — no second formula exists.

Evidence:

- The mockup's own figures are the test contract and they hold exactly: subtotal ฿913.00 + ฿1,096.00 =
  **฿2,009.00**, service **฿200.90**, VAT **฿154.69**, total **฿2,364.59**. The read-only preview endpoint
  returns the same four numbers for the same selection and writes nothing, so the dialog's panel is the
  server's arithmetic rather than the browser's.
- Paying once finishes every merged round: each becomes `Paid` with the same closing time, there is exactly
  **one** `Payment` row and one receipt of the frozen format on the primary round, both tokens are refused
  afterwards (`SESSION_CLOSED` on submit and on the customer read), the child's socket is disconnected, both
  tables show free, and a repeat or child payment answers `ALREADY_PAID`.
- Merge rules: a paid or voided round is refused (`SESSION_CLOSED`), merging a round with itself, an unknown
  round, a duplicate selection and an empty selection are refused, a round that is already a child cannot be
  merged again or promoted to primary, and an owner cannot pull a child back out — merging is not reversible,
  as the ticket requires. Kitchen 403, anonymous 401.
- A merged child keeps its own QR until payment: an order placed through it lands in the merged bill (the
  test's total moved to ฿2,417.56), which is the behaviour the spec describes. While the kitchen is still
  working, closing is refused and the message names the blocking round **with its table**
  (`โต๊ะ <A2> รอบที่ 2 (รอรับ)`) — a real defect found in passing, since round numbers repeat across tables,
  so the blocking-order shape now carries the table name.
- The reports from ticket 23 count a merged bill as one bill with all guests summed and the package taken
  from the primary round, which the spec states as an assumption; no change was needed there.
- Screens compared against `design/out/o19-merge-bill.png` with every string reused verbatim from the
  approved copy: title, subtitle, the primary-round well, section headers with counters, the checkbox rows,
  and the panel showing per-table totals, the pre-service total, service, VAT and the grand total. Two
  differences found by that comparison were fixed (dialog title size, and the primary row wrapping Thai
  mid-word); a clipped-text DOM check then reported nothing clipped.
- One deliberate difference: the destination rows are **not** pre-ticked even though the frame shows one
  ticked, because merging is irreversible and a tick the owner did not make would turn an accidental confirm
  into a destructive one. The screen requires an explicit tick, and says so.
- Live verification on a real stack: the dialog produced `รวมบิล SM-A6 + SM-A1`, six merge buttons and five
  checkboxes rendered, no horizontal scroll, and the probe reported the mockup figures matching
  (`MATCH o19: true`).
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 221, Skipped: 0, Total: 221` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean. The entity model was not changed,
  so `Initial` needed no regeneration.
- The tooling hazard the implementer reported is worth recording: a `read` with a **relative** path resolves
  against the main checkout rather than the worktree, which briefly showed it another branch's version of
  three shared files. All writes used absolute paths, so nothing foreign reached the patch.
