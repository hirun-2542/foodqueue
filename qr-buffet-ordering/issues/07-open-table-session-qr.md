# 07: เปิดโต๊ะ + QR เฉพาะ session + ยกเลิกโต๊ะ (void)

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 44, 45, 47, 49, 50, 53) และ `../plan.md` (Step 5 (ไม่รวม extend), Step 14 (dialog เปิดโต๊ะ/QR)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o02, o03 ใน `../../design/out/`

## What to build

เจ้าของร้านเปิดโต๊ะว่างโดยเลือกแพ็กเกจและจำนวนผู้ใหญ่/เด็ก ระบบสร้าง Table Session ที่ snapshot ชื่อ/ราคา/rank ของแพ็กเกจ มี Session Token สุ่มเดาไม่ได้ (256 บิต, base64url 43 ตัว) `EndsAt = OpenedAt + DurationMinutes` และแสดง QR (ลิงก์ `/s/<token>`) บนจอ โต๊ะหนึ่งมี session Open ได้ทีละหนึ่ง (partial unique index) ยกเลิกโต๊ะที่เปิดผิดได้เมื่อยังไม่มีออเดอร์ที่ไม่ถูกยกเลิก ไม่มีการต่อเวลา

## Override ของ plan.md

plan.md บรรทัด 116 (`POST /api/sessions/{id}/extend`) และบรรทัด 220 (ปุ่มต่อเวลา 15/30/60) ถูกยกเลิก — ไม่มีการต่อเวลา (ผู้ใช้ตัดสินแล้ว)

## Blocked by

04 จัดการแพ็กเกจบุฟเฟ่ต์, 06 จัดการโต๊ะ

## Acceptance criteria

- [x] เปิดโต๊ะซ้ำที่ยังมี session Open → `TABLE_OCCUPIED` (เทสต์ seam 1 และด้วยการเปิดพร้อมกันสองคำขอ index ในฐานข้อมูลกันได้)
- [x] เปิดโต๊ะด้วยแพ็กเกจที่ปิดใช้งาน หรือโต๊ะที่ปิดใช้งาน ถูกปฏิเสธ
- [x] ปิดใช้งานโต๊ะที่มี session Open ถูกปฏิเสธ (ปิดช่อง ticket 06)
- [x] void session ที่ไม่มีออเดอร์สำเร็จ → สถานะ Voided โต๊ะว่าง; ที่มีออเดอร์ที่ไม่ถูกยกเลิก → `SESSION_HAS_ORDERS` (เทสต์ seed ออเดอร์เข้าฐานข้อมูลทดสอบตรง ๆ จนกว่า ticket 10 จะมีเส้นทางสั่งจริง)
- [x] ผลลัพธ์ตรงกับ mockup o02 (ฟอร์มเปิดโต๊ะ) และ o03 (QR ผลลัพธ์); QR สแกนได้จริงจากมือถือเมื่อตั้ง base URL เป็น IP ใน LAN
- [x] ไม่มี endpoint หรือปุ่ม 'ต่อเวลา' (ตรงกับ mockup o04)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 07 — เปิดโต๊ะ + QR เฉพาะ session + ยกเลิกโต๊ะ (void)

Resolution: opening a table creates a session with an unguessable token, a package snapshot and a
non-extendable window; the QR is session-scoped; void works only while no live order exists. This ticket
also created the session seam every later ticket reads through.

Evidence:

- Opening twice on one table yields **one** success and one **409** `TABLE_OCCUPIED` with a Thai message
  (`โต๊ะ … มีลูกค้านั่งอยู่แล้ว เปิดรอบซ้อนไม่ได้`); the concurrent case was covered with two genuinely
  simultaneous requests. The partial unique index is what enforces it — the losing insert is caught as
  `23505` rather than being pre-checked — and a token-index collision retries once.
- The token is exactly **43 base64url characters** and unique per session, proven in tests and visible in
  the QR: a PNG of the rendered QR was decoded and gave
  `http://192.168.1.15:3708/s/4xuxK2Tw7E3_…`, identical to the server's `qrUrl`.
- Snapshots are taken at open time: a later change to the package's price or rank does not move the
  existing session's `AdultPriceSnapshot`/`ChildPriceSnapshot`/`PackageNameSnapshot`.
- Refusals: a disabled table, a disabled package, `adultCount < 1` and `childCount < 0` are all rejected as
  `VALIDATION_FAILED` with Thai messages and write nothing.
- `POST /api/sessions/{id}/void` returns the session to `Voided` and frees the table when it has no
  non-cancelled order, and answers `SESSION_HAS_ORDERS` when it does; voiding an already-closed session
  answers `SESSION_CLOSED`. Disabling a table that holds an Open session is refused with `TABLE_OCCUPIED`
  (ticket 06 had already implemented that guard, so `Features/Tables` was **not** touched).
- `sessionChanged` is emitted after the database write — proven with a recording notifier in the tests.
- There is no extend route anywhere: a probe for it answers **404**, and no `Served` status exists in the
  system. `GET /api/sessions/{id}` returns the QR URL and a live (initially empty) bill rendered entirely by
  `BillCalculator`; the list endpoint filters by status and by Thai day, using `COALESCE(ClosedAt, OpenedAt)`
  so it also serves the history screen later.
- Screens compared against `design/out/o02-open.png` and `design/out/o03-qr.png` at 1440×900: the package
  rows with their อันดับ, the ผู้ใหญ่/เด็ก steppers, the live price line
  `ค่าบุฟเฟ่ต์ 2 × ฿399 + 1 × ฿199  ฿997.00`, the `เปิด 05:55 · หมดเวลา 07:25 · ยังไม่รวมค่าบริการและ VAT`
  line, and the QR result card (`โต๊ะ B6 เปิดแล้ว`, green banner, QR, คัดลอกลิงก์, three summary rows,
  `พิมพ์ QR slip`) all match. At 390×844 the screen has zero horizontal overflow.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 103, Skipped: 0, Total: 103` on the merged
  integration branch.
- Not verified by this ticket: the `พิมพ์ QR slip` target (`/print/qr/{id}` is ticket 20's route, linked with
  `prefetch={false}` so it does not 404 today) and scanning with a physical phone.
