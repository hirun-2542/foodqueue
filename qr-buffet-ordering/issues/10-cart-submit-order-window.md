# 10: ตะกร้า ส่งออเดอร์ ลิมิต เลขรอบ และการปิดการสั่งเมื่อหมดเวลา

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 3, 4, 12, 13, 14, 15, 16, 17, 18, 19, 23, 24) และ `../plan.md` (Step 6 (create order), Step 12); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame c2, c5 ใน `../../design/out/`

## What to build

ลูกค้าเพิ่ม/ลดจำนวนด้วย +/− ใส่หมายเหตุต่อรายการ (≤100 ตัวอักษร) เห็นจำนวนรวมเทียบลิมิตต่อรอบ ตะกร้าเก็บในเครื่อง (ต่อ token) แยกกันในแต่ละมือถือและอยู่ต่อหลังรีเฟรช กด "ส่งออเดอร์" แล้วได้ข้อความยืนยัน ตะกร้าล้าง และเมื่อเหลือเวลาไม่เกินค่า Last order เห็นประกาศ "Last order" (ข้อความเท่านั้น) เมื่อเลย EndsAt เห็น "หมดเวลาสั่งอาหาร" และปุ่มสั่งถูกปิด ฝั่ง server ตรวจตามลำดับที่ spec กำหนดเสมอ ด้วยเวลาเซิร์ฟเวอร์ (ลูกค้าได้ค่า offset ชดเชยนาฬิกามือถือที่เพี้ยน) เลขรอบรันต่อ session

## Override ของ plan.md

plan.md Step 12 บรรทัด 202–207: นับถอยหลัง `mm:ss`, toast "ส่งออเดอร์รอบที่ N" ถูกแทนด้วยข้อความยืนยันที่ไม่มีเลขรอบ และประกาศ Last order แบบข้อความ

## Blocked by

09 ลูกค้าสแกน QR เข้าเมนูตามแพ็กเกจ

## Acceptance criteria

- [x] ลำดับตรวจ: session Open → ยังไม่เลย EndsAt → รายการไม่ว่าง → ต่อเมนู (รวมบรรทัดซ้ำ) ≤ ลิมิต → รวม ≤ ลิมิต → เมนู active → IsAvailable → Buffet ในสิทธิ์; ทุกกรณีมีเทสต์ seam 1 ที่ได้ `code` ตรงตาม spec (`SESSION_CLOSED`, `SESSION_EXPIRED`, `EMPTY_ORDER`, `QTY_LIMIT_EXCEEDED`, `ORDER_ITEMS_LIMIT_EXCEEDED`, `ITEM_NOT_FOUND`, `ITEM_UNAVAILABLE`, `ITEM_NOT_IN_PACKAGE`)
- [x] สั่งสำเร็จสองรอบได้เลขรอบ 1 และ 2 (max+1 ใน transaction เดียว retry หนึ่งครั้งเมื่อชน unique); หน้าจอลูกค้าไม่แสดงเลขรอบ
- [x] ตั้ง EndsAt ผ่านข้อมูลทดสอบ (ไม่ sleep) แล้วสั่ง → `SESSION_EXPIRED`; หน้าจอแสดง c5 "หมดเวลาสั่งอาหาร" และ Last order ตามค่าที่ตั้งใน ticket 03 โดยไม่มีตัวนับถอยหลัง
- [x] ข้อความภาษาไทยชัดเจนเมื่อสั่งไม่ได้ (เกินลิมิต หมดเวลา เมนูหมด เมนูไม่อยู่ในแพ็กเกจ)
- [ ] หน้าจอตรงกับ mockup c2 (cart sheet) และ c5 (Last order, ครบลิมิต, หมดเวลา)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 10 — ตะกร้า ส่งออเดอร์ ลิมิต เลขรอบ และการปิดการสั่งเมื่อหมดเวลา

Resolution: the ordering path runs the spec's checks in the spec's order with a distinct code for each, the
round number is server-assigned and never shown to the customer, the cart is per device and survives
refresh, and ordering closes on the server's clock — not the phone's. Verified end-to-end against the real
stack as well as through seam 1.

Evidence:

- Precedence proven one test per code, in order: unknown token → `SESSION_NOT_FOUND`; a Paid session →
  `SESSION_CLOSED`; past `EndsAt` with an empty cart → `SESSION_EXPIRED` (not `EMPTY_ORDER`); an empty cart
  that also exceeds a limit → `EMPTY_ORDER` (not `QTY_LIMIT_EXCEEDED`); an over-limit cart containing an
  unknown item → `QTY_LIMIT_EXCEEDED`/`ORDER_ITEMS_LIMIT_EXCEEDED` (not `ITEM_NOT_FOUND`); then item
  existence/active state → `ITEM_NOT_FOUND`, `ITEM_UNAVAILABLE`; then the package rank rule →
  `ITEM_NOT_IN_PACKAGE`.
- Duplicate lines of one item sum against the per-item limit: `3 + 3` is refused while `2 + 1` is accepted as
  two lines. Limits come from `RestaurantSettings`.
- Numbering: two submissions get rounds **1 then 2**; two genuinely simultaneous submissions both succeed
  with numbers 1 and 2 (the single retry path); numbering is `max + 1`, not a row count (a pre-existing
  1–5 continues at 6). Confirmed on the live stack too: `select "Number" from "Orders"` → `1, 2`.
- The customer pays no attention to rounds: the submit response carries only `submittedAt` and `itemCount`
  and has no `number`/`status`/`items` keys, and the screen shows only the confirmation `ส่งออเดอร์แล้ว`.
- Expiry is set through data (`EndsAt` backdated with a direct update), never a sleep, and the session stays
  open so the bill remains payable afterwards.
- Snapshots: a buffet line stores `unitPrice` 0, an à-la-carte line its price at order time; a later price
  change does not move an existing order's line.
- The cart is per device: `localStorage` under `fq-cart-{token}`, so two phones at one table keep separate
  carts while the table's orders are shared through the server. It survives a refresh.
- The ordering window uses the server's clock: the session payload's `serverNow` is stored as an offset once
  and added to displayed times; no countdown exists anywhere, and the screen shows the text-only `Last order`
  notice inside the warning window and `หมดเวลาสั่งอาหาร` with ordering disabled past `endsAt`.
- Screens compared against `design/out/c2-cart.png` and `design/out/c5-states.png`.
- Live end-to-end on the compose stack (customer token from the real QR session): first submission
  `201 {"submittedAt":…,"itemCount":3}`, second `201 {…,"itemCount":1}`, empty cart `422 EMPTY_ORDER`,
  duplicate-line over-limit `QTY_LIMIT_EXCEEDED`, unknown item `ITEM_NOT_FOUND`, and the two orders landed as
  rounds 1 and 2 in `Pending`.
- Integration join: this ticket's cart was mounted into ticket 09's customer screen by the controller
  (ticket 09 owns `page.tsx`/`menu-screen.tsx`); the join exposed one real type mismatch — the API omits null
  fields so `imagePath` arrives as `undefined`, while the cart had declared `string | null` — which was fixed
  in the cart's own type. That is the kind of defect the two-branch split could have shipped silently.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 137, Skipped: 0, Total: 137` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- Pre-existing, not this ticket's: `pnpm lint` reports one `react-hooks/purity` error in ticket 07's
  `open-table-flow.tsx:54` (its `Date.now()` is inside a click handler, so it is a false positive at a
  swallowable location); confirmed present with this ticket's changes stashed.
- Verified by the controller on the running stack after the join: the customer cart sheet renders at
  390×844 with no horizontal overflow (`scrollWidth == innerWidth == 390`) and matches
  `design/out/c2-cart.png` in structure — `ตะกร้า / ยังไม่ได้ส่ง`, `ล้างตะกร้า`, per-line thumb + name +
  `รวมในบุฟเฟ่ต์` or `คิดเงินแยก ฿45`, the 44 px steppers, and the `รายการในรอบนี้ 3 / 20 รายการ` well with
  `สั่งได้สูงสุด 5 ต่อเมนูต่อรอบ` above the `ส่งออเดอร์` button. The only differences from the frame are the
  seeded shop's own data (2 menu items instead of the frame's 4, and no photos yet) and the frame's
  inline note field which appears when the pencil is tapped.
- Full-stack smoke after the join, driven through Caddy at 390×844: adding items then submitting recorded
  round **3** in the database (`Orders.Number` 1, 2, 3, all `Pending`) with the line snapshot
  `หมูสามชั้นสไลซ์ | Buffet | 0.00 | 1`, the cart cleared, and the screen showing the confirmation without
  ever displaying the round number.
