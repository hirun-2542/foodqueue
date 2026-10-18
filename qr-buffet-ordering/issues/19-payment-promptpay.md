# 19: ชำระ PromptPay: QR ตามยอดบิล

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 71, 73) และ `../plan.md` (Step 8 (PromptPay), Step 14); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o05 ใน `../../design/out/`

## What to build

เจ้าของร้านเลือก PromptPay เห็น QR ที่สร้างฝั่ง frontend จาก PromptPay ID ที่ตั้งไว้กับยอดรวมของบิล ลูกค้าสแกนจ่าย แล้วเจ้าของร้านกดยืนยันรับเงิน (บันทึกด้วยมือ ไม่มีการยืนยันอัตโนมัติ) ถ้ายังไม่ตั้ง PromptPay ID ตัวเลือกถูกปิดและแสดงข้อความแจ้ง

## Blocked by

18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [x] QR ที่แสดงถอดได้ PromptPay ID และยอดตรงกับบิล (ตรวจด้วยการถอดรหัส payload)
- [x] ไม่มี PromptPay ID → ปิดตัวเลือกพร้อมข้อความ ไม่แสดง QR เปล่า
- [x] ยืนยันรับเงินแล้วใช้เส้นทางปิดบิลเดียวกับ ticket 18 (ได้เลขใบเสร็จ token ใช้ไม่ได้) ตรงกับ mockup o05
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 19 — ชำระ PromptPay: QR ตามยอดบิล

Resolution: the owner gets a real PromptPay QR for the exact bill total, and confirming it closes the bill
through the same path cash uses. The payload was proved by decoding the rendered image, not by trusting the
encoder.

Evidence:

- **The payload is real and decodes to the right numbers.** The rendered QR was rasterized to 512px — the
  pixels a camera would see — and decoded with `jsqr`, then walked field by field by the test's own TLV parser
  with the CRC recomputed locally, so no PromptPay library is checking its own output. The decoded payload is
  `00020101021229370016A000000677010111011300668123456785802TH530376454071278.2263041528`, giving format
  `01`, POI `12` (dynamic), AID `A000000677010111`, target `0066812345678` (the configured `0812345678`),
  country `TH`, currency `764`, amount `1278.22` (the server's own bill total) and a verified CRC. This runs on
  both viewports and is rerun-stable.
- `promptpay-qr@0.5.0` **does** work under this Next 16 build, established by a throwaway app before the
  dependency was committed to, so no hand-written encoder was needed and `plan.md`'s contingency was not
  required. `lib/promptpay.ts` adds one pure function that fixes the inputs: it strips non-digits from the ID
  and returns `null` for **both** a missing ID and a non-positive or non-finite total — a static QR with no
  amount is not "QR ตามยอดบิล", so it is never shown.
- Closing through PromptPay is the same close as cash: `method: "PromptPay"` is recorded with nothing
  tendered, the receipt comes from the database sequence in the frozen format, and the same
  `sessionChanged(Paid)` and `sessionClosed` events fire; paying twice answers `ALREADY_PAID` and the token is
  dead afterwards. A merged bill can also be paid by PromptPay with one receipt (ticket 22's path).
- With no PromptPay ID configured the option is disabled with `PromptPay: ยังไม่ได้ตั้งค่า PromptPay ID`
  (the wording `plan.md` fixes) and **no QR element is rendered at all**. A separate flag keeps a
  settings-still-loading moment from claiming the shop is unconfigured.
- Screen compared against `design/out/o05-pay-promptpay.png` with the dialog and QR geometry measured live
  (QR well 244px, QR 198px) and all wording reused from the approved copy: the total, `สแกนเพื่อจ่าย ฿…`, the
  account line `PromptPay 081-234-5678 · ชาบูเดโม่`, the note that the money arrives in the shop's account
  directly, and `ยืนยันว่าได้รับเงินแล้ว`. The QR band differs from the frame by design — the frame carries a
  decorative stand-in graphic while the product carries the encoded payload — and that difference is stated
  rather than hidden.
- The dependency addition (`jsqr` as a dev dependency) and the two new payload/flow files are the only new
  frontend packages; Cash and Card behaviour was not touched.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 223, Skipped: 0, Total: 223` on the merged
  integration branch; `pnpm build`, `pnpm exec tsc --noEmit` and eslint → clean; the new Playwright spec →
  `8 passed`.
