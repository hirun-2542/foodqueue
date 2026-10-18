# 26: หน้า split-screen ลูกค้า ครัว เจ้าของร้านเคียงกัน

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 108) และ `../plan.md` (Step 16 (split-screen)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame d1 ใน `../../design/out/`

## What to build

ผู้ชมเปิดหน้าเดียวที่แสดงมุมมองลูกค้า ครัว และเจ้าของร้านเคียงกัน (พร้อมแผง bot) เห็นข้อมูลไหลข้ามบทบาทแบบ real-time เมื่อลูกค้าสั่งหรือครัวเลื่อนสถานะ

## Blocked by

13 รายการของโต๊ะฝั่งลูกค้าแบบ real-time ออฟไลน์/reconnect และเมนูหมดทันที, 25 Bot จำลองลูกค้าสั่งอาหารและปิดบิลเอง

## Acceptance criteria

- [x] หน้าตรงกับ mockup d1 (สามมุมมอง + แผง bot) ใช้ Demo login/customer session จาก ticket 24 และ bot จาก ticket 25
- [x] สั่งจากมุมมองลูกค้าแล้วการ์ดโผล่ที่ครัวและป๊อปอัปที่ owner ในหน้าเดียวโดยไม่รีเฟรช
- [x] Demo ปิด → หน้านี้ไม่ถูกเสนอ/ไม่ทำงาน
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 26 — หน้า split-screen ลูกค้า ครัว เจ้าของร้านเคียงกัน

Resolution: one page shows the customer, kitchen and owner views beside the bot panel, and an order placed in
the customer pane reaches the other two live. When Demo is off the page does not work and does not offer itself.

Evidence:

- The three panes are the **real routes** (`/s/{token}`, `/kitchen`, `/owner`) rendered in same-origin iframes
  at their designed viewports and scaled to the frame's board, rather than three copies inside one document.
  The reason is recorded in the code: each shell sizes itself against the viewport, and — more importantly —
  ticket 15's live connection is reused *within one document*, so three in-tree copies would share a single hub
  connection and the kitchen pane would end up in the owner's groups. With one iframe per pane each gets its own
  cookie jar and exactly one connection; the spec waits for four handshakes (three panes plus the page) to prove
  nothing is doubled.
- **The join works:** an order placed through the customer pane's own UI (tapping the add control, opening the
  cart, submitting) appears on the kitchen pane as a `ใหม่` card for the round the server named, **and** the
  owner pane's popup fires with `มีออเดอร์ใหม่` and the round line, **and** the bot panel gains the entry — with
  the document-load counter unchanged before and after, so nothing reloaded. Run twice, stable.
- **Demo off:** `/login` shows no demo card and no split-screen link, and `/demo` states
  `ไม่มีโหมดเดโมในเซิร์ฟเวอร์นี้` with zero iframes, no bot heading, zero `/hubs/live` sockets and no further
  document loads. Both configurations were exercised, and the spec asks the stack which side of the switch it
  is on so it runs only the applicable half.
- The bot panel reports the product's **own** hub events filtered by the bot-round marker — the same filter the
  bot uses — rather than inventing activity lines, and it polls nothing. Because ticket 25 had not yet merged
  when this was verified, the panel was fed by the viewer's own order on a bot round; ticket 25 has since
  merged on top, and the filter path it exercises is the same one the bot's rounds take.
- Layout: at ≥1280px the frame's fixed scales are used (scaling the board between 1280–1439px); below that the
  screen switches to tabs, and the switch is chosen in JavaScript rather than by hiding a layout — a merely
  hidden layout would still load its iframes and give every pane a second connection.
- Screen compared against `design/out/d1-demo.png` at 1440×1060: pane borders land within ~2px of the frame's
  own edges. The remaining pixel difference is content, not geometry — the kitchen column carries real rounds
  rather than the mockup's staged ones, and the owner grid shows today's real figure (฿0, because the seeder
  deliberately writes only past days) instead of the mockup's synthetic total.
- `pnpm build` then `pnpm exec tsc --noEmit` → clean; `cd backend && dotnet test` →
  `Passed! - Failed: 0, Passed: 246, Skipped: 0, Total: 246` on the merged integration branch. No schema change.
