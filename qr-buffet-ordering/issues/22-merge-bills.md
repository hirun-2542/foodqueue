# 22: รวมบิลหลายโต๊ะ ชำระครั้งเดียว

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 68) และ `../plan.md` (ไม่มีใน plan.md (ใหม่ตาม spec)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o19 ใน `../../design/out/`

## What to build

เจ้าของร้านเลือกโต๊ะหลักและรวมโต๊ะอื่นที่ยังไม่ปิดบิลเข้าบิลเดียวได้ แม้ครัวยังทำออเดอร์ไม่เสร็จ ลูกค้าแต่ละโต๊ะยังสั่งผ่าน QR เดิมได้ (ออเดอร์ใหม่เข้าบิลรวม ตาม EndsAt ของ session ตัวเอง) บิลรวมคิด subtotal ของทุก session แล้วคำนวณ SC/VAT ครั้งเดียว ชำระครั้งเดียวที่ session หลัก ใบเสร็จใบเดียว เมื่อชำระ ทุก session กลายเป็น Paid ทุก token สั่งไม่ได้และโต๊ะทั้งหมดว่าง ยกเลิกการรวมไม่ได้

## Override ของ plan.md

plan.md บรรทัด 119: รวมโต๊ะอยู่นอกขอบเขต — ถูกยกเลิกสำหรับการรวมบิล (ผู้ใช้ตัดสินให้เก็บ)

## Blocked by

14 คิวออเดอร์ของเจ้าของร้าน, 18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [x] ตัวอย่างใน mockup o19 ให้ยอดตรง: 913.00 + 1,096.00 = 2,009.00; service 200.90; vat 154.69; total 2,364.59 (เทสต์ seam 1)
- [x] ชำระที่ session หลักแล้วทุก session ที่รวมเป็น Paid ได้ใบเสร็จใบเดียว และ token ทุกอันสั่งไม่ได้อีก
- [x] ในรายงาน (ticket 23) บิลรวมนับเป็นหนึ่งบิล จำนวนลูกค้ารวมทุก session และแพ็กเกจนับตาม session หลัก (สมมติฐานของ spec)
- [x] หน้าตรงกับ mockup o19 และเปิดจากปุ่ม "รวมบิล" ใน ticket 14
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 22 — รวมบิลหลายโต๊ะ ชำระครั้งเดียว

Resolution: several open rounds can be combined into one bill that is paid once, and the combined bill is
produced by the same calculator that every other screen uses — no second formula exists.

Evidence:

- The mockup's own figures are the test contract and they hold exactly: subtotal ฿913.00 + ฿1,096.00 =
  **฿2,009.00**, service **฿200.90**, VAT **฿154.69**, total **฿2,364.59**. The read-only preview endpoint
  returns the same four numbers for the same selection and writes nothing, so the dialog's panel is the
  server's arithmetic rather than the browser's.
- Paying once finishes every merged round: each becomes `Paid` with the same closing time, there is exactly
  **one** `Payment` row and one receipt of the frozen format on the primary round, both tokens are refused
  afterwards (`SESSION_CLOSED` on submit and on the customer read), the child's socket is disconnected, both
  tables show free, and a repeat or child payment answers `ALREADY_PAID`.
- Merge rules: a paid or voided round is refused (`SESSION_CLOSED`), merging a round with itself, an unknown
  round, a duplicate selection and an empty selection are refused, a round that is already a child cannot be
  merged again or promoted to primary, and an owner cannot pull a child back out — merging is not reversible,
  as the ticket requires. Kitchen 403, anonymous 401.
- A merged child keeps its own QR until payment: an order placed through it lands in the merged bill (the
  test's total moved to ฿2,417.56), which is the behaviour the spec describes. While the kitchen is still
  working, closing is refused and the message names the blocking round **with its table**
  (`โต๊ะ <A2> รอบที่ 2 (รอรับ)`) — a real defect found in passing, since round numbers repeat across tables,
  so the blocking-order shape now carries the table name.
- The reports from ticket 23 count a merged bill as one bill with all guests summed and the package taken
  from the primary round, which the spec states as an assumption; no change was needed there.
- Screens compared against `design/out/o19-merge-bill.png` with every string reused verbatim from the
  approved copy: title, subtitle, the primary-round well, section headers with counters, the checkbox rows,
  and the panel showing per-table totals, the pre-service total, service, VAT and the grand total. Two
  differences found by that comparison were fixed (dialog title size, and the primary row wrapping Thai
  mid-word); a clipped-text DOM check then reported nothing clipped.
- One deliberate difference: the destination rows are **not** pre-ticked even though the frame shows one
  ticked, because merging is irreversible and a tick the owner did not make would turn an accidental confirm
  into a destructive one. The screen requires an explicit tick, and says so.
- Live verification on a real stack: the dialog produced `รวมบิล SM-A6 + SM-A1`, six merge buttons and five
  checkboxes rendered, no horizontal scroll, and the probe reported the mockup figures matching
  (`MATCH o19: true`).
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 221, Skipped: 0, Total: 221` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean. The entity model was not changed,
  so `Initial` needed no regeneration.
- The tooling hazard the implementer reported is worth recording: a `read` with a **relative** path resolves
  against the main checkout rather than the worktree, which briefly showed it another branch's version of
  three shared files. All writes used absolute paths, so nothing foreign reached the patch.

### Smoke on the real stack (2026-10-18)

Resolution: on the real `docker compose` stack (`fqsm`, Caddy origin `http://localhost:8091`) I opened three
rounds, merged two of them into the third from the owner order queue's `รวมบิล` button, checked the dialog's
panel against the server's own arithmetic for the same selection, and paid once — every merged round became
Paid with exactly one receipt, all three tokens were refused afterwards, and the tables show free.
**Only acceptance box 5 (the final `- [ ]`, the smoke-run box) is marked proven here** — boxes 1–4 were
already ticked by the implementer and this smoke corroborates but does not re-claim them.

Evidence:

- **Three real rounds, opened and ordered on the stack.** `B2` (session **`945`**, Silver, 2 adults), `B4`
  (session **`946`**, Silver, 3 adults) and `B5` (session **`947`**, Silver, 1 adult 1 child) were opened
  through `POST /api/sessions` and each submitted a real order over its own customer token. Their own bills
  at that moment: `945` subtotal **687** / total **808.60**; `946` subtotal **986** / total **1,160.52**;
  `947` subtotal **537** / total **632.05**.
- **The `รวมบิล` button is real and on every queue card.** `/owner/orders` rendered **34** `article[aria-label^='โต๊ะ']`
  cards, each carrying a button with the accessible name `รวมบิลของโต๊ะ <table>` (the visible label is
  `รวมบิล`, frame `o08`'s secondary action row); clicking `รวมบิลของโต๊ะ B2` opened the `o19` dialog.
- **The dialog's numbers are the server's numbers, for the same selection.** With nothing ticked the panel
  read `โต๊ะหลัก | B2 | Silver | 2 ผู้ใหญ่ · เปิด 15:05 | ฿808.60`, six selectable rows
  (`A1 Gold · 4 คน ฿3,480.39 | A2 Gold · 3 คน ฿2,623.53 | A3 Gold · 4 คน ฿2,996.64 | B4 Silver · 3 คน
  ฿1,160.52 | B5 Silver · 2 คน ฿632.05 | B6 Gold · 3 คน ฿1,173.47`), `เลือกแล้ว 0 โต๊ะ` and the confirm button
  **disabled** (`confirmDisabledInitially true`) — merging is irreversible, so nothing is pre-ticked even
  though the frame shows a tick.
- Ticking `B4` then `B5` (two of the **6** rendered `[role=checkbox]` rows) moved the panel to
  `เลือกแล้ว 2 โต๊ะ` and `บิลรวม | 3 โต๊ะ | ยอดโต๊ะ B2 ฿687.00 | ยอดโต๊ะ B4 ฿986.00 | ยอดโต๊ะ B5 ฿537.00 |
  ยอดก่อนค่าบริการ ฿2,210.00 | ค่าบริการ 10% ฿221.00 | VAT 7% ฿170.17 | รวมทั้งสิ้น ฿2,601.17`, and the confirm
  button's label became **`รวมบิล B2 + B4 + B5`**.
- **Same selection against the server.** `POST /api/sessions/945/merge/preview {"sessionIds":[946,947]}`
  returned `primarySubtotal 687`, `mergeable [B4: sessionId 946 subtotal 986 total 1160.52, B5: sessionId 947
  subtotal 537 total 632.05]` and `bill {subtotal 2210, serviceCharge 221, vat 170.17, total 2601.17,
  adults 6, children 1, buffetTotal 1943, alaCarteTotal 267}` — **identical to what the dialog drew**, so the
  panel is the server's arithmetic, not the browser's. Single-selection preview agrees too:
  `{"sessionIds":[946]}` → `bill {subtotal 1673, serviceCharge 167.3, vat 128.82, total 1969.12}`, matching
  the panel's `ยอดโต๊ะ B2 ฿687.00 | ยอดโต๊ะ B4 ฿986.00 | ยอดก่อนค่าบริการ ฿1,673.00 | ค่าบริการ 10% ฿167.30 |
  VAT 7% ฿128.82 | รวมทั้งสิ้น ฿1,969.12`.
- **The preview endpoint writes nothing.** The raw `GET /api/sessions/941` JSON was byte-identical before and
  after a `merge/preview` for that round (`previewWroteNothing: true`), and a round that has never been merged
  does not carry a `mergedIntoSessionId` key at all (`a2ContainsKey false`), so a preview cannot half-merge.
- **SC/VAT are applied once, verified by hand from the three rounds' own subtotals.** A preview of
  `{941, 942}` into `940` returned `subtotal 8988` — exactly `3191 + 2658 + 3139` from the three rounds'
  standalone `bill.subtotal`, not three separately-rounded bills — with `serviceCharge 898.8` (= 10 % of
  8988), `vat 692.08` (= 7 % of 8988 + 898.8) and `total 10578.88`; my own arithmetic reproduced all four
  (`matches: true`).
- **The merge happened from the owner UI.** Confirming `รวมบิล B2 + B4 + B5` closed the dialog
  (`dialogClosedAfterMerge true`) and the API then reported `945` with `mergedIntoSessionId null` and total
  **2601.17**, while `946` and `947` both carried `mergedIntoSessionId: 945` and were still `Open`.
- **A child keeps its own QR until payment, and its order lands in the merged bill.** `GET /api/s/…B4-token/orders`
  → **200**; `POST` of 2 × โค้ก → **201** `{itemCount: 2}`; the merged primary's bill then moved from
  `2601.17` to **`2660.02`** and the primary's `orders` payload listed the child's orders
  (`s946:o2877:n1`, `s946:o2885:n2`) — the spec's "ลูกค้าแต่ละโต๊ะยังสั่งผ่าน QR เดิมได้".
- **Merging is not reversible and the rules hold.** `merge` into itself → **400 `VALIDATION_FAILED`**
  `รวมบิลของโต๊ะนี้เข้ากับตัวเองไม่ได้ · เลือกโต๊ะอื่นที่ยังไม่ปิดบิล`; empty selection → **400**
  `เลือกอย่างน้อยหนึ่งโต๊ะที่จะรวมเข้าบิลนี้`; unknown round → **404 `SESSION_NOT_FOUND`**; the same id twice →
  **400** `ระบุโต๊ะเดียวกันซ้ำในคำขอเดียวไม่ได้`; a paid round as the source → **409 `SESSION_CLOSED`**
  `โต๊ะ B2 ชำระเงินแล้ว · รวมบิลไม่ได้`; a child as the primary → **400** `โต๊ะ B4 ถูกรวมเข้าบิลอื่นอยู่แล้ว ·
  ชำระที่บิลหลัก ไม่ใช่รวมซ้ำ`; and after the merge, `POST /api/sessions/945/merge {sessionIds:[947]}` →
  **400** `โต๊ะ B2 เป็นเจ้าของบิลรวมอยู่ · ยกไปรวมกับบิลอื่นไม่ได้` (a child cannot be pulled back out). Kitchen
  → **403** on merge, preview and history; anonymous → **401**.
- **Closing was refused while the kitchen was still working.** Paying `946` (a child) before the kitchen
  finished answered **409 `ORDERS_IN_PROGRESS`** `ยังปิดบิลไม่ได้ · มีออเดอร์ค้างในครัว: รอบที่ 1 (รอรับ)`.
  I then advanced all four blocking orders on `/kitchen` with the board's own buttons (`เริ่มทำ` → `พร้อมเสิร์ฟ`)
  until the API listed `B2:o2876:Ready B4:o2877:Ready B5:o2878:Ready B4:o2885:Ready`.
- **ONE payment, ONE receipt, on the primary round.** On `/owner/sessions/945` the dialog read
  `รับชำระเงิน · โต๊ะ B2 | Silver | ยอดที่ต้องชำระ ฿2,660.02 | ค่าบุฟเฟ่ต์ ฿1,943.00 · เมนูคิดเงินแยก ฿317.00 ·
  ปรับยอด ฿0.00 · ค่าบริการและ VAT ฿400.02`; I took cash `3000` and pressed `ยืนยันรับเงินสด` →
  `ชำระเงินสำเร็จ | โต๊ะ B2 · ปิดบิลแล้ว ลูกค้าสั่งเพิ่มไม่ได้อีก | เลขที่ใบเสร็จ R20261018-000941 | วิธีจ่าย เงินสด |
  รับเงิน ฿3,000.00 | เงินทอน ฿339.98`.
- **Every merged session became Paid with the same closing time, and only the primary holds the payment.**
  `945` → `status Paid`, `payment.receiptNo R20261018-000941`; `946` → `status Paid`, `mergedIntoSessionId 945`,
  **no** `payment`; `947` → `status Paid`, `mergedIntoSessionId 945`, **no** `payment`. All three `closedAt`
  values are the identical string (`closedAtAllEqual true`). `GET /api/sessions/history` for today returned
  exactly **2** rows — `R20261018-000941` (sessionId **945**, table `B2`, `guests 7`, `total 2660.02`) and the
  earlier standalone `R20261018-000940` — with **0** rows for `946` and **0** for `947`, i.e. one receipt for
  the merged bill. `…/print-data?doc=receipt` for `945`, `946` **or** `947` all answer **200** with the same
  receipt `R20261018-000941` and the same total `2660.02` on table `B2`, while `doc=qr` on `946`/`947` answers
  **409 `SESSION_CLOSED`**.
- **All merged tokens are refused.** `POST` and `GET /api/s/<token>/orders` for the `B2`, `B4` and `B5` tokens
  all answer **409 `SESSION_CLOSED`** `รอบนี้ปิดแล้ว · สั่งอาหารไม่ได้`. A repeat payment on `945` → **409
  `ALREADY_PAID`** `โต๊ะ B2 ชำระเงินแล้ว · ใบเสร็จ R20261018-000941 · รับชำระซ้ำไม่ได้`; a payment on the child
  `946` → **409 `ALREADY_PAID`** `โต๊ะ B4 ชำระเงินแล้ว · รับชำระซ้ำไม่ได้`.
- **The tables show free.** `GET /api/tables/overview` returns `B2`, `B4` and `B5` with **no `sessionId`**, and
  the overview screen renders three free cards: `B2 | ว่าง | 4 ที่นั่ง | เปิดโต๊ะ`, `B4 | ว่าง | 4 ที่นั่ง |
  เปิดโต๊ะ`, `B5 | ว่าง | 4 ที่นั่ง | เปิดโต๊ะ`.
- **The merged receipt is one 80 mm document.** `/print/receipt/945` rendered `เลขที่ R20261018-000941 |
  วันเวลา 18 ต.ค. 2569 15:09 | โต๊ะ B2 · Silver | 6 ผู้ใหญ่ 1 เด็ก | เปิด 15:05` with `ผู้ใหญ่ 6 × ฿399 ฿1,794.00`,
  `เด็ก 1 × ฿149 ฿149.00`, `เบียร์สิงห์ 3 × ฿89 ฿267.00`, `โค้ก 2 × ฿25 ฿50.00`, `ค่าบริการ 10% ฿226.00`,
  `VAT 7% ฿174.02`, `รวมทั้งสิ้น ฿2,660.02` — the guests of all three rounds (6+1 = 7) and the one total, and
  `/owner/history` offers `พิมพ์ใบเสร็จซ้ำ` with `href="/print/receipt/945"`.
- **What this smoke did NOT prove (said rather than asserted):** the ticket's box 3 (the merged bill counting
  as one bill with summed guests in the **reports** screen of ticket 23) was already ticked and I did not
  re-exercise the reports screen; I only saw the merged single row and summed `guests 7` in the history API.
  I also did not test `MoveTableAction`/`ออเดอร์` movement, nor a merge attempted concurrently from two
  browsers (`FOR UPDATE` deadlock ordering) — both are outside this ticket's own behaviour.
