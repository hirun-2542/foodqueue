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
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

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

### Smoke on the real stack (2026-10-18)

Resolution: exercised on **both** sides of the switch on the real stack — the DEMO-ON stack (`fqsm`,
`http://localhost:8091`, `Demo__Enabled=true`) and the DEMO-OFF stack (`fqsmoff`, `http://localhost:8092`,
`DEMO_ENABLED=false`). With Demo on, `/demo` renders the three panes as live same-origin iframes with one
`/hubs/live` socket each, and an order placed through the customer pane's own UI reached the kitchen pane and
the owner's popup with no document reload. **Only acceptance box 4 (the final `- [ ]`, the smoke-run box) is
marked proven here** — boxes 1–3 were already ticked by the implementer and this smoke corroborates but does
not re-claim them.

Evidence:

- **`/login` on the DEMO-ON stack offers the demo entry points.** `GET /api/demo/status` → **200
  `{"enabled":true}`**; the login screen rendered the `ทดลองใช้งาน` card (`loginDemoCardOn true`) with one
  `/demo` link labelled **`ดูทั้ง 3 มุมมองพร้อมกัน`** and the three role buttons
  `เข้าใช้แบบเจ้าของร้านภาพรวม โต๊ะ รายงาน และการรับชำระเงิน`,
  `เข้าใช้แบบครัวรับออเดอร์และเลื่อนสถานะบนจอครัว`,
  `ดูมุมมองลูกค้าลองสั่งอาหารเหมือนลูกค้าที่โต๊ะจริง`.
- **`/demo` renders the three real routes as three same-origin iframes, not copies in one document.**
  `document.querySelectorAll('iframe')` → **3**, with
  `src="/s/Pbb3CdaYTBp6DBvDFdX1UZvFSmIfgC1QW6Sl_jIQ-A8"` `title="มุมมองลูกค้า โต๊ะ A1"`,
  `src="/kitchen"` `title="จอครัว"`, `src="/owner"` `title="หน้าภาพรวมเจ้าของร้าน"` — all on
  `http://localhost:8091`, i.e. through the same Caddy origin as the board. The frames' own URLs are
  `http://localhost:8091/s/Pbb3Cda…`, `http://localhost:8091/kitchen`, `http://localhost:8091/owner` and the
  page's four pane captions are `ลูกค้า · โต๊ะ A1`, `ครัว`, `เจ้าของร้าน`, `บอทกำลังทำงาน`.
- **One hub socket per pane, and one for the page — four in total, nothing doubled.** Instrumenting
  `window.WebSocket` per frame and reading its log inside each frame:
  `http://localhost:8091/demo → ["ws://localhost:8091/hubs/live?id=UnVPASQpGiQs49FsHCRk_w"]`,
  `…/s/Pbb3CdaYTBp6DBvDFdX1UZvFSmIfgC1QW6Sl_jIQ-A8 →
  ["ws://localhost:8091/hubs/live?token=Pbb3CdaYTBp6DBvDFdX1UZvFSmIfgC1QW6Sl_jIQ-A8&id=Vxhf47vji7LgEUKOpCZUQA"]`,
  `…/kitchen → ["ws://localhost:8091/hubs/live?id=hRieM74_L3ixSKQrA5plzA"]`,
  `…/owner → ["ws://localhost:8091/hubs/live?id=LBALu4Joq4D9Gri1YxA9vA"]` — exactly **one** socket for each of
  the four documents, and the customer pane's is the token-scoped one. The page-level `websocket` events
  totalled **4**.
- **An order placed through the customer pane's own UI.** Inside the token frame I clicked the menu's add
  control (`getByRole('button', {name:/^เพิ่ม /})`, **40** such buttons) for `น้ำซุปใส`, opened the cart
  (`aria-label="เปิดตะกร้า · 1 เมนู · 1 จาก 20 รายการ"`), and pressed `ส่งออเดอร์`; the pane then showed
  `ส่งออเดอร์แล้ว`. The only order request the page issued was
  `POST http://localhost:8091/api/s/Pbb3CdaYTBp6DBvDFdX1UZvFSmIfgC1QW6Sl_jIQ-A8/orders` — the customer
  pane's own token, not the board's.
- **It reached the kitchen pane.** The kitchen document's waiting count went `รอรับ 5` → `รอรับ 6` and a card
  appeared with the `ใหม่` tag: `article[aria-label="โต๊ะ A1 รอบที่ 16"]` whose text is
  `A1 | รอบที่ 16 · ส่ง 15:12 | ใหม่ | รอรับ | <1 นาที | ×1 | น้ำซุปใส | เริ่มทำ` (`kitchenNewCards 1`).
- **It reached the owner pane's popup.** The owner document's dialog read
  `มีออเดอร์ใหม่ | โต๊ะ A1 | รอบที่ 16 · ส่ง 15:12 | Gold · 4 ผู้ใหญ่ · 1 ชิ้น | เบราว์เซอร์ยังไม่ให้เล่นเสียง ·
  แตะที่ใดก็ได้บนหน้าจอเพื่อเปิด | ปิดเสียง | ดูว่าลูกค้าสั่งอะไร | ไว้ก่อน | ไปหน้าออเดอร์ |
  ยังมีออเดอร์รอรับอีก 6 รอบ · A2, A3, A1`.
- **The bot panel gained the entry, and it reports the product's own event.** The board's bot panel read
  `บอทกำลังทำงาน | 15:12 · A1 ส่งรอบที่ 16` alongside the placeholder legend
  `ลูกค้า: ส่งออเดอร์ | ครัว: การ์ดใหม่โผล่ทันที | เจ้าของร้าน: ยอดและโต๊ะอัปเดต`.
- **Nothing reloaded.** A per-document `window.__loadCount` counter installed by `addInitScript` read
  **1** for all four documents at the moment the board was up and was still **1** for all four after the
  order had landed in the kitchen and the owner popup had fired (`{demo:1, /s/…:1, /kitchen:1, /owner:1}`
  before and after) — no pane document was re-created. The main frame's navigation list contains only
  `/login` and `/demo` (the two `page.goto` calls I made); the three `child:` navigations are the iframes'
  initial loads, and no second `child:` navigation followed the order.
- **DEMO-OFF stack: the switch turns everything off.** With `DEMO_ENABLED=false`, `PUBLIC_BASE_URL=http://localhost:8092`:
  `GET /api/demo/status` → **404**, `POST /api/demo/login` → **404**, `POST /api/demo/customer-session` →
  **404** (the browser recorded exactly two `404 /api/demo/status` responses, from `/login` and `/demo`).
  `/login` rendered no demo card at all: `getByRole('heading', {name:'ทดลองใช้งาน'})` → **0**, the
  `ดูทั้ง 3 มุมมองพร้อมกัน` link → **0**, role buttons matching `เจ้าของร้าน|ครัว` → **0**, `iframe` → **0**,
  and the page showed just `เข้าสู่ระบบ | อีเมล | รหัสผ่าน | เข้าสู่ระบบ | สำหรับเจ้าของร้านและพนักงานครัว`.
- `/demo` on the off stack rendered exactly `ไม่มีโหมดเดโมในเซิร์ฟเวอร์นี้ | หน้านี้แสดงได้เฉพาะเมื่อเปิด
  Demo Mode · เปิดด้วยค่า DEMO_ENABLED=true แล้วลองอีกครั้ง | กลับหน้าเข้าสู่ระบบ` with **0** iframes
  (`page.frames().length` = **1**, the main frame only), **0** `บอทกำลังทำงาน` headings, **0**
  `/hubs/live` WebSocket events and **0** `/hubs/live` HTTP requests, and only the two main-frame
  navigations I caused (`mainNavCount 2`, `childNavCount 0`) — the page does not even try to connect.
- **What this smoke did NOT prove (said rather than asserted):** the narrow-screen (`<1280px`) tab layout of
  `/demo`, where the same three documents become one tab each, was not exercised — I ran at 1440×1060. I also
  did not drive ticket 25's bot service itself (its rounds and activity lines); the bot panel's entry above
  came from my own order on a bot round, which is the filter path the panel uses either way.
