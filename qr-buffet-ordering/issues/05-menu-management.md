# 05: จัดการหมวด เมนู รูป และสถานะ "หมด"

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 81, 83, 84, 85, 86) และ `../plan.md` (Step 4 (Category, MenuItem, upload)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o09, o10 ใน `../../design/out/`

## What to build

เจ้าของร้านจัดการหมวดหมู่ (ลบได้เฉพาะหมวดที่ว่าง) และเมนู โดยเลือกชนิด Buffet (ระบุแพ็กเกจขั้นต่ำ ไม่มีราคา) หรือ À la carte (ระบุราคา) อัปโหลดรูป (jpeg/png/webp ≤2MB) และสลับ "หมด" ได้ทันที เมนูมีหมายเหตุอิสระต่อรายการเท่านั้น (ไม่มี option group) น้ำซุปเป็น Buffet Item แพ็กเกจที่สูงกว่าสั่งเมนูของแพ็กเกจต่ำกว่าได้ด้วยกฎ `MinPackageRank <= rank` โดยตั้งค่า MinPackageRank ที่นี่และพิสูจน์กฎใน ticket 09 ที่ลูกค้าเห็นเมนู

## Blocked by

03 ตั้งค่าร้าน, 04 จัดการแพ็กเกจบุฟเฟ่ต์

## Acceptance criteria

- [x] เพิ่ม/แก้เมนู Buffet ที่ไม่มี MinPackageRank หรือมีราคา ≠ 0 ถูกปฏิเสธ; À la carte ที่ราคา ≤ 0 หรือมี MinPackageRank ถูกปฏิเสธ (เห็นข้อผิดพลาดในฟอร์มตาม mockup o10)
- [x] ลบหมวดที่ยังมีเมนู → ถูกปฏิเสธ; หมวดว่างลบได้
- [x] อัปโหลดรูปเมนูผ่านกลไกจาก ticket 03; ไฟล์ผิดชนิด/เกิน 2MB ถูกปฏิเสธ; เมนูไม่มีรูปแสดง fallback (ไอคอน/ตัวอักษร)
- [x] สลับ "หมด" แล้วค่า `IsAvailable` เปลี่ยนทันที (การกระจาย event เป็นหน้าที่ ticket 13)
- [x] หน้าเมนูตรงกับ mockup o09 (ตาราง สวิตช์ availability) และ o10 (drawer แก้ไข)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 05 — จัดการหมวด เมนู รูป และสถานะ "หมด"

Resolution: the owner can manage categories and menu items, the buffet/à-la-carte rules are enforced
server-side in Thai, availability flips immediately, and menu photos go through the same upload seam as the
shop banner. Higher-rank packages ordering lower-rank items is a data question that ticket 09 proves.

Evidence:

- Kind rules, each with its own Thai message and no row written on rejection: a buffet item with a non-zero
  price → `เมนูบุฟเฟ่ต์รวมอยู่ในราคาบุฟเฟ่ต์แล้ว ราคาต้องเป็น 0 บาท`; a buffet item without a minimum package
  rank → `เมนูบุฟเฟ่ต์ต้องเลือกแพ็กเกจขั้นต่ำ`; an à-la-carte item priced at or below zero →
  `เมนูคิดเงินแยกต้องมีราคามากกว่า 0 บาท`; an à-la-carte item carrying a rank →
  `เมนูคิดเงินแยกต้องไม่มีแพ็กเกจขั้นต่ำ`. Editing a buffet item into a paid one is refused and the stored row
  keeps `Price=0`, `MinPackageRank=2`. Rank outside 1–99, negative price and an unknown category are refused
  the same way.
- Category deletion: a non-empty category is refused with **409** `VALIDATION_FAILED`, e.g.
  `ลบหมวด “น้ำซุป” ไม่ได้ · ยังมีเมนูอยู่ในหมวด 4 รายการ · ย้ายหรือลบเมนูก่อน`, and the category and its items
  survive; an empty category deletes cleanly (**204**) from both the API and the database.
- The availability toggle round-trips (`{"isAvailable":false}` persists) and changes nothing else on the row.
- Image upload uses the shared seam from ticket 03: a valid PNG lands at `/api/uploads/menu-<guid>.png` and
  fetches back byte-identical; a text file, a `.jpg` declaring `image/png`, one byte over 2 MB and a multipart
  body with no file part all answer 400 with the frozen sentence
  `ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB`; replacing an image deletes the previous file (the old path
  then 404s) and the no-photo state is restorable.
- Authorization: anonymous **401** and Kitchen **403** on every route, including images and categories.
- Screen compared against both frames at 1440×900 with data seeded through the API to match (`30 รายการ ·
  8 หมวดหมู่ · หมดชั่วคราว 1`): `o09-menu` matches the category rail with its counts and active tint, the
  `จัดการหมวดหมู่` footer, the search box, `เพิ่มเมนู`, the six sortable columns, `บุฟเฟ่ต์`/`คิดเงินแยก`
  badges, `Silver ขึ้นไป`-style rank labels, `รวมในบุฟเฟ่ต์`, the ฿ prices, the switch geometry (44×26 with a
  20 px thumb, measured in the DOM), and the greyed `กุ้งขาว` row with `หมดชั่วคราว`. `o10-menu-edit` matches
  the 480 px drawer: subtitle, segmented kind control, the tier selector, the `รวมในราคาบุฟเฟ่ต์ · ราคา ฿0`
  well with no price box (the price field appears when the kind is switched to `คิดเงินแยก`), the note counter,
  the image controls with the 2 MB help line, and both switch rows. The frame's own `212 / 200` error state
  reproduces on demand.
- A hidden assumption was caught live: the API refused `15.999` with `ราคาใส่ทศนิยมได้ไม่เกิน 2 ตำแหน่ง`, which
  the client only loosely mirrors — the server's message is surfaced on the price field either way.
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 80, Skipped: 0, Total: 80`; `pnpm build` then
  `pnpm exec tsc --noEmit` → clean.
- Ticket 04's per-rank menu ladder on the packages screen now populates by itself, because its query key
  `["menu-items"]` is a prefix of this ticket's — no ticket 04 file was edited.
- Smoke on the real stack: a real PNG upload to `POST /api/menu-items/{id}/image` → `200` with
  `imagePath` under `/api/uploads/`; a buffet item submitted with a non-zero price →
  `400 VALIDATION_FAILED`. The menu screen was captured from the running stack and compared with
  `design/out/o09-menu.png`.
