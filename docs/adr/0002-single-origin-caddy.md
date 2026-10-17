# ADR 0002: ให้ web, /api และ /hubs อยู่ origin เดียวผ่าน Caddy

## สถานะ
ยอมรับแล้ว

## บริบท
ระบบมีทั้งเว็บ (Next.js), API (ASP.NET Core) และ realtime hub (SignalR) ถ้าให้เบราว์เซอร์เรียกคนละ origin จะต้องตั้ง CORS พร้อมส่ง cookie ข้ามโดเมน และต้องพึ่ง rewrite ของ Next เพื่อ proxy WebSocket ซึ่งเปราะและตั้งค่าเยอะ

## การตัดสินใจ
ใช้ Caddy เป็น reverse proxy ตัวเดียว ให้ทุกอย่างอยู่ใต้ `http://localhost:8080` โดยแยกด้วย path: `/api/*` และ `/hubs/*` ไปที่ API ที่เหลือไปที่เว็บ

## ผลที่ตามมา
- cookie auth และ WebSocket ทำงานแบบ same-origin ไม่ต้องมี CORS และไม่ต้อง proxy WebSocket ผ่าน Next
- ต้องเปิดเว็บผ่านพอร์ต 8080 เสมอ ไม่ใช่ 3000 ตรง ๆ และ Caddy กลายเป็นจุดที่ต้องตั้งค่าให้ถูก
- รันแบบ local ด้วย docker compose เท่านั้น ไม่มี HTTPS หรือ certificate จริง
