# FoodQueue — ระบบสั่งอาหารผ่าน QR สำหรับร้านชาบู/บุฟเฟ่ต์

ระบบร้านเดียว (single-restaurant) UI ภาษาไทย mobile-first: ลูกค้าสแกน QR ของโต๊ะแล้วสั่งอาหารจากมือถือ ครัวเห็นออเดอร์แบบ real-time เจ้าของร้านดูแลโต๊ะ เมนู แพ็กเกจ บิล การชำระเงิน รายงาน และมี Demo Mode ไว้เดโม

เอกสาร: ข้อกำหนดเชิงพฤติกรรมอยู่ที่ `qr-buffet-ordering/spec.md` แผนที่อนุมัติที่ `qr-buffet-ordering/plan.md` (สำเนาเหมือนต้นฉบับ) หน้าจอที่ออกแบบที่ `design/` และ `DESIGN.md` คำในโดเมนที่ `GLOSSARY.md` การตัดสินใจเชิงสถาปัตยกรรมที่ `docs/adr/`

## รันทั้งระบบ (คำสั่งเดียว)

```bash
cp .env.example .env
docker compose up --build -d
```

รอจน `docker compose ps` ขึ้นว่า service ทุกตัว healthy/running แล้วเปิด **http://localhost:8080**

> ต้องเปิดผ่านพอร์ต 8080 ของ Caddy เสมอ ไม่ใช่ 3000 ของ Next โดยตรง เพราะ cookie auth และ WebSocket ของ SignalR พึ่งความ same-origin (ดู `docs/adr/0002-single-origin-caddy.md`)

ตรวจว่า API ต่อฐานข้อมูลได้: เปิด `http://localhost:8080/api/health` ต้องได้ `{"status":"ok"}`

## รันแบบพัฒนา (hot reload)

```bash
docker compose -f compose.yaml -f compose.dev.yaml up db caddy
cd backend/src/FoodQueue.Api && dotnet run     # http://localhost:5080
cd frontend && pnpm install && pnpm dev        # http://localhost:3000
```

## ทดสอบ

```bash
# seam 1: backend จริง + Postgres จริงผ่าน Testcontainers (ต้องมี Docker)
cd backend && dotnet test

# seam 2: Playwright บน stack ที่เปิด Demo (ต้องรัน docker compose ไว้ก่อน)
cd frontend && pnpm exec playwright install chromium && pnpm exec playwright test
```

## ข้อตกลง migration

มี migration ชื่อ **`Initial`** ตัวเดียว และ ticket ถัดไปต้อง **regenerate** (ลบแล้วสร้างใหม่) แทนการสะสม migration จนกว่าจะมีข้อมูลจริงที่ต้องรักษา:

```bash
cd backend
dotnet ef migrations remove --project src/FoodQueue.Api --startup-project src/FoodQueue.Api
dotnet ef migrations add Initial --project src/FoodQueue.Api --startup-project src/FoodQueue.Api --output-dir Data/Migrations
```

migration จะ apply เองตอน API สตาร์ท (`Database.MigrateAsync()`)

## สแกน QR ด้วยมือถือจริง

ค่าเริ่มต้นของ `PUBLIC_BASE_URL` คือ `http://localhost:8080` ซึ่งมือถือสแกนแล้วเปิดไม่ได้ ให้ตั้งเป็น IP ใน LAN ของเครื่องที่รันระบบ เช่น

```bash
PUBLIC_BASE_URL=http://192.168.1.20:8080
```

แล้วรัน compose ใหม่ ค่านี้ถูกใช้สร้างลิงก์ `/s/<token>` ใน QR ทุกอัน
