# Canonical demo data (synthetic, one snapshot)

Every frame draws from this file so the whole set tells one story. **Snapshot: ศุกร์ 16 ต.ค. 2569, 19:42.** All of it is demo data; the shop is fictional.
Dates use the Buddhist year (2569). Baht shown as `฿1,021.64` (tabular), two decimals on bills, none on menu/package prices.

## Shop
- ชาบูเดโม่ · caption "FoodQueue" · address 99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110 · โทร 02-000-0000 · เลขผู้เสียภาษี 0-1055-67000-00-0 · PromptPay 081-234-5678
- Service charge 10%, VAT 7%. Limits: 5 ต่อเมนูต่อรอบ, 20 รายการต่อรอบ. เตือน Last order 15 นาที.
- Users: owner@demo.local (เจ้าของร้าน, "ส" avatar, name คุณสมศรี), kitchen@demo.local (ครัว).

## Packages
| Package | rank | ผู้ใหญ่ | เด็ก | เวลา |
|---|---|---|---|---|
| Silver | 1 | ฿299 | ฿149 | 90 นาที |
| Gold | 2 | ฿399 | ฿199 | 90 นาที |
| Platinum | 3 | ฿549 | ฿279 | 120 นาที |

## Menu (Buffet = รวมในบุฟเฟ่ต์, min rank in brackets; À la carte = คิดเงินแยก)
- น้ำซุป: น้ำซุปใส [1], ต้มยำน้ำข้น [1], สุกี้น้ำ [1], หม่าล่า (เผ็ดชา) [1]
- หมู: สันคอหมูสไลซ์ [1], หมูสามชั้นสไลซ์ [1], หมูนุ่มสไลซ์ [1]
- เนื้อวัว: เนื้อวัวสไลซ์ [2], ริบอายสไลซ์ [3], วากิวสไลซ์ A5 [3]
- ทะเล: กุ้งขาว [2] **(หมดชั่วคราว)**, หมึกกล้วย [2], หอยเชลล์ [3], แซลมอนสไลซ์ [3]
- ลูกชิ้น/ของทอด: ลูกชิ้นปลา [1], ลูกชิ้นกุ้ง [1], เต้าหู้ปลา [1], เกี๊ยวซ่าทอด [1]
- ผัก: ผักกาดขาว [1], เห็ดรวม [1], ข้าวโพดอ่อน [1], ผักบุ้งจีน [1]
- ของหวาน: ไอศกรีมวานิลลา [1], วุ้นมะพร้าว [1]
- เครื่องดื่ม (À la carte): น้ำเปล่า ฿15, โค้ก ฿25, ชาไทยเย็น ฿45, ชาเขียวเย็น ฿45, น้ำมะพร้าว ฿55, เบียร์สิงห์ ฿89
- No photography exists: items use the `.thumb` placeholder (a lucide category icon on a quiet tile, see BRIEF.md).

## Tables at 19:42 (12 tables; every table seats 4 except B3, a large table that seats 6; "Time left" is only the arithmetic behind the cards, which show start–end and minutes used, never a countdown)
| Table | Package | Guests | Open–End | Time left | Orders not yet served | Bill total |
|---|---|---|---|---|---|---|
| A1 | Gold | 2 ผู้ใหญ่ | 18:55–20:25 | 43:12 | 1 | ฿1,074.60 |
| A2 | Silver | 3 ผู้ใหญ่ 1 เด็ก | 19:10–20:40 | 58:12 | 0 | ฿1,289.99 |
| A3 | Platinum | 4 ผู้ใหญ่ | 18:30–20:30 | 48:12 | 2 | ฿3,148.95 |
| A4 | Gold | 2 ผู้ใหญ่ | 18:20–19:50 | **08:12 (Last order)** | 0 | ฿1,045.18 |
| B1 | Silver | 2 ผู้ใหญ่ | 19:25–20:55 | 73:12 | 1 | ฿739.16 |
| B2 | Gold | 2 ผู้ใหญ่ 1 เด็ก | 17:55–19:25 | **ปิดการสั่งแล้ว** (ใช้ไป 107 นาที), all ready, ready to pay | 0 | ฿1,441.83 |
| B3 | Platinum | 3 ผู้ใหญ่ 2 เด็ก | 18:45–20:45 | 63:12 | 1 | ฿3,380.40 |
| B5 | Gold | 4 ผู้ใหญ่ | 19:30–21:00 | 78:12 | 2 | ฿1,884.30 |
| A5, A6, B4, B6 | free | | | | | |

Time rail `--p` = elapsed% of the package duration: A1 52, A2 36, A3 60, A4 91, B1 19, B2 100, B3 48, B5 13. Mark `--m` = (duration − 15)/duration: Silver/Gold 83.3, Platinum 87.5. A4 is `late`, B2 is `over`.

## Session A1 (the customer demo, โต๊ะ A1, Gold, 2 ผู้ใหญ่)
- รอบที่ 1 · 18:58 · เสิร์ฟแล้ว: สันคอหมูสไลซ์ ×3, เนื้อวัวสไลซ์ ×2, น้ำซุปต้มยำน้ำข้น ×1, ผักกาดขาว ×2
- รอบที่ 2 · 19:20 · เสิร์ฟแล้ว: หมูนุ่มสไลซ์ ×3, ลูกชิ้นกุ้ง ×2, เห็ดรวม ×1, ชาไทยเย็น ×1 (฿45), โค้ก ×1 (฿25)
- รอบที่ 3 · 19:41 · **รอรับ**: หมูสามชั้นสไลซ์ ×2 (หมายเหตุ "ไม่ติดมัน"), เห็ดรวม ×1, ชาไทยเย็น ×1 (฿45)
- **Customer orders page (merged view):** one list for the whole table, quantities summed across rounds, notes kept, no round numbers, statuses or times: สันคอหมูสไลซ์ ×3, หมูสามชั้นสไลซ์ ×2 ("ไม่ติดมัน"), หมูนุ่มสไลซ์ ×3, เนื้อวัวสไลซ์ ×2, น้ำซุปต้มยำน้ำข้น ×1, ลูกชิ้นกุ้ง ×2, ผักกาดขาว ×2, เห็ดรวม ×2, ชาไทยเย็น ×2 (฿45), โค้ก ×1 (฿25) = 20 รายการ. Staff screens keep the per-round data above.
- (Order rounds that were cancelled appear elsewhere: B3 รอบที่ 3 ยกเลิก — "วัตถุดิบหมด: กุ้งขาว")
- Bill: ค่าบุฟเฟ่ต์ผู้ใหญ่ 2×฿399 = ฿798.00 · ชาไทยเย็น 2×฿45 = ฿90.00 · โค้ก 1×฿25 = ฿25.00 · subtotal ฿913.00 · ค่าบริการ 10% ฿91.30 · VAT 7% ฿70.30 · **รวม ฿1,074.60** (this includes round 3's tea, which is still รอรับ).
- Limit counter in the cart for a new round: "8 / 20 รายการ".

## Session B2 (owner session detail, payment, receipt) — Gold, 2 ผู้ใหญ่ 1 เด็ก, 17:55–19:25, ปิดการสั่งแล้ว (ใช้ไป 107 นาที)
- ค่าบุฟเฟ่ต์ ผู้ใหญ่ 2 × ฿399 = ฿798.00 · เด็ก 1 × ฿199 = ฿199.00
- เบียร์สิงห์ 2 × ฿89 = ฿178.00
- ปรับยอด: ค่าปรับอาหารเหลือ +฿50.00
- subtotal ฿1,225.00 · ค่าบริการ 10% ฿122.50 · VAT 7% ฿94.33 · **รวม ฿1,441.83**
- 4 order rounds, all เสิร์ฟแล้ว. Payment: PromptPay, รับ ฿1,441.83, เงินทอน ฿0.00, เลขที่ใบเสร็จ **R20261016-000047**, ชำระ 19:46.

## Kitchen board at 19:42 (card = โต๊ะ · รอบที่ N · เวลา · อายุ)
- รอรับ: A1 รอบที่ 3 · 19:41 · <1 นาที (NEW, just arrived: หมูสามชั้นสไลซ์ ×2 "ไม่ติดมัน", เห็ดรวม ×1, ชาไทยเย็น ×1) · B3 รอบที่ 4 · 19:38 · 4 นาที (เนื้อวัวสไลซ์ ×4, ลูกชิ้นกุ้ง ×3, เกี๊ยวซ่าทอด ×2) · B5 รอบที่ 2 · 19:35 · 7 นาที (สันคอหมูสไลซ์ ×5, ผักบุ้งจีน ×2, หม่าล่า ×1 "เผ็ดน้อย", โค้ก ×2)
- กำลังทำ: A3 รอบที่ 5 · 19:29 · **13 นาที (เกิน 10 → แดง)** (แซลมอนสไลซ์ ×3, หอยเชลล์ ×4, ริบอายสไลซ์ ×4, เบียร์สิงห์ ×2) · B1 รอบที่ 2 · 19:36 · 6 นาที (สันคอหมูสไลซ์ ×3, ข้าวโพดอ่อน ×2)
- พร้อมเสิร์ฟ: A3 รอบที่ 4 · 19:22 · 20 นาที รอเสิร์ฟ (เนื้อวัวสไลซ์ ×4, ลูกชิ้นปลา ×3) · B5 รอบที่ 1 · 19:31 · 11 นาที (น้ำซุปใส ×1, หมูนุ่มสไลซ์ ×4, เห็ดรวม ×2)

Staff order flow (queue and kitchen): รอรับ → กดเริ่มทำ → กำลังทำ → กดพร้อมเสิร์ฟ → พร้อมเสิร์ฟ (final; no เสิร์ฟแล้ว step). Table colours at 19:42 (two states): ใช้งานอยู่ A1, A2, A3, A4, B1, B2, B3, B5 (A1, B3, B5 also have an unstarted order, shown as a bell line); ว่าง A5, A6, B4, B6. Minutes used at 19:42: A1 47, A2 32, A3 72, A4 82, B1 17, B2 107 (closed since 19:25), B3 57, B5 12. Merge example A1 + A2: subtotals ฿913.00 + ฿1,096.00 = ฿2,009.00, service ฿200.90, VAT ฿154.69, total ฿2,364.59. Edit example: remove 1 ชาไทยเย็น from A1 รอบที่ 3 → A1 total ฿1,074.60 → ฿1,021.64.

## Today / reports
- วันนี้ (ถึง 19:42): ยอดขาย ฿38,420 · 29 บิล · 91 คน · เฉลี่ย ฿422/หัว · +8% เทียบเมื่อวานเวลาเดียวกัน. โต๊ะที่เปิดอยู่ 8 จาก 12.
- 7 วัน: ฿214,860 · 168 บิล · 521 คน · ฿412/หัว. 30 วัน: ฿812,540 · 612 บิล · 1,903 คน · ฿427/หัว.
- รายวัน 7 วัน (฿): ส.10 39,870 · อา.11 36,540 · จ.12 21,310 · อ.13 19,980 · พ.14 24,650 · พฤ.15 34,090 · ศ.16 38,420
- รายชั่วโมง 7 วัน (฿): 11:00 7,400 · 12:00 17,100 · 13:00 13,900 · 14:00 6,200 · 15:00 4,500 · 16:00 5,800 · 17:00 15,600 · 18:00 35,500 · 19:00 48,300 · 20:00 44,200 · 21:00 16,360
- Package mix 7 วัน: Silver 52 บิล / 148 คน / ฿49,320 (23%) · Gold 79 / 246 / ฿106,170 (49%) · Platinum 37 / 127 / ฿59,370 (28%)
- วิธีจ่าย 7 วัน: PromptPay 74 บิล ฿96,700 (45%) · เงินสด 67 บิล ฿86,010 (40%) · บัตร 27 บิล ฿32,150 (15%)
- Top 10 เมนู 7 วัน (จำนวน): หมูสามชั้นสไลซ์ 1,184 · สันคอหมูสไลซ์ 1,052 · เนื้อวัวสไลซ์ 861 · น้ำซุปต้มยำน้ำข้น 744 · ลูกชิ้นกุ้ง 698 · เห็ดรวม 655 · กุ้งขาว 611 · ชาไทยเย็น 402 · เบียร์สิงห์ 377 · ไอศกรีมวานิลลา 356

## History (ชำระแล้ว, วันนี้) — เลขที่, โต๊ะ, เวลา, คน, Package, วิธีจ่าย, ยอด
R20261016-000046 · A6 · 19:38 · 2 · Silver · เงินสด · ฿739.16 ; -000045 · B4 · 19:21 · 4 · Gold · PromptPay · ฿1,949.11 ; -000044 · A5 · 19:05 · 3 · Silver · บัตร · ฿1,043.90 ; -000043 · B6 · 18:52 · 2 · Platinum · PromptPay · ฿1,356.40 ; -000042 · A2 · 18:31 · 5 · Gold · เงินสด · ฿2,401.20

Detail of -000045 (B4, the one shown open in the history screen): ผู้ใหญ่ 4 × ฿399 = ฿1,596.00 · น้ำเปล่า 4 × ฿15 = ฿60.00 · subtotal ฿1,656.00 · ค่าบริการ 10% ฿165.60 · VAT 7% ฿127.51 · **รวม ฿1,949.11** · PromptPay รับ ฿1,949.11 · ชำระ 19:21 · 4 order rounds, all เสิร์ฟแล้ว · no adjustments.

## Shared assets (real, scannable QR codes, ink-on-white SVG)
- `../assets/qr-session.svg` : QR for a session URL (`http://192.168.1.20:8080/s/<43-char token>`). Use `<img src="../assets/qr-session.svg">` (path is relative to the built html, which sits next to `frames/`). The token is fake; the URL may be shown truncated as `…/s/Kq3vT8mZ1xR4…`.
- `../assets/qr-promptpay.svg` : PromptPay QR for 081-234-5678, amount ฿1,441.83 (session B2). Only use it for ฿1,441.83.

## Demo Mode
Demo accounts: เจ้าของร้าน (owner@demo.local), ครัว (kitchen@demo.local). Bot message: "บอทจำลองลูกค้าสั่งอาหารทุก 20–40 วินาที". Demo login buttons: "เข้าใช้แบบเจ้าของร้าน", "เข้าใช้แบบครัว", "ดูมุมมองลูกค้า", link "ดูทั้ง 3 มุมมองพร้อมกัน".
