import type { Metadata } from "next";
import { ReceiptScreen } from "./receipt";

export const metadata: Metadata = {
  title: "พิมพ์ใบเสร็จ · FoodQueue",
};

/** Every round is a different id, so this route cannot be prerendered (same escape hatch as `/s/[token]`). */
export const instant = false;

/**
 * `/print/receipt/[sessionId]` — frame `p1-print`, ใบกลาง: ใบเสร็จรับเงินที่มีเลขที่ วันเวลา และวิธีจ่าย
 * เข้าถึงได้เฉพาะ session ที่ Paid; รอบที่ยังไม่ชำระถูกปฏิเสธด้วย `SESSION_CLOSED` จากเซิร์ฟเวอร์
 * (หน้าจอนี้จึงไม่ต้องรู้เงื่อนไขเอง) · พิมพ์ซ้ำได้ทุกเมื่อจากหน้าประวัติการชำระ
 */
export default async function ReceiptPage({ params }: PageProps<"/print/receipt/[sessionId]">) {
  const { sessionId } = await params;
  return (
    <ReceiptScreen sessionId={Number(sessionId)} />
  );
}
