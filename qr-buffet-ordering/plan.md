# FoodQueue — QR Ordering สำหรับร้านชาบู/บุฟเฟ่ต์ (Portfolio Full-Stack)

## Context
Greenfield repo `/home/novanod/Documents/ProjectPort/FoodQueue` (มีแค่ `.omp/` ยังไม่มีโค้ด) ต้องสร้างระบบสั่งอาหารผ่าน QR สำหรับร้านชาบู/บุฟเฟ่ต์ร้านเดียว แยกเป็น 3 ส่วน:
1. ลูกค้าสแกน QR ของ session แล้วดูเมนูตามแพ็กเกจ สั่งอาหาร และติดตามสถานะแบบ real-time โดยไม่ต้องสมัครสมาชิก
2. Owner Dashboard ใช้จัดการโต๊ะ อาหาร แพ็กเกจ ออเดอร์ การชำระเงิน และสถิติ รวมถึงพิมพ์ใบแจ้งยอด ใบเสร็จ และ QR slip
3. Kitchen Display รับออเดอร์และอัปเดตสถานะแบบ real-time

นอกจากนี้ต้องมี Demo Mode สำหรับ portfolio ด้วย

Toolchain ที่ตรวจในเครื่องแล้ว: .NET SDK 10.0.112, Node 24.16, pnpm 12.10, Docker 29.9 / Compose v5.6
เวอร์ชันแพ็กเกจล่าสุด ณ วันวางแผน: next 16.4.2, @microsoft/signalr 10.0.11, promptpay-qr 0.5.0, qrcode.react 4.2.0, recharts 3.10.1, tailwindcss 4.3.3, @playwright/test 1.64.1, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.13, Testcontainers.PostgreSql 4.16.0, Microsoft.AspNetCore.Mvc.Testing 10.0.13

## Domain rules (ยืนยันกับผู้ใช้แล้ว)
- รองรับร้านเดียว (single-tenant) จึงไม่มี RestaurantId UI เป็นภาษาไทยอย่างเดียว และรันแบบ local ด้วย docker compose เท่านั้น
- Roles มี `Owner` และ `Kitchen` Owner เข้าหน้า Kitchen Display ได้ด้วย ส่วน Kitchen เข้าได้เฉพาะ `/kitchen`
- **การเปิดโต๊ะ:** Owner กดเปิดโต๊ะ ระบุจำนวนผู้ใหญ่/เด็ก และเลือก Package ระบบจะสร้าง Table Session พร้อม Session Token แบบสุ่ม แล้วพิมพ์ QR slip ได้ QR ใช้ได้เฉพาะตอน session ยัง `Open`
- **Package** มีหลายระดับ แต่ละระดับมี `Rank`, ราคาผู้ใหญ่, ราคาเด็ก และ `DurationMinutes` Package ระดับสูงจะสั่ง Buffet Item ที่ `MinPackageRank <= Rank` ได้ทั้งหมด ส่วน À la carte Item คิดเงินแยกและทุก Package สั่งได้
- **จำกัดเวลา:** `EndsAt = OpenedAt + DurationMinutes` ลูกค้าเห็นนับถอยหลัง และเห็นแบนเนอร์ "Last order" เมื่อเหลือไม่เกิน `LastOrderWarningMinutes` (ค่าเริ่ม 15) พอเลย `EndsAt` แล้วเซิร์ฟเวอร์จะปฏิเสธออเดอร์ Owner ต่อเวลาได้ และไม่มีค่าปรับอัตโนมัติ
- **ลิมิตต่อรอบ:** แต่ละ item สั่งได้ไม่เกิน `MaxQtyPerItemPerOrder` (ค่าเริ่ม 5) และผลรวม quantity ต่อ Order ไม่เกิน `MaxItemsPerOrder` (ค่าเริ่ม 20) ไม่มีเวลารอระหว่างรอบ ตรวจทั้ง client และ server
- **สถานะ Order** เปลี่ยนทั้งออเดอร์ ลำดับคือ `Pending → Preparing → Ready → Served` โดย Kitchen/Owner เป็นคนกดทุกขั้น ส่วน `Cancelled` (ต้องมีเหตุผล) Kitchen/Owner ยกเลิกได้จากสถานะ Pending/Preparing/Ready ลูกค้ายกเลิกไม่ได้
- รายการอาหารมีแค่หมายเหตุอิสระ (ไม่เกิน 100 ตัวอักษร) ไม่มี option group น้ำซุปเป็น Buffet Item ในหมวด "น้ำซุป"
- **บิล** = ค่าบุฟเฟ่ต์ (ผู้ใหญ่×ราคา + เด็ก×ราคา ใช้ราคาที่ snapshot ไว้ตอนเปิดโต๊ะ) + À la carte ของ Order ที่ไม่ถูกยกเลิก + Bill Adjustment (บวกหรือลบได้ พร้อมเหตุผล ใช้กับค่าปรับอาหารเหลือ/ส่วนลด/ค่าเกินเวลา) จากนั้นคิด Service charge % และ VAT % ที่ตั้งค่าได้ (ค่าเริ่ม 0)
- **ชำระเงิน:** Owner บันทึกวิธีจ่าย `Cash` / `PromptPay` / `Card` ถ้าเป็น PromptPay ระบบแสดง QR ตามยอด ไม่มี payment gateway หลังจ่าย session จะเป็น `Paid` และ QR หมดอายุ
- **การพิมพ์:** พิมพ์ผ่าน browser print ด้วย CSS ขนาดกระดาษ 80mm ได้แก่ Pre-bill, Receipt และ QR slip ไม่มี kitchen ticket
- ฝั่งลูกค้าไม่มีปุ่มเรียกพนักงานหรือปุ่มขอเช็คบิล
- **Dashboard stats** แสดงข้อมูลตามช่วงวันนี้/7 วัน/30 วัน ได้แก่ ยอดขาย, จำนวนบิล, จำนวนลูกค้า (หัว), ยอดเฉลี่ยต่อหัว, สัดส่วน Package, Top 10 เมนู, ยอดรายชั่วโมง, สัดส่วนวิธีจ่าย และยอดรายวัน พร้อมโต๊ะที่เปิดอยู่แบบ real-time
- **Demo Mode:** seed ข้อมูลพร้อมประวัติ 30 วัน, ปุ่มเข้าใช้ทันทีตาม role, Bot จำลองลูกค้า และหน้า split-screen 3 มุมมอง ไม่มี auto-reset

## Approach

### Step 0 — Domain docs
- สร้าง `GLOSSARY.md` ที่ root (ตามรูปแบบ domain-modeling ใช้ bold term, นิยาม 1 บรรทัด และ `_Avoid_:`) ใส่คำเหล่านี้: **Table**, **Table Session** (_Avoid_: visit, check), **Session Token**, **Package** (_Avoid_: tier, plan), **Buffet Item**, **À la carte Item** (_Avoid_: extra, add-on), **Order** (_Avoid_: ticket; UI ใช้ "รอบที่ N"), **Order Status**, **Bill**, **Bill Adjustment** (_Avoid_: fee, discount line), **Payment** (_Avoid_: transaction), **Pre-bill**, **Receipt**, **Last order**, **Demo Bot** ห้ามใส่รายละเอียด implementation
- สร้าง `docs/adr/0001-session-scoped-qr.md` ยาวหนึ่งย่อหน้า อธิบายว่าเลือก QR ต่อ session แทน QR ติดโต๊ะถาวร เพราะ QR หมดอายุเมื่อปิดบิล กันการสั่งจากรูปถ่ายเก่า และผูกจำนวนคน/Package ได้ แลกกับการต้องพิมพ์ slip ทุกครั้ง
- สร้าง `docs/adr/0002-single-origin-caddy.md` ยาวหนึ่งย่อหน้า อธิบายว่าใช้ Caddy reverse proxy ให้ web, `/api` และ `/hubs` อยู่ origin เดียว (`http://localhost:8080`) เพื่อให้ cookie auth กับ SignalR WebSocket ทำงานได้โดยไม่ต้องพึ่ง Next rewrite proxy WebSocket และไม่ต้องตั้ง CORS

### Step 1 — Repo skeleton และ infra
โครงสร้าง:
```
compose.yaml  compose.dev.yaml  Caddyfile  .env.example  .gitignore  README.md
backend/FoodQueue.slnx
backend/src/FoodQueue.Api/          (ASP.NET Core 10 Minimal API, project เดียว)
backend/tests/FoodQueue.Api.Tests/  (xUnit)
frontend/                           (Next.js 16 App Router, TS, pnpm)
```
- `git init` ที่ root และสร้าง `.gitignore` ครอบ `bin/ obj/ node_modules/ .next/ uploads/ .env`
- `Caddyfile`:
  ```
  :8080 {
    handle /api/* { reverse_proxy {$API_UPSTREAM} }
    handle /hubs/* { reverse_proxy {$API_UPSTREAM} }
    handle { reverse_proxy {$WEB_UPSTREAM} }
  }
  ```
- `compose.yaml` มี service ดังนี้
  - `db`: `postgres:17` ใช้ volume `pgdata`, env `POSTGRES_DB=foodqueue POSTGRES_USER=foodqueue POSTGRES_PASSWORD=foodqueue` และ healthcheck `pg_isready`
  - `api`: build `backend/src/FoodQueue.Api/Dockerfile` (multi-stage sdk:10.0 → aspnet:10.0) ใช้ env `ConnectionStrings__Default=Host=db;Database=foodqueue;Username=foodqueue;Password=foodqueue`, `Demo__Enabled=${DEMO_ENABLED:-true}`, `App__PublicBaseUrl=${PUBLIC_BASE_URL:-http://localhost:8080}` และ volume `uploads:/app/uploads`
  - `web`: build `frontend/Dockerfile` (Next `output: "standalone"`) port ภายใน 3000
  - `caddy`: `caddy:2` เปิด port `8080:8080` ใช้ env `API_UPSTREAM=api:8080 WEB_UPSTREAM=web:3000`
- `compose.dev.yaml` override ให้รันแค่ `db` + `caddy` โดย caddy ใช้ `API_UPSTREAM=host.docker.internal:5080 WEB_UPSTREAM=host.docker.internal:3000` และ `extra_hosts: ["host.docker.internal:host-gateway"]` เปิด port db `5432:5432` ส่วน api รันบน host ที่ `http://localhost:5080` (กำหนดใน `launchSettings.json`)
- `.env.example` มี `DEMO_ENABLED=true` และ `PUBLIC_BASE_URL=http://localhost:8080` พร้อมคอมเมนต์ว่าให้ใช้ LAN IP ถ้าจะสแกนด้วยมือถือจริง

### Step 2 — Backend: data model และ migrations
Packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`
`Data/AppDbContext : IdentityDbContext<AppUser>` (`AppUser : IdentityUser` มี `DisplayName`) entity มีดังนี้ เงินทั้งหมดเป็น `decimal(10,2)` เวลาเป็น `DateTimeOffset` แบบ UTC (timestamptz) และ enum เก็บเป็น string (`HasConversion<string>()`)

| Entity | Fields |
|---|---|
| `Table` | `Id int`, `Name string(20) unique`, `Seats int`, `SortOrder int`, `IsActive bool` |
| `Package` | `Id`, `Name(50)`, `Rank int unique`, `AdultPrice`, `ChildPrice`, `DurationMinutes int`, `IsActive` |
| `Category` | `Id`, `Name(50)`, `SortOrder` |
| `MenuItem` | `Id`, `CategoryId`, `Name(80)`, `Description(200)?`, `ImagePath?`, `Kind` (`Buffet`/`AlaCarte`), `MinPackageRank int?` (ต้องมีค่าเมื่อเป็น Buffet และต้องเป็น null เมื่อเป็น AlaCarte), `Price` (ต้องเป็น 0 เมื่อเป็น Buffet และ >0 เมื่อเป็น AlaCarte), `IsAvailable` (ใช้สลับสถานะของหมด), `IsActive`, `SortOrder` |
| `TableSession` | `Id`, `TableId`, `PackageId`, `Token string(43) unique`, `AdultCount`, `ChildCount`, `PackageNameSnapshot`, `AdultPriceSnapshot`, `ChildPriceSnapshot`, `PackageRankSnapshot`, `OpenedAt`, `EndsAt`, `Status` (`Open`/`Paid`/`Voided`), `ClosedAt?`, `IsDemoBot bool` |
| `Order` | `Id`, `TableSessionId`, `Number int` (unique ร่วมกับ session), `Status` (`Pending`/`Preparing`/`Ready`/`Served`/`Cancelled`), `CancelReason(200)?`, `CreatedAt`, `UpdatedAt` |
| `OrderItem` | `Id`, `OrderId`, `MenuItemId`, `NameSnapshot`, `KindSnapshot`, `UnitPrice`, `Quantity`, `Note(100)?` |
| `BillAdjustment` | `Id`, `TableSessionId`, `Description(100)`, `Amount` (±), `CreatedAt` |
| `Payment` | `Id`, `TableSessionId unique`, `Method` (`Cash`/`PromptPay`/`Card`), `BuffetTotal`, `AlaCarteTotal`, `AdjustmentTotal`, `Subtotal`, `ServiceCharge`, `Vat`, `Total`, `AmountReceived`, `Change`, `ReceiptNo unique`, `PaidAt` |
| `RestaurantSettings` | singleton `Id=1`: `Name`, `Address`, `Phone`, `TaxId?`, `PromptPayId?`, `ServiceChargePercent`, `VatPercent`, `MaxQtyPerItemPerOrder=5`, `MaxItemsPerOrder=20`, `LastOrderWarningMinutes=15` |

- ตั้ง partial unique index `TableSession(TableId) WHERE "Status" = 'Open'` เพื่อให้โต๊ะเดียวมี session เปิดอยู่ได้ทีละหนึ่ง
- สร้าง Postgres sequence `receipt_no_seq` ใน migration
- สร้าง migration `Initial` แล้วเรียก `Database.MigrateAsync()` ตอน startup ใน `Program.cs` (เหมาะกับโปรเจกต์ local portfolio) และ seed `RestaurantSettings` Id=1 ถ้ายังไม่มี

### Step 3 — Backend: plumbing (auth, errors, realtime)
- **Feature folders** แต่ละ feature เขียน `public static class XEndpoints { public static RouteGroupBuilder MapX(this IEndpointRouteBuilder app) }`: `Features/Auth`, `Tables`, `Packages`, `Menu`, `Sessions`, `Orders`, `Billing`, `Reports`, `Settings`, `Demo`, `Customer` ห้ามใช้ MediatR หรือแยก layer Domain/Application/Infrastructure
- **Errors:** สร้าง `Common/AppException(string code, int status, string message)` และ exception handler ที่แปลงเป็น `ProblemDetails` พร้อม extension `code` ข้อความ `message` เป็นภาษาไทยเพื่อแสดงตรงใน UI รายการ code ที่ใช้คงที่ตามนี้:
  `SESSION_NOT_FOUND`(404), `SESSION_CLOSED`(409), `SESSION_EXPIRED`(409), `TABLE_OCCUPIED`(409), `ITEM_NOT_FOUND`(404), `ITEM_UNAVAILABLE`(409), `ITEM_NOT_IN_PACKAGE`(422), `QTY_LIMIT_EXCEEDED`(422), `ORDER_ITEMS_LIMIT_EXCEEDED`(422), `EMPTY_ORDER`(422), `INVALID_STATUS_TRANSITION`(409), `ORDERS_IN_PROGRESS`(409), `NEGATIVE_SUBTOTAL`(422), `SESSION_HAS_ORDERS`(409), `ALREADY_PAID`(409), `INSUFFICIENT_AMOUNT`(422), `VALIDATION_FAILED`(400)
- **Auth:** ใช้ `AddIdentityCore<AppUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager()` กับ cookie scheme `Identity.Application` cookie ชื่อ `fq_auth`, SameSite=Lax, HttpOnly และให้ 401/403 คืนเป็น status code ไม่ redirect สร้าง policy `OwnerOnly` (role Owner) และ `KitchenAccess` (role Owner หรือ Kitchen) Endpoints:
  - `POST /api/auth/login {email,password}` → 204
  - `POST /api/auth/logout`
  - `GET /api/auth/me` → `{email,displayName,role}`
- **Realtime:** สร้าง `Realtime/LiveHub` map ที่ `/hubs/live` และ `ILiveNotifier` ที่ wrap `IHubContext<LiveHub>`
  - Group: `kitchen`, `owner`, `session-{id}`, `menu`
  - `OnConnectedAsync`: ถ้ามี query `token` ให้ตรวจว่าเป็น session `Open` แล้วเข้า group `session-{id}` + `menu` ถ้า token ไม่ถูกต้องให้ `Context.Abort()` ถ้าเป็น user ที่ login ให้ Owner เข้า `owner` + `kitchen` + `menu` และ Kitchen เข้า `kitchen` + `menu`
  - ชื่อ event ฝั่ง client: `orderCreated(OrderDto)` และ `orderStatusChanged(OrderDto)` ส่งไป kitchen, owner และ session; `sessionChanged(SessionSummaryDto)` ส่งไป owner และ session; `menuItemChanged({menuItemId,isAvailable,isActive})` ส่งไป menu; `sessionClosed({sessionId})` ส่งไป session และ owner
  - ทุก event ส่งหลังจาก `SaveChangesAsync` สำเร็จแล้วเท่านั้น
- `Program.cs` ใช้ `AddProblemDetails`, JSON camelCase และ enum เป็น string (`JsonStringEnumConverter`) serve ไฟล์อัปโหลดจาก `/app/uploads` (config `App:UploadsPath`) ที่ `/api/uploads/{file}` ผ่าน `UseStaticFiles` + `PhysicalFileProvider` และ `RequestPath="/api/uploads"`

### Step 4 — Backend: Owner management APIs (ต้องผ่าน `OwnerOnly`)
- Tables:
  - `GET/POST /api/tables`, `PUT /api/tables/{id}`
  - `DELETE` เป็น soft delete โดยตั้ง `IsActive=false` ถ้าโต๊ะมี session Open ให้คืน `TABLE_OCCUPIED`
  - `GET /api/tables/overview` (`KitchenAccess`) คืนทุกโต๊ะที่ active พร้อม session ที่เปิดอยู่: `sessionId`, `packageName`, `adults`, `children`, `openedAt`, `endsAt`, `pendingOrders`, `runningTotal`
- Packages: `GET/POST /api/packages`, `PUT /api/packages/{id}`, `Rank` ซ้ำไม่ได้ (409 `VALIDATION_FAILED`) ไม่มี delete ให้ใช้ `IsActive`
- Menu:
  - `GET/POST/PUT /api/categories` และ `DELETE` ได้เฉพาะเมื่อหมวดว่าง
  - `GET/POST/PUT /api/menu-items`
  - `PATCH /api/menu-items/{id}/availability {isAvailable}` แล้ว emit `menuItemChanged`
  - `POST /api/menu-items/{id}/image` รับ multipart ≤2MB ชนิด jpeg/png/webp บันทึกเป็น `{guid}.{ext}` แล้วตั้ง `ImagePath="/api/uploads/{file}"`
  - ตรวจกฎ Kind/MinPackageRank/Price ตาม Step 2 ถ้าผิดคืน `VALIDATION_FAILED`
- Settings: `GET/PUT /api/settings` โดยเปอร์เซ็นต์ต้องอยู่ในช่วง 0–30 และลิมิตต้องอยู่ในช่วง 1–99

### Step 5 — Backend: Table Session lifecycle (`OwnerOnly`)
- `POST /api/sessions {tableId, packageId, adultCount>=1, childCount>=0}` สร้าง Token ด้วย `RandomNumberGenerator.GetBytes(32)` → base64url (43 ตัวอักษร) snapshot ข้อมูล package และตั้ง `EndsAt` ถ้าชน partial unique index (`PostgresException.SqlState == "23505"`) ให้คืน `TABLE_OCCUPIED` จากนั้น emit `sessionChanged` แล้วคืน `SessionDetailDto` ที่มี `qrUrl = {App:PublicBaseUrl}/s/{token}`
- `GET /api/sessions/{id}` คืนรายละเอียด orders, adjustments, `BillDto` และ payment `GET /api/sessions?status=Open|Paid&date=yyyy-MM-dd` ใช้แสดงประวัติ
- `POST /api/sessions/{id}/extend {minutes: 15|30|60}` ทำได้เฉพาะ session Open
- `POST /api/sessions/{id}/adjustments {description, amount}` และ `DELETE /api/sessions/{id}/adjustments/{adjId}` ทำได้เฉพาะ session Open ถ้า subtotal ใหม่ <0 ให้คืน `NEGATIVE_SUBTOTAL`
- `POST /api/sessions/{id}/void` ใช้ได้เฉพาะ session Open ที่ไม่มี order ที่ยังไม่ถูกยกเลิก (ถ้ามีคืน `SESSION_HAS_ORDERS`) ตั้ง `Status=Voided` แล้ว emit `sessionClosed`
- ย้ายโต๊ะและรวมโต๊ะไม่อยู่ในขอบเขต

### Step 6 — Backend: Customer + ordering
Endpoint ลูกค้าไม่ต้อง auth ใช้ token ระบุตัว:
- `GET /api/s/{token}` ถ้าไม่พบคืน `SESSION_NOT_FOUND` ถ้า Paid/Voided คืน `SESSION_CLOSED` ถ้าปกติคืน `{tableName, packageName, packageRank, adults, children, openedAt, endsAt, lastOrderWarningMinutes, maxQtyPerItemPerOrder, maxItemsPerOrder, serverNow}` (`serverNow` ใช้ชดเชยนาฬิกาเครื่องลูกค้าที่ไม่ตรง)
- `GET /api/s/{token}/menu` คืนหมวดหมู่และ item ที่ `IsActive` โดยมี Buffet Item ที่ `MinPackageRank <= PackageRankSnapshot` รวมกับ AlaCarte ทั้งหมด และส่ง `isAvailable` มาด้วย (item ที่หมดแสดงเป็นสีเทา)
- `GET /api/s/{token}/orders` คืน `OrderDto[]` และ `GET /api/s/{token}/bill` คืน `BillDto`
- `POST /api/s/{token}/orders {items:[{menuItemId, quantity, note?}]}` ตรวจตามลำดับนี้:
  1. session Open (`SESSION_CLOSED`)
  2. `now <= EndsAt` (`SESSION_EXPIRED`)
  3. items ไม่ว่าง (`EMPTY_ORDER`)
  4. รวม menuItemId ที่ซ้ำแต่หมายเหตุต่างกันเป็นบรรทัดแยก แต่จำนวนต่อ menuItemId รวมกันต้อง ≤ MaxQty (`QTY_LIMIT_EXCEEDED`)
  5. ผลรวม quantity ≤ MaxItems (`ORDER_ITEMS_LIMIT_EXCEEDED`)
  6. item มีอยู่และ active (`ITEM_NOT_FOUND`)
  7. `IsAvailable` (`ITEM_UNAVAILABLE`)
  8. Buffet item ต้องอยู่ในเงื่อนไข rank (`ITEM_NOT_IN_PACKAGE`)

  จากนั้น `Number = max(Number)+1` ภายใน transaction ถ้าชน unique ให้ retry 1 ครั้ง snapshot ชื่อและราคา (Buffet=0) ตั้ง `Status=Pending` แล้ว emit `orderCreated` และคืน 201 `OrderDto`
- `OrderDto = {id, sessionId, tableName, number, status, cancelReason, createdAt, updatedAt, items:[{name, kind, quantity, unitPrice, note}]}`

### Step 7 — Backend: Kitchen order workflow (`KitchenAccess`)
- `GET /api/orders?status=Pending,Preparing,Ready` (ค่าเริ่ม) และ `?date=` ใช้แสดงประวัติ เรียงตาม `CreatedAt`
- `POST /api/orders/{id}/status {status}` ยอมเฉพาะ transition `Pending→Preparing`, `Preparing→Ready`, `Ready→Served` โดยใช้ `ExecuteUpdateAsync(o => o.Id==id && o.Status==expectedPrev)` ถ้าได้ 0 แถวให้คืน `INVALID_STATUS_TRANSITION` ซึ่งกันกรณีสองจอกดพร้อมกันด้วย จากนั้น emit `orderStatusChanged`
- `POST /api/orders/{id}/cancel {reason}` ต้องมี reason 1–200 ตัวอักษร และทำได้จากสถานะ Pending/Preparing/Ready ใช้ conditional update แบบเดียวกัน

### Step 8 — Backend: Billing, payment, receipts
- สร้าง `Billing/BillCalculator.Compute(TableSession s, IEnumerable<Order> orders, IEnumerable<BillAdjustment> adj, RestaurantSettings st) → BillDto` เป็น pure function สูตร:
  - `buffet = adults*AdultPriceSnapshot + children*ChildPriceSnapshot`
  - `alaCarte = Σ UnitPrice*Quantity` เฉพาะ Order ที่ไม่ใช่ Cancelled
  - `adjust = Σ Amount`
  - `subtotal = buffet + alaCarte + adjust`
  - `service = Round(subtotal*SC/100, 2, AwayFromZero)`
  - `vat = Round((subtotal+service)*VAT/100, 2, AwayFromZero)`
  - `total = subtotal + service + vat`
  - `BillDto` มีบรรทัดค่าบุฟเฟ่ต์ผู้ใหญ่/เด็ก, บรรทัด à la carte ที่รวมตามชื่อ+ราคา, adjustments และตัวเลขทุกยอดข้างต้น
- `POST /api/sessions/{id}/payment {method, amountReceived}` (`OwnerOnly`) ตรวจตามลำดับ:
  1. session Open (ถ้าจ่ายแล้วคืน `ALREADY_PAID`)
  2. ต้องไม่มี Order ที่เป็น Pending/Preparing/Ready (`ORDERS_IN_PROGRESS` เพราะครัวต้องเสิร์ฟหรือยกเลิกให้เสร็จก่อน)
  3. ถ้าเป็น Cash ต้องมี `amountReceived >= total` (`INSUFFICIENT_AMOUNT`) ส่วนวิธีอื่นให้ใช้ amountReceived=total

  `ReceiptNo = "R" + PaidAt(Asia/Bangkok):yyyyMMdd + "-" + nextval('receipt_no_seq'):D6` บันทึก Payment ตั้ง session `Paid` และ `ClosedAt` แล้ว emit `sessionClosed` + `sessionChanged`
- `GET /api/sessions/{id}/print-data` (`OwnerOnly`) คืน `{settings(name,address,phone,taxId), tableName, packageName, adults, children, openedAt, bill, payment?, qrUrl}` ใช้ข้อมูลชุดนี้กับทั้ง 3 หน้าพิมพ์
- PromptPay payload สร้างฝั่ง frontend ด้วย `promptpay-qr` จาก `settings.promptPayId` + `total` ถ้ายังไม่ได้ตั้ง `promptPayId` ให้ disable ปุ่ม PromptPay QR และแสดงข้อความ "ยังไม่ได้ตั้งค่า PromptPay ID"

### Step 9 — Backend: Reports (`OwnerOnly`)
- `GET /api/reports/summary?range=today|7d|30d` นับเฉพาะ session ที่ `Paid` โดยใช้ `PaidAt` จัดกลุ่มตามเวลา `Asia/Bangkok` ช่วง `today` คือตั้งแต่ 00:00 Bangkok ส่วน `7d`/`30d` นับรวมวันนี้ คืน:
  `{revenue, billCount, guestCount (adults+children), avgPerGuest, packageMix:[{name,sessions,guests,revenue}], topItems:[{name,quantity}] (10 รายการ, รวมทั้ง Buffet และ AlaCarte, ไม่นับ order ที่ยกเลิก), hourly:[{hour 0-23, revenue}], paymentMix:[{method,count,total}], daily:[{date,revenue,bills}]}`
- ถ้า `guestCount==0` ให้ `avgPerGuest = 0`

### Step 10 — Backend: Demo Mode (เปิดเมื่อ `Demo:Enabled=true` เท่านั้น ถ้าปิด endpoint ทั้งหมดจะ map แต่คืน 404)
- `Demo/DemoSeeder` รันตอน startup เมื่อ `Tables` ยังว่าง โดยใช้ `new Random(42)` เพื่อให้ผลซ้ำได้ทุกครั้ง:
  - Users: `owner@demo.local` (Owner) และ `kitchen@demo.local` (Kitchen) รหัสผ่าน `Demo1234!`
  - Settings: ชื่อ "ชาบูเดโม่", PromptPayId `0812345678`, SC 10%, VAT 7%
  - Tables `A1–A6`, `B1–B6` (4 ที่นั่ง)
  - Packages: `Silver` rank1 ผู้ใหญ่ 299 / เด็ก 149 / 90 นาที, `Gold` rank2 399/199/90, `Platinum` rank3 549/279/120
  - Categories ตามลำดับ: น้ำซุป, หมู, เนื้อวัว, ทะเล, ลูกชิ้น/ของทอด, ผัก, ของหวาน, เครื่องดื่ม
  - Menu ~40 รายการ: น้ำซุป 4 อย่าง (rank1), หมู (rank1), เนื้อวัวพรีเมียม (rank2–3), ทะเล (rank2–3), ผัก (rank1), ของหวาน (rank1) ส่วนเครื่องดื่มเป็น AlaCarte ราคา 25–89 เช่น น้ำเปล่า 15, โค้ก 25, ชาไทย 45, เบียร์ 89 ไม่มีรูป
  - History 30 วัน (ไม่รวมวันนี้): วันละ 20–45 session เวลาเปิด 11:00–21:30 Bangkok, 1–5 orders ต่อ session (Served ทั้งหมด), ทุก session จ่ายแล้ว (วิธีจ่าย Cash 40% / PromptPay 45% / Card 15%), ประมาณ 10% มี adjustment ค่าปรับอาหารเหลือ +50 หรือส่วนลด −20 ใช้ `IsDemoBot=true`
- `POST /api/demo/login {role:"Owner"|"Kitchen"}` sign in เป็น user demo ตาม role
- `POST /api/demo/customer-session` คืน `{token}` ของ session ที่ bot เปิดอยู่ ถ้าไม่มีให้เปิดใหม่บนโต๊ะว่าง (Gold, 2 ผู้ใหญ่) ถ้าไม่มีโต๊ะว่างเลยคืน `TABLE_OCCUPIED`
- `Demo/DemoBotService : BackgroundService` วนทุก `Random(20..40)` วินาที:
  1. ให้มี bot session ที่ Open อย่างน้อย 3 ถ้าโต๊ะว่างไม่พอก็ข้ามขั้นนี้
  2. เลือก bot session ที่ยังไม่หมดเวลามาหนึ่งอัน แล้วสั่ง 2–6 รายการแบบสุ่ม โดยเรียกผ่าน service เดียวกับ Step 6 (ห้ามเขียน DB ตรง)
  3. order ของ bot session ที่ค้างในสถานะเดิมเกิน 120 วินาที ให้เลื่อนไปหนึ่งขั้นผ่าน service ของ Step 7
  4. bot session ที่เลย `EndsAt` และไม่มี order ค้าง ให้จ่ายด้วย Cash ผ่าน service ของ Step 8

  bot ห้ามแตะ session ที่ `IsDemoBot=false` ทุก action ต้อง emit SignalR ตามปกติ ถ้า loop โยน exception ให้ log แล้วทำต่อ
- เพื่อให้ bot ใช้ logic ร่วมได้ Step 6/7/8 ต้องแยกเป็น service class (`OrderService`, `OrderWorkflowService`, `PaymentService`) แล้วให้ endpoint เรียก service เหล่านั้น

### Step 11 — Frontend foundation
- `pnpm create next-app frontend --ts --tailwind --app --eslint --src-dir --import-alias "@/*"` แล้ว `pnpm dlx shadcn@latest init` เพิ่ม component: button, card, dialog, sheet, input, label, select, tabs, badge, table, sonner, switch, textarea, dropdown-menu, skeleton
- Deps: `@tanstack/react-query`, `@microsoft/signalr`, `recharts`, `qrcode.react`, `promptpay-qr`, `zod`, `react-hook-form`, `@hookform/resolvers`, `lucide-react`
- `next.config.ts`: `output: "standalone"` ไม่ต้องตั้ง rewrites เพราะ Caddy ทำให้เป็น same origin แล้ว ต้องเปิดเว็บผ่าน `http://localhost:8080` เสมอ
- ฟอนต์ใช้ `IBM_Plex_Sans_Thai` จาก `next/font/google` (weights 400/500/600) ตั้ง `<html lang="th">`
- Theme แบบ Modern Minimal: พื้นหลังขาว/neutral-50 accent สีส้มแดงโทนชาบู (`--primary: oklch(0.62 0.19 35)`) radius 0.75rem และ mobile-first สีสถานะ: Pending=amber, Preparing=blue, Ready=green, Served=neutral, Cancelled=red ข้อความบน badge ต้องผ่านคอนทราสต์ WCAG AA
- `src/lib/api.ts`: `apiFetch<T>(path, init)` ใช้ `credentials: "include"` ถ้า response ไม่ ok ให้ parse ProblemDetails แล้ว throw `ApiError {code, status, message}` โดย UI แสดง `message` ผ่าน toast
- `src/lib/types.ts`: DTO ให้ตรงกับ backend
- `src/lib/realtime.ts`: `useLiveConnection({token?})` สร้าง `HubConnectionBuilder().withUrl("/hubs/live" + (token ? "?token="+token : "")).withAutomaticReconnect()` เมื่อ reconnect ให้ invalidate TanStack Query ทั้งหมด และแสดง badge "ออฟไลน์" ระหว่างที่หลุด ส่วน hook `useLiveEvent(name, handler)` ใช้ update/invalidate query
- `src/lib/format.ts`: `formatBaht` (`Intl.NumberFormat("th-TH",{style:"currency",currency:"THB"})`), `formatTime` ใช้ Asia/Bangkok และ countdown คำนวณจาก `endsAt` ปรับด้วย offset จาก `serverNow` ห้ามเรียก `Date.now()` ระหว่าง render ให้ใช้ state ที่ tick ใน `useEffect` interval 1 วินาที
- Auth guard: `src/app/(staff)/layout.tsx` เรียก `GET /api/auth/me` ถ้าได้ 401 ให้ไป `/login` และถ้า role เป็น Kitchen ห้ามเข้า `/owner/*` (redirect ไป `/kitchen`)

### Step 12 — Frontend: Customer `/s/[token]`
- เป็นหน้าเดียวแบบ mobile-first:
  - Header แสดงชื่อโต๊ะ, Package และนับถอยหลัง `mm:ss` (เปลี่ยนเป็นสีแดงพร้อมแบนเนอร์ "Last order" เมื่อเหลือไม่เกิน warning minutes ถ้าหมดเวลาแสดง "หมดเวลาสั่งอาหาร" และ disable การสั่ง)
  - Category tabs แบบ sticky และเลื่อนแนวนอนได้
  - การ์ดเมนูมีรูปหรือ placeholder ตัวอักษรแรกบนพื้นสีของหมวด Buffet แสดง "รวมในบุฟเฟ่ต์" ส่วน AlaCarte แสดงราคา item ที่หมดแสดงสีเทาพร้อม badge "หมด"
  - ปุ่ม +/− จำกัดตาม MaxQty
- Cart เป็น bottom sheet: แก้จำนวน ใส่หมายเหตุต่อบรรทัด แสดงจำนวนรวมเทียบลิมิต แล้วกด "ส่งออเดอร์" ถ้าสำเร็จให้ล้างตะกร้าและ toast "ส่งออเดอร์รอบที่ N แล้ว" ตะกร้าเก็บใน `localStorage` key `fq-cart-{token}` แต่ละเครื่องแยกกัน ส่วนรายการ order ใช้ร่วมกันทั้งโต๊ะ
- Tab "ออเดอร์ของโต๊ะ" แสดง order ทุกรอบพร้อม badge สถานะ อัปเดตจาก `orderCreated`/`orderStatusChanged` ส่วน Tab "ยอดรวม" แสดง `BillDto`
- ถ้าเจอ `menuItemChanged` ให้ update ข้อมูลเมนู ถ้าเจอ `sessionClosed` หรือ API ตอบ `SESSION_CLOSED` ให้แสดงหน้า "ขอบคุณที่ใช้บริการ" แทนทั้งหน้า ถ้าเป็น `SESSION_NOT_FOUND` แสดง "QR ไม่ถูกต้อง"

### Step 13 — Frontend: Kitchen `/kitchen`
- เป็นบอร์ด 3 คอลัมน์ "รอรับ" (Pending), "กำลังทำ" (Preparing) และ "พร้อมเสิร์ฟ" (Ready) การ์ดแสดงโต๊ะ, รอบที่, เวลาที่ผ่านไป (เปลี่ยนเป็นสีแดงเมื่อเกิน 10 นาที) และรายการพร้อมหมายเหตุที่ highlight มีปุ่มใหญ่สำหรับขั้นถัดไป ("รับออเดอร์", "เสร็จแล้ว", "เสิร์ฟแล้ว") และเมนู ⋯ → "ยกเลิก" ที่เปิด dialog ให้ใส่เหตุผล
- เมื่อเจอ `orderCreated` ให้เล่นเสียงสั้นจาก `/sounds/new-order.mp3` (มีปุ่มเปิด/ปิดเสียงที่ต้องกดหนึ่งครั้งเพื่อปลด autoplay) แล้วไฮไลต์การ์ดใหม่ 3 วินาที ถ้าได้ `INVALID_STATUS_TRANSITION` ให้ refetch และ toast "ออเดอร์ถูกอัปเดตจากเครื่องอื่นแล้ว"
- Layout เต็มจอ ใช้ได้บน tablet แนวนอน มีแถบด้านบนแสดงตัวนับแต่ละคอลัมน์และสถานะการเชื่อมต่อ

### Step 14 — Frontend: Owner `/owner/*`
- Sidebar บน desktop และ bottom nav บน mobile เมนูได้แก่ ภาพรวม, โต๊ะ, ออเดอร์, เมนูอาหาร, แพ็กเกจ, รายงาน, ตั้งค่า, ครัว (→`/kitchen`)
- `/owner` (ภาพรวม): grid โต๊ะจาก `/api/tables/overview` โต๊ะว่างแสดงปุ่ม "เปิดโต๊ะ" เปิด dialog เลือก Package, จำนวนผู้ใหญ่/เด็ก แล้วสร้าง → แสดง QR (`qrcode.react`) และปุ่ม "พิมพ์ QR" ที่เปิด `/print/qr/[id]` ในแท็บใหม่ โต๊ะที่ไม่ว่างแสดง Package, จำนวนคน, เวลาที่เหลือ, order ค้าง และยอดสะสม คลิกแล้วไป `/owner/sessions/[id]` บนหัวหน้ามี KPI วันนี้ (ยอดขาย, บิล, ลูกค้า) อัปเดตจาก `sessionChanged`
- `/owner/sessions/[id]`:
  - รายการ order (ยกเลิกได้) และรายการ adjustment (เพิ่ม/ลบ)
  - ปุ่มต่อเวลา 15/30/60, ปุ่ม "พิมพ์ใบแจ้งยอด" และ "แสดง QR"
  - "รับชำระเงิน" เปิด dialog เลือกวิธีจ่าย ถ้าเป็น Cash ให้กรอกยอดรับแล้วแสดงเงินทอน ถ้าเป็น PromptPay แสดง QR จาก `promptpay-qr` ถ้าเป็น Card กดยืนยันได้เลย จ่ายสำเร็จแล้วแสดงปุ่ม "พิมพ์ใบเสร็จ"
  - ปุ่ม "ยกเลิกโต๊ะ" ใช้ void ได้เฉพาะกรณีไม่มี order
  - ถ้าได้ `ORDERS_IN_PROGRESS` ให้แสดงข้อความพร้อมรายการ order ที่ค้าง
- `/owner/orders`: ตาราง order วันนี้ filter ตามสถานะได้ และอัปเดต live
- `/owner/menu`: แยก tab หมวดหมู่และรายการ ฟอร์มเลือก Kind ได้ ถ้าเป็น Buffet ต้องเลือก MinPackage และซ่อนช่องราคา ถ้าเป็น AlaCarte ต้องกรอกราคา มีอัปโหลดรูป และ switch "หมด" ที่เปลี่ยนทันทีด้วย optimistic update
- `/owner/packages`: CRUD Package
- `/owner/tables`: CRUD โต๊ะ
- `/owner/reports`: ตัวเลือกช่วง วันนี้/7 วัน/30 วัน, KPI cards, Recharts ได้แก่ bar ยอดรายวัน, bar รายชั่วโมง, pie Package mix, pie วิธีจ่าย และตาราง Top 10 พร้อม empty state "ยังไม่มียอดขายในช่วงนี้"
- `/owner/settings`: ฟอร์ม settings ทั้งหมด
- `/owner/history`: รายการ session ที่ Paid เลือกวันได้ เปิดดูบิลและพิมพ์ใบเสร็จซ้ำได้

### Step 15 — Frontend: Print pages `/print/{prebill|receipt|qr}/[sessionId]`
- อยู่นอก staff layout (ไม่มี nav) แต่ยังต้อง auth ทั้งสามหน้าดึง `/api/sessions/{id}/print-data` CSS ใช้ `@page { size: 80mm auto; margin: 0 }` กว้าง 72mm ฟอนต์ 12px ตัวเลขชิดขวา หลัง data โหลดเสร็จให้เรียก `window.print()` ใน `useEffect` และมีปุ่ม "พิมพ์อีกครั้ง" ที่ซ่อนตอนพิมพ์
- Pre-bill: ชื่อร้าน, หัว "ใบแจ้งยอด (ยังไม่ใช่ใบเสร็จ)", โต๊ะ, เวลา, บรรทัดบิล, Service/VAT, ยอดรวม และ PromptPay QR (ถ้าตั้งค่า PromptPay ID ไว้)
- Receipt: ใช้ได้เฉพาะ session ที่ Paid ถ้ายังไม่ Paid ให้แสดง "ยังไม่ได้ชำระเงิน" มีชื่อร้าน, ที่อยู่, โทร, เลขผู้เสียภาษี (ถ้ามี), `ReceiptNo`, วันเวลา, บรรทัดบิล, วิธีจ่าย, รับเงิน, เงินทอน และ "ขอบคุณที่ใช้บริการ"
- QR slip: ชื่อร้าน, โต๊ะ, Package, จำนวนคน, เวลาเปิดและเวลาหมด, QR ขนาด 48mm และข้อความ "สแกนเพื่อสั่งอาหาร"

### Step 16 — Frontend: Login + Demo
- `/login`: ฟอร์มอีเมล/รหัสผ่าน ถ้า `GET /api/demo/status` คืน `{enabled:true}` (เพิ่ม endpoint นี้ใน Step 10 โดยไม่ต้อง auth) ให้แสดงการ์ด "ทดลองใช้งาน" มี 3 ปุ่ม: "เข้าใช้แบบเจ้าของร้าน" (→`/owner`), "เข้าใช้แบบครัว" (→`/kitchen`) และ "ดูมุมมองลูกค้า" (เรียก customer-session แล้วไป `/s/{token}`) พร้อมลิงก์ "ดูทั้ง 3 มุมมองพร้อมกัน" (→`/demo`)
- `/` redirect ไป `/login`
- `/demo`: เรียก `demo/login` role Owner (Owner มีสิทธิ์ kitchen อยู่แล้ว) และ `demo/customer-session` แล้ว render 3 iframe เรียงแนวนอนบน ≥1280px ได้แก่ กรอบมือถือ 390×844 (`/s/{token}`), `/kitchen` และ `/owner` ถ้าจอ <1280px ให้เปลี่ยนเป็น tabs มีหัวอธิบายสั้นว่า "Bot จำลองลูกค้าสั่งอาหารทุก 20–40 วินาที"

### Step 17 — Tests
- `backend/tests/FoodQueue.Api.Tests` ใช้ xUnit + `Testcontainers.PostgreSql` + `WebApplicationFactory<Program>` (เพิ่ม `public partial class Program;`) ตั้ง `Demo:Enabled=false` แล้ว seed เฉพาะที่แต่ละ test ต้องใช้ผ่าน helper `TestData`
  - `BillCalculatorTests` (unit): บุฟเฟ่ต์+เด็ก, ไม่นับ order ที่ยกเลิก, adjustment ติดลบ, ปัด SC/VAT (เช่น subtotal 1000.05 กับ SC 10% ต้องได้ service 100.01)
  - `OrderingTests`: Silver session สั่ง rank2 item → 422 `ITEM_NOT_IN_PACKAGE`, qty 6 → `QTY_LIMIT_EXCEEDED`, รวม 21 → `ORDER_ITEMS_LIMIT_EXCEEDED`, item ที่หมด → `ITEM_UNAVAILABLE`, หลัง EndsAt (ตั้ง EndsAt ย้อนอดีตใน DB) → `SESSION_EXPIRED`, session ที่ Paid → `SESSION_CLOSED` และสั่งสำเร็จ → Number เพิ่มเป็น 1,2
  - `WorkflowTests`: transition ข้ามขั้น (Pending→Ready) → 409, ยกเลิกออเดอร์ที่ Served → 409, ส่ง status พร้อมกันสองครั้ง → สำเร็จหนึ่ง และ 409 หนึ่ง
  - `SessionTests`: เปิดโต๊ะซ้ำ → `TABLE_OCCUPIED`, payment ขณะมี Pending → `ORDERS_IN_PROGRESS`, Cash ไม่พอ → `INSUFFICIENT_AMOUNT`, จ่ายสำเร็จ → ReceiptNo ตรง regex `^R\d{8}-\d{6}$` และ token ใช้ไม่ได้อีก
  - `AuthTests`: Kitchen เรียก `/api/settings` → 403, ไม่ได้ login → 401
- `frontend/e2e/flow.spec.ts` (Playwright, baseURL `http://localhost:8080`, ใช้ stack ที่เปิด demo): demo-login Owner → เปิดโต๊ะ A1 (ถ้า bot ใช้อยู่ให้เลือกโต๊ะว่างแรก) → อ่าน qrUrl → เปิด context ใหม่ไปที่ qrUrl → ใส่ "หมูสามชั้น" ×2 และ "ชาไทย" ×1 → ส่ง → ในแท็บ `/kitchen` ของ Owner การ์ดโต๊ะนั้นต้องโผล่ใน "รอรับ" ภายใน 5 วินาที → กดเลื่อนจนเสิร์ฟ → หน้าลูกค้าต้องเห็น "เสิร์ฟแล้ว" → Owner รับชำระ Cash → หน้าลูกค้าแสดง "ขอบคุณที่ใช้บริการ"

## Critical files & anchors
- `backend/src/FoodQueue.Api/Features/Orders/OrderService.cs`: เก็บลำดับการตรวจของ Step 6 ทั้งหมด และใช้ร่วมกับ DemoBot
- `backend/src/FoodQueue.Api/Billing/BillCalculator.cs`: สูตรบิลจุดเดียว ใช้ร่วมกันทั้ง customer bill, owner, payment และ print-data
- `backend/src/FoodQueue.Api/Realtime/LiveHub.cs`: กติกาเข้า group (token กับ role) ถ้าผิดลูกค้าจะเห็นออเดอร์โต๊ะอื่น
- `frontend/src/lib/realtime.ts`: จุดเดียวที่สร้าง SignalR connection และ invalidate query เมื่อ reconnect
- `Caddyfile`: ทำให้เป็น same origin ซึ่งเป็นเงื่อนไขของ cookie auth และ WebSocket

## Verification
1. Backend tests (ต้องมี Docker): `cd backend && dotnet test` ต้องผ่านทุก test ใน Step 17
2. Full stack: ที่ root รัน `cp .env.example .env && docker compose up --build -d` รอให้ `docker compose ps` ขึ้นว่าทุก service healthy/running แล้วเปิด `http://localhost:8080/login`
3. Smoke API:
   - `curl -s http://localhost:8080/api/demo/status` → `{"enabled":true}`
   - `curl -s -c c.txt -X POST localhost:8080/api/demo/login -H 'content-type: application/json' -d '{"role":"Owner"}' -o /dev/null -w '%{http_code}'` → `204`
   - `curl -s -b c.txt 'localhost:8080/api/reports/summary?range=30d'` → `billCount` มากกว่า 500 และ `daily` มี 30–31 แถว
4. Ordering rule แบบ end-to-end: เปิด Silver session ผ่าน `POST /api/sessions` ด้วย cookie ของ Owner แล้วใช้ token ส่ง `POST /api/s/{token}/orders` พร้อม Buffet item rank2 → 422 และ body มี `"code":"ITEM_NOT_IN_PACKAGE"`
5. Realtime ด้วยตาจริง: เปิด `/demo` ภายใน 40 วินาทีต้องมีการ์ด bot โผล่ในคอลัมน์ "รอรับ" ของ iframe ครัวโดยไม่ต้อง refresh และกดสั่งใน iframe ลูกค้าแล้วการ์ดต้องโผล่ใน iframe ครัวภายใน 2 วินาที
6. พิมพ์: เปิด `/print/receipt/{paidSessionId}` แล้วใช้ Chromium print preview ต้องเห็นกระดาษกว้าง 80mm ข้อความไทยไม่ถูกตัด และมีเลขใบเสร็จ
7. Playwright: `cd frontend && pnpm exec playwright install chromium && pnpm exec playwright test` โดย stack จากข้อ 2 ต้องรันอยู่
8. Mobile: ใช้ Playwright viewport 390×844 เปิด `/s/{token}` แล้วจับภาพหน้าเมนู ตะกร้า และสถานะ ตรวจว่าไม่มี horizontal scroll และปุ่มกดได้ขนาด ≥44px

## Assumptions & contingencies
- คำตอบเรื่อง QR ต่อ session บอกว่า "พิมพ์ slip" แต่ในรายการสิ่งที่ต้องพิมพ์ไม่ได้ติ๊ก QR slip แผนนี้จึงใส่ QR slip ไว้ด้วย (Step 15) และยังแสดง QR บนจอ Owner ได้เหมือนเดิม
- ถ้า `promptpay-qr@0.5.0` ใช้ใน Next 16 / ESM ไม่ได้ ให้เขียน `src/lib/promptpay.ts` เอง ใช้ EMVCo payload: AID `A000000677010111`, เบอร์โทรแปลงเป็น `0066` + ตัดเลข 0 ตัวแรก, currency `764`, country `TH` และ CRC16-CCITT (0xFFFF)
- ถ้า `host.docker.internal:host-gateway` ใช้ไม่ได้ใน dev บน Linux ให้ใช้ `network_mode: host` กับ caddy ใน `compose.dev.yaml` แล้วตั้ง upstream เป็น `localhost:5080` / `localhost:3000`
- ถ้า shadcn CLI ยังไม่รองรับ Tailwind 4.3 / Next 16 ให้ pin `next@15` กับ `tailwindcss@4` เวอร์ชันที่ shadcn init รองรับ แล้วจดเวอร์ชันลง README
- ใช้ Postgres image `postgres:17` ถ้าไม่มีให้ใช้ `postgres:16`
