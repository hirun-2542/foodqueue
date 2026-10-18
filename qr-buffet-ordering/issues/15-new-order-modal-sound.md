# 15: ป๊อปอัปออเดอร์ใหม่พร้อมเสียงสำหรับเจ้าของร้านทุกหน้า

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 59) และ `../plan.md` (Step 14 (layout), Step 13 (เสียง)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o20, o21 ใน `../../design/out/`

## What to build

เมื่อมีออเดอร์ใหม่เข้ามา เจ้าของร้านเห็นป๊อปอัปกลางจอบนทุกหน้า owner พร้อมเสียงเตือน ปิดเสียงได้ ป๊อปอัปแสดงโต๊ะ เลขรอบ สรุป และรายการที่ขยายดูได้ มีปุ่ม "ไว้ก่อน" (ปิดป๊อปอัป) และ "ไปหน้าออเดอร์" (นำทางไปคิวออเดอร์ ไม่เริ่มทำที่ป๊อปอัป) ไอคอนกระดิ่งนิ่ง ไม่กะพริบ

## Blocked by

14 คิวออเดอร์ของเจ้าของร้าน

## Acceptance criteria

- [x] ส่งออเดอร์จากฝั่งลูกค้าขณะ Owner อยู่หน้าใดก็ได้ → ป๊อปอัปขึ้นและเสียงดังหนึ่งครั้ง; ปิดเสียงแล้วไม่ดัง; ปุ่ม "ไปหน้าออเดอร์" นำทางโดยไม่เปลี่ยนสถานะออเดอร์
- [x] ตรงกับ mockup o20 (ยุบ) และ o21 (ขยายรายการ) บนหน้าผังโต๊ะ; เสียงเล่นหลังการโต้ตอบครั้งแรกของผู้ใช้ตามข้อจำกัดของเบราว์เซอร์ (บันทึกพฤติกรรมไว้)
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 15 — ป๊อปอัปออเดอร์ใหม่พร้อมเสียงสำหรับเจ้าของร้านทุกหน้า

Resolution: an owner anywhere in the back office is alerted to a new round by a popup driven by the existing
`orderCreated` event, with a sound that respects the browser's autoplay rule and two buttons that do what
they say. It adds no backend surface.

Evidence:

- The popup appears on every owner screen. A route sweep against the production build showed it on all seven:
  `/owner`, `/owner/orders`, `/owner/menu`, `/owner/packages`, `/owner/tables`, `/owner/settings` and
  `/owner/sessions/1`.
- `ไปหน้าออเดอร์` navigates to the queue **without touching the order**: after the click the URL is
  `/owner/orders`, the dialog is gone, and `GET /api/orders?status=Pending` still reports that round as
  `Pending` (asserted in the e2e test). `ไว้ก่อน` simply dismisses.
- Sound is honest about the browser rules, and this is recorded in the component and the test: before any
  user interaction the row reads `เบราว์เซอร์ยังไม่ให้เล่นเสียง · แตะที่ใดก็ได้บนหน้าจอเพื่อเปิด` and the
  oscillator count is **0**; the first tap unlocks it; the next round reads `กำลังเล่นเสียงแจ้งเตือน` with
  exactly one chime (two oscillators counted, i.e. one tone pair). A round that arrived while sound was
  blocked is deliberately **not** replayed. With the sound muted the popup still appears and the tone count
  does not move, and the row offers `เปิดเสียง`.
- The bell is static: an assertion checks `getAnimations({subtree:true})` has zero running animations 600 ms
  after the popup appears, so nothing pulses — which is what both the spec and the frames require.
- Owner-only: the scope is enabled only when `me.role === "Owner"`. A Kitchen account never sees the popup —
  the test waits until the round is live on the kitchen board and then asserts a zero dialog count and no
  `มีออเดอร์ใหม่` text anywhere, and `/owner/tables` redirects it to `/kitchen`.
- Popup and its expanded state compared against `design/out/o20-new-order-modal.png` and
  `o21-new-order-items.png` at 1440×900, with all wording taken verbatim from the approved copy
  (`มีออเดอร์ใหม่ · โต๊ะ … · รอบที่ N · ส่ง HH:MM`, the package and guest line, `ปิดเสียง`, `ดูว่าลูกค้าสั่งอะไร`,
  `ไว้ก่อน`, `ไปหน้าออเดอร์`, and the collapsed state's `ยังมีออเดอร์รอรับอีก N รอบ`).
- Known difference from `o21`: the frame shows a photo per line, but the order-line contract has no image
  field (the spec's item shape is name/kind/price/quantity/note). Per-line thumbnails were deliberately not
  added, and no substitute placeholder tile was invented either; if ticket 17's `MenuItemId` lands, thumbnails
  can be resolved with one extra query later. The kitchen board — the screen the owner jumps to — is
  text-only in the same way, so the popup is consistent with it.
- As a side effect of mounting one connection per owner screen tree, `LiveProvider` now reuses an enclosing
  connection with a matching audience, so owner screens no longer open a second hub connection each.
- `cd frontend && pnpm build` then `pnpm exec tsc --noEmit` → clean; `pnpm exec eslint e2e src` → clean;
  `pnpm exec playwright test e2e/new-order-popup.spec.ts` → `10 passed` (5 cases × desktop + mobile);
  `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 186, Skipped: 0, Total: 186`.
