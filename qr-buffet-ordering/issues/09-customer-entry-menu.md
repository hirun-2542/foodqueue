# 09: ลูกค้าสแกน QR เข้าเมนูตามแพ็กเกจ

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 1, 2, 5, 6, 7, 8, 9, 10, 28, 101) และ `../plan.md` (Step 6 (customer menu), Step 12); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame c1, c6 ใน `../../design/out/`

## What to build

ลูกค้าสแกน QR เข้า `/s/<token>` โดยไม่ต้องสมัครสมาชิก เห็นแบนเนอร์ร้านพร้อมชื่อโต๊ะและแพ็กเกจ แท็บหมวดที่เลื่อนได้และติดด้านบน เมนูที่แพ็กเกจของตนสั่งได้ (Buffet ที่ `MinPackageRank <= rank` ของ session + À la carte ทุกรายการที่ active) ป้าย "รวมในบุฟเฟ่ต์" ราคาของเมนูคิดเงินแยก รูปเมนู (หรือ fallback) และเมนูที่หมดเป็นสีเทาพร้อมป้าย "หมด" กดสั่งไม่ได้ QR ที่ไม่มีอยู่แสดง "QR ไม่ถูกต้อง" ลูกค้าเห็นเฉพาะข้อมูลของโต๊ะตัวเอง

## Override ของ plan.md

plan.md Step 12 (บรรทัด 202) header นับถอยหลังและ Tab "ยอดรวม" (บรรทัด 208) ถูกยกเลิก — ไม่มีนับถอยหลังและไม่มีบิลฝั่งลูกค้า

## Blocked by

05 จัดการหมวด เมนู รูป และสถานะ "หมด", 07 เปิดโต๊ะ + QR เฉพาะ session + ยกเลิกโต๊ะ (void)

## Acceptance criteria

- [x] เมนูของ session แพ็กเกจ rank ต่ำไม่มีเมนู Buffet ที่ rank สูงกว่า; rank สูงเห็นเมนูของ rank ต่ำด้วย (เทสต์ seam 1)
- [x] token ที่ไม่มีอยู่ → `SESSION_NOT_FOUND` และหน้า "QR ไม่ถูกต้อง" (c6); ข้อมูลที่คืนให้ token หนึ่งไม่มีข้อมูลของโต๊ะอื่น
- [x] หน้าตรงกับ mockup c1 (แบนเนอร์ แท็บหมวด แถวเมนู แถวหมด; tab bar เมนู/ออเดอร์) บนวิวพอร์ต 390×844 โดยไม่มี horizontal scroll
- [x] ไม่มีตัวนับถอยหลังและไม่มีแท็บบิล
- [x] ปุ่ม + และตะกร้าเป็นหน้าที่ ticket 10 (ที่นี่แสดงเมนูเท่านั้น)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 09 — ลูกค้าสแกน QR เข้าเมนูตามแพ็กเกจ

Resolution: a customer opening `/s/<token>` gets the shop banner with the table and package, the category
rail, and exactly the menu its package may order — with no sign-up and no countdown. An unknown token gets
the invalid-QR screen; a closed session is distinguished from a missing one.

Evidence:

- Rank filtering: a rank-1 (Silver) session's menu contains no rank-2 buffet item, while a rank-2 session
  sees both its own and the lower-rank items; an à-la-carte item is visible at every rank with its price and
  `kind: "AlaCarte"`. Covered by seam-1 tests that assert the response body, not the query.
- Inactive items are absent from the response; a `Buffet` item whose `IsAvailable` is false **is** returned
  with `isAvailable:false`, so the screen greys it out and shows `หมด` rather than silently hiding stock.
  A category with nothing orderable is not returned as a tab.
- Token handling: an unknown token answers `SESSION_NOT_FOUND` from both reads and renders `QR ไม่ถูกต้อง`;
  a Paid **and** a Voided session answer `SESSION_CLOSED` (a found-but-closed row is not a miss — the
  distinction is enforced by the endpoint, while the seam's `FindByTokenAsync` deliberately returns the row).
- Isolation: one token's response body contains neither another table's name nor its package name.
- Clock: the session payload carries `serverNow` (asserted to be within 30 s of the real clock) plus
  `lastOrderWarningMinutes` and both ordering limits, so the client never has to trust its own clock. The
  screen contains no countdown, no `setInterval`/`setTimeout` and no `Date.now()` in render.
- Mobile screen compared against `design/out/c1-menu.png` at 390×844 @2x with data seeded through the real
  endpoints from `design/DATA.md`: measured `documentScrollWidth == clientWidth == 390` (no horizontal
  scroll), the banner field with the table chip and the package chip, the shop name, the sticky horizontally
  scrollable category rail with its underline selection, the `รวมในบุฟเฟ่ต์` badge, à-la-carte prices, the
  greyed sold-out row, and the two-item tab bar. Tapping a tab scrolls the list and the rail tracks the
  section underneath it (verified at two positions).
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 114, Skipped: 0, Total: 114` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
- Deferred by design, not silently: the paid-session "ขอบคุณที่ใช้บริการ" screen belongs to the payment flow
  (tickets 18/10's dependency); this ticket's `SESSION_CLOSED` branch states the fact and refetches.
- Disclosure: the implementer's first smoke run pointed its scratch API at the shared compose Postgres by
  mistake and created a stray `foodqueue09` database, which it then dropped. I re-verified the real database
  afterwards: it contains only `foodqueue`, and its row counts are unchanged.
