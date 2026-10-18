import type { Metadata } from "next";
import { PrebillScreen } from "./prebill";

export const metadata: Metadata = {
  title: "พิมพ์ใบแจ้งยอด · FoodQueue",
};

/** Every round is a different id, so this route cannot be prerendered (same escape hatch as `/s/[token]`). */
export const instant = false;

/**
 * `/print/prebill/[sessionId]` — frame `p1-print`, ใบซ้าย: ใบแจ้งยอดก่อนรับเงิน
 * ยอดทุกบรรทัดมาจากบิลของเซิร์ฟเวอร์ (ตัวคำนวณเดียวกับหน้าจอเจ้าของร้าน) และด้านล่างเป็น PromptPay QR
 * ของยอดนั้นพร้อมสแกน — ถ้ายังไม่ได้ตั้งค่า PromptPay ID จะไม่แสดง QR เปล่า แต่บอกให้รับชำระที่เคาน์เตอร์
 */
export default async function PrebillPage({ params }: PageProps<"/print/prebill/[sessionId]">) {
  const { sessionId } = await params;
  return (
    <PrebillScreen sessionId={Number(sessionId)} />
  );
}
