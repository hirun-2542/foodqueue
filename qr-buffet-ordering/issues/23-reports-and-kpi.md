# 23: รายงานยอดขายและ KPI หน้าภาพรวม

Status: ready-for-agent
State: closed

Source: `../spec.md` (user stories 51, 91, 92, 93, 94, 95, 96, 97, 98, 99) และ `../plan.md` (Step 9, Step 14 (reports, overview KPI)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec; หน้าจอที่ออกแบบแล้ว: frame o14, o01 ใน `../../design/out/`

## What to build

เจ้าของร้านเห็น KPI วันนี้ (ยอดขาย บิล จำนวนลูกค้า) บนหน้าภาพรวมที่อัปเดตเอง และหน้ารายงานที่สลับช่วงวันนี้/7 วัน/30 วัน แสดงยอดขาย จำนวนบิล จำนวนลูกค้า ยอดเฉลี่ยต่อหัว สัดส่วนยอดตามแพ็กเกจ Top 10 เมนู ยอดรายชั่วโมงและรายวัน และสัดส่วนวิธีจ่าย นับเฉพาะบิลที่ Paid ไม่นับออเดอร์ที่ยกเลิก วันขายนับตามเวลาไทย และข้อความ "ยังไม่มียอดขายในช่วงนี้" เมื่อไม่มีข้อมูล

## Blocked by

08 ผังโต๊ะและภาพรวมโต๊ะ, 18 ชำระเงินสด/บัตร ใบเสร็จเลขที่ ปิด session และหน้าขอบคุณ

## Acceptance criteria

- [x] เทสต์ seam 1: นับเฉพาะบิล Paid; ไม่นับออเดอร์ยกเลิกใน Top 10; บิลใกล้เที่ยงคืนเวลาไทยตกวันถูกต้อง; `avgPerGuest` = 0 เมื่อไม่มีลูกค้า; Kitchen เรียก reports → 403
- [x] หน้ารายงานตรงกับ mockup o14 (แท่งซ้อนแทนวงกลม) และแถบ KPI บนภาพรวม (o01) ตรงกับตัวเลขของ API
- [x] หน้าว่างแสดง "ยังไม่มียอดขายในช่วงนี้" ไม่ใช่กราฟว่าง
- [x] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)

## Comments

### 23 — รายงานยอดขายและ KPI หน้าภาพรวม

Resolution: the sales summary counts only paid bills, respects the Bangkok day, and feeds both the report
screen and the owner's KPI band from one endpoint so the two cannot disagree.

Evidence:

- A hand-computed fixture was seeded and every assertion reconciles with it. The fixture holds two paid rounds
  today (Silver cash ฿700 at 12:30 and Gold PromptPay ฿1,500 at 00:05, the second including a merged Silver
  sub-session), a Gold card round at 23:55 the previous day, a Silver round 40 days back, plus an Open and a
  Voided round today, and a cancelled order alongside ready ones.
  - `today` → revenue **2,200.00**, bills **2**, guests **8**, average **275.00**; package mix
    `Silver 1/2/700.00` and `Gold 1/6/1,500.00`; payment mix `PromptPay 1/1,500.00` and `Cash 1/700.00`;
    hourly sums to 2,200 (1,500 at hour 0, 700 at hour 12); one daily row for today.
  - `7d` → revenue **3,100.00**, bills **3**, guests **10**, average **310.00**; the previous day's round
    lands at hour 23 with 900; mixes and counts both sum to the revenue and the bill count.
  - `30d` → still 3,100.00 over 30 daily rows with the 40-day-old round excluded.
  - Top items today `หมูสามชั้น 7, เนื้อวัว 5, ชาไทยเย็น 3`: the 50 cancelled กุ้งขาว and the 9 unpaid beers are
    absent, and the merged sub-session's tea is included. Zero guests → `avgPerGuest = 0`. An empty range
    returns all zeros with 7 zero daily rows and 24 zero hourly rows.
- The `today` window starts at 00:00 Bangkok and a bill paid at 00:05 Bangkok is counted in that day rather
  than the previous one — the midnight boundary has its own test. `7d`/`30d` include today.
- The merged-bill rule from the spec's assumptions is implemented and stated: one bill, guests summed across
  the merged sessions, package taken from the primary session.
- Kitchen is refused with 403 and an anonymous caller with 401.
- Screens compared against `design/out/o14-reports.png` and the KPI strip of `o01-overview.png`: the range
  segment, four KPI cells with the same captions, daily bars with value labels and the current day emphasised
  plus weekday ticks, hourly bars with the peak emphasised, stacked share bars with a legend and a total row,
  and the two-column ranked Top 10 with progress bars and the sold-out badge. **No pie or donut chart exists
  anywhere** (a scan for pie/ring SVG found zero), which is what DESIGN.md requires.
- The empty state `ยังไม่มียอดขายในช่วงนี้` renders instead of empty axes, verified with a range that has no
  data (charts drawn: 0, empty state visible: true).
- `cd backend && dotnet test` → `Passed! - Failed: 0, Passed: 205, Skipped: 0, Total: 205` on the merged
  integration branch; `pnpm build` then `pnpm exec tsc --noEmit` → clean.
