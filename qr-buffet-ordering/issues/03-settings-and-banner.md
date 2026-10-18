# 03: ตั้งค่าร้าน: ข้อมูลร้าน SC/VAT ลิมิต Last order และภาพแบนเนอร์

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 65, 87, 88, 89, 90) และ `../plan.md` (Step 4 (settings), Step 2 (Settings)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o13 ใน `../../design/out/`

## What to build

เจ้าของร้านเปิดหน้าตั้งค่า กรอกชื่อร้าน ที่อยู่ โทร เลขผู้เสียภาษี PromptPay ID, Service charge %, VAT % (0–30, เริ่ม 0), ลิมิตต่อเมนูต่อรอบ (เริ่ม 5), ลิมิตรวมต่อรอบ (เริ่ม 20), นาทีเตือน Last order (เริ่ม 15) และอัปโหลดภาพแบนเนอร์ร้าน (jpeg/png/webp ไม่เกิน 2MB) ระบบเก็บเป็น settings เดียวของร้านและปฏิเสธค่านอกช่วง ตัวอย่างบิลสดในหน้าตั้งค่าคำนวณตามค่า SC/VAT ที่กรอก กลไกอัปโหลดไฟล์ที่นี่ใช้ซ้ำกับรูปเมนูใน ticket 05

## Blocked by

02 Login, roles Owner/Kitchen, auth guard

## Acceptance criteria

- [x] บันทึกแล้วโหลดใหม่ค่าตรงเดิม; ลิมิตนอก 1–99 หรือ VAT/SC นอก 0–30 → `VALIDATION_FAILED` ข้อความไทย (เทสต์ seam 1)
- [x] อัปโหลดแบนเนอร์ที่ไม่ใช่ jpeg/png/webp หรือเกิน 2MB ถูกปฏิเสธ; ไฟล์ที่ผ่านดึงกลับมาแสดงได้
- [x] Kitchen เรียก settings → 403
- [x] หน้าตั้งค่าตรงกับ mockup o13 ส่วนข้อมูลร้าน แบนเนอร์ SC/VAT ลิมิต และ Last order (ตัวอย่างบิลสดแสดงตามค่าที่กรอก)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 03 — ตั้งค่าร้าน: ข้อมูลร้าน SC/VAT ลิมิต Last order และภาพแบนเนอร์

Resolution: the shop settings singleton is editable by the owner only, out-of-range values are refused in
Thai, and the banner accepts exactly the three image types under the size cap. This ticket also created the
shared upload seam that ticket 05 reuses for menu photos.

Evidence:

- Round-trip: `PUT /api/settings` with SC 10.5 / VAT 7.25 / limits 3 / 42 / Last order 20 then `GET` returns
  the same object, and the `Settings` table holds exactly one row with those values.
- Out-of-range refusal, driven by a theory over ten cases (SC −1 and 31, VAT −1 and 30.01, each of the three
  limits at 0 and 100): every one answers **400** `VALIDATION_FAILED` with a Thai message naming the field,
  and the stored row is byte-identical to what it was before the request. The boundaries themselves (0 and 30;
  1 and 99) are accepted.
- Kitchen is **403** (as a status, with no redirect) on all four settings endpoints; anonymous is **401**.
- Banner upload: a real PNG returns `200` with `/api/uploads/banner-<guid>.png`, and fetching that path back
  yields `image/png` with the exact uploaded bytes. A `.txt`/`text/plain` file, a `.jpg` whose declared type is
  `image/png` (extension mismatch), and a file one byte over 2 MB are all refused with the contract sentence
  `ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB`; exactly 2 MB is accepted; a multipart body without a `file`
  part gives that same sentence instead of a framework error. `DELETE /api/settings/banner` clears the path.
- The banner path cannot be forged through the settings PUT body — only an upload may set it.
- Screen compared against `design/out/o13-settings.png` at the frame's own 1440×1120 viewport with real API
  data: the card sits at x=272 w=760 (frame 272/760) and the aside at x=1048 w=360 (frame 1048/360), inputs
  are 355×44, `h1` is 24 px/600, and every `h2`/`label` lands within 6 px vertically. The remaining pixel
  difference is confined to the sidebar (which lists only the three routes that exist today, no `DEMO DATA`
  tag, `FoodQueue` in the brand slot instead of the shop name) and form spacing.
- Screen behaviour with the live API: the worked example 10%/7% reads ฿1,000 + ฿100 + ฿77 = ฿1,177; typing
  15/0 changes it to ฿150/฿0/฿1,150; typing 31 shows `ค่าบริการ (%) ต้องอยู่ระหว่าง 0–30` and refuses the save;
  a valid save toasts `บันทึกการตั้งค่าแล้ว`; an oversized file and a `.gif` both toast the contract sentence
  without reaching the API; a valid PNG swaps the preview to the returned `/api/uploads/...` path.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 32, Skipped: 0, Total: 32`; on the merged
  integration branch `44` tests pass. `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- Smoke on the real stack: `PUT /api/settings` with SC 10 / VAT 7 → `200`; SC `31` →
  `400 VALIDATION_FAILED` `ค่าบริการ (%) ต้องอยู่ระหว่าง 0–30`; a real PNG banner upload → `200` with a
  retrievable `/api/uploads/banner-<guid>.png`. The settings screen was also captured from the running stack
  at 1440×900 and compared with `design/out/o13-settings.png` (matching geometry, live worked example).
