import type { Metadata } from "next";
import { QrSlipScreen } from "./qr-slip";

export const metadata: Metadata = {
  title: "พิมพ์ QR slip · FoodQueue",
};

/** Every round is a different id, so this route cannot be prerendered (same escape hatch as `/s/[token]`). */
export const instant = false;

/**
 * `/print/qr/[sessionId]` — frame `p1-print`, slip ขวา: โต๊ะ แพ็กเกจ จำนวนคน เวลาเปิด–หมดเวลา และ QR
 * ที่ลูกค้าสแกน (URL จริงจากเซิร์ฟเวอร์ ไม่ใช่รูปประกอบ) พร้อมลิมิตต่อรอบจากค่าตั้งของร้าน
 * เปิดได้เฉพาะรอบที่ยังไม่ปิดบิล — เซิร์ฟเวอร์ปฏิเสธด้วย `SESSION_CLOSED` เมื่อบิลปิดแล้ว
 */
export default async function QrSlipPage({ params }: PageProps<"/print/qr/[sessionId]">) {
  const { sessionId } = await params;
  return <QrSlipScreen sessionId={Number(sessionId)} />;
}
