# 18: ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 27, 29, 69, 70, 72, 74, 75, 76, 77) และ `../plan.md` (Step 8 (payment, receipts), Step 14 (dialog ชำระ)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o06, o07, c6 ใน `../../design/out/`

## What to build

เจ้าของร้านรับชำระเงินสด (กรอกเงินที่ได้รับ เห็นเงินทอน) หรือบัตร (กดยืนยัน) ระบบปิดบิลได้เมื่อ session Open และไม่มีออเดอร์ Pending/Preparing (ออเดอร์ Ready ไม่ขวาง) สำเร็จแล้ว session เป็น Paid ได้เลขใบเสร็จ `R` + วันที่ไทย + `-` + ลำดับ 6 หลักจาก sequence ของฐานข้อมูล, Payment หนึ่งรายการต่อ session และ token ใช้สั่งไม่ได้ทันที ลูกค้าเห็นหน้า "ขอบคุณที่ใช้บริการ" (ไม่แสดงยอดเงิน) และ QR เก่าสั่งไม่ได้ กันการกดจ่ายซ้ำ

## Override ของ plan.md

plan.md บรรทัด 156 (และการทดสอบบรรทัด 248): เงื่อนไข 'ออเดอร์ต้อง Served ก่อนปิดบิล' ถูกแทนด้วย 'ไม่มี Pending/Preparing' (ผู้ใช้ตัดสินแล้ว)

## Blocked by

16 บิล

## Acceptance criteria

- [x] Cash ไม่พอ → `INSUFFICIENT_AMOUNT`; จ่ายซ้ำ → `ALREADY_PAID`; มีออเดอร์ Pending/Preparing → `ORDERS_IN_PROGRESS` พร้อมบอกว่าออเดอร์ไหนค้าง; เหลือแต่ Ready จ่ายได้ (เทสต์ seam 1)
- [x] จ่ายสำเร็จได้เลขใบเสร็จตามรูปแบบไม่ซ้ำ (จ่ายพร้อมกันสองคำขอได้ใบเสร็จใบเดียว) และ token สั่งไม่ได้อีก (`SESSION_CLOSED`); hub ตัดการเชื่อมต่อลูกค้าและส่ง `sessionClosed`
- [x] หลังเลย EndsAt ยังจ่ายได้; Kitchen เรียก payment → 403
- [x] หน้าตรงกับ mockup o06 (เงินสด) และ o07 (สำเร็จ + เลขใบเสร็จ) และฝั่งลูกค้า c6 (ขอบคุณ ไม่มียอด)
- [x] ตัวเลือก PromptPay แสดงแต่ถูกปิดพร้อมข้อความจนกว่า ticket 19 จะเสร็จ (ห้ามแสดง QR เปล่า)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 18 — ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

Resolution: cash and card close a bill through one shared service, the receipt number comes from a database
sequence, and a paid round immediately stops accepting orders. This is the code the Demo bot will call.

Evidence:

- Rule order, proven per code: an insufficient cash amount (including `0`) → `INSUFFICIENT_AMOUNT`; a
  double payment → `ALREADY_PAID` naming the existing receipt; a voided round → `ALREADY_PAID`; and a bill
  is blocked **only** by Pending/Preparing → `ORDERS_IN_PROGRESS`, whose payload names exactly the blocking
  rounds (Ready and Cancelled excluded). Once only a Ready order remains the bill closes — that is the spec's
  override of `plan.md`, and it has its own test. Invalid methods, missing cash amounts and three-decimal
  amounts answer with Thai validation errors and write nothing.
- Receipt numbers: format `^R\d{8}-\d{6}$` with the date being the Bangkok date of payment, taken from
  `receipt_no_seq`; six concurrent payments produce six distinct numbers; and a **refused** payment consumes
  none of the sequence. Two genuinely simultaneous payments (both proven to be waiting on the row lock via
  `pg_stat_activity`, then released together) yield exactly one success, one `ALREADY_PAID`, one `Payment`
  row and one `sessionClosed`.
- After payment the round is finished for the customer: `POST /api/s/{token}/orders` → `SESSION_CLOSED`,
  `GET /api/s/{token}` → `SESSION_CLOSED` carrying `closedAs:"Paid"` and the shop name with **no money,
  receipt or total fields**, and the round's sockets are aborted server-side after the event. The table
  shows free again on the owner overview. A round past `EndsAt` refuses new orders (`SESSION_EXPIRED`) but
  still pays, which is the spec's time rule.
- Concurrency defect found and fixed while building this: an order sent in the same instant as the bill
  closed could land as a Pending order inside a paid bill. `OrderService` now takes a `FOR SHARE` read of
  the session inside its insert transaction, so the order waits and is refused with `SESSION_CLOSED`.
  Demonstrated red-before-green: removing the read made that test fail with `Created` instead of `Conflict`,
  and removing the disconnect made the customer test time out.
- SignalR: the customer receives `sessionClosed` **and** is disconnected by the server; the owner receives
  `sessionChanged(Paid)` and `sessionClosed`; a neighbouring table is unaffected.
- Screens compared against `design/out/o06-pay-cash.png`, `o07-paid.png` and `c6-end.png`: the cash dialog
  with amount received and change, the success state with its receipt number, and the customer's
  `ขอบคุณที่ใช้บริการ` screen showing the shop name and no money. The PromptPay choice renders disabled with
  its message until ticket 19 lands, so no empty QR is ever shown.
- Authorization: Kitchen is refused with 403 and an anonymous caller with 401.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 186, Skipped: 0, Total: 186` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
