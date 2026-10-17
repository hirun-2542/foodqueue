# 25: Bot จำลองลูกค้าสั่งอาหารและปิดบิลเอง

Status: ready-for-agent

Source: `../spec.md` (user stories 107, 110) และ `../plan.md` (Step 10 (bot)); ถ้า spec กับ plan ขัดกัน ให้ยึด spec

## What to build

เมื่อเปิด Demo มี bot วนทุก 20–40 วินาที ดูแลให้มี session ของ bot เปิดอย่างน้อย 3 โต๊ะ สั่งอาหาร 2–6 รายการ เลื่อนสถานะออเดอร์ที่ค้างเกิน 120 วินาที (ถึง Ready) และจ่ายเงินสดให้ session ที่หมดเวลาและไม่มีออเดอร์ Pending/Preparing ผ่านบริการชุดเดียวกับที่ endpoint ใช้ (กฎและ event เหมือนของจริง) แตะเฉพาะ session ที่ถูกทำเครื่องหมายเป็นของ bot error ในรอบหนึ่งต้อง log แล้วทำรอบถัดไปต่อ

## Blocked by

24 Demo Mode

## Acceptance criteria

- [ ] เรียกผ่านบริการชุดเดียวกัน ไม่เขียนฐานข้อมูลตรง (เทสต์ seam 1: ได้ event ครบ)
- [ ] ไม่แตะ session ที่ไม่ใช่ของ bot (โต๊ะที่คนจริงเปิด) และเปิดโต๊ะที่คนจริงเปิดอยู่ไม่ได้
- [ ] รอบที่เกิดข้อผิดพลาดถูก log และรอบถัดไปทำงานต่อ; ปิด Demo แล้วไม่มี bot
- [ ] ในการรันจริง เห็นการ์ดจากบอทเข้าจอครัวและป๊อปอัป owner (ticket 15)
- [ ] สร้างครบทุกชั้นที่ slice นี้แตะ (schema/API/UI) พร้อมเทสต์ seam 1 ตามหัวข้อ Testing Decisions ของ spec และ smoke run บน stack จริง (`docker compose up`) ที่เห็นพฤติกรรมของ ticket นี้ด้วยตา; โมเดลที่เพิ่มเข้า migration `Initial` ตัวเดียว (ดู ticket 01)
