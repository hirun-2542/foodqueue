"use client";

import generatePayload from "promptpay-qr";
import { QRCodeSVG } from "qrcode.react";
import { formatBaht, formatTime } from "@/lib/format";
import { BillLines, BuffetLines, PaperRow, PrintScreen, ShopHeader } from "../../print-shell";

/**
 * `/print/prebill/[sessionId]` — frame `p1-print`, ใบซ้าย: ใบแจ้งยอดก่อนรับเงิน
 *
 * ยอดทุกบรรทัดมาจากบิลของเซิร์ฟเวอร์ (ตัวคำนวณเดียวกับหน้าจอเจ้าของร้าน) ผ่าน `?doc=prebill`
 * ด้านล่างเป็น PromptPay QR แบบ dynamic amount ที่สร้างจาก PromptPay ID ของร้านกับยอดรวม (spec กำหนดให้
 * สร้างฝั่ง frontend) — ถ้ายังไม่ได้ตั้ง ID จะไม่วาด QR เปล่า แต่บอกให้ไปรับชำระที่เคาน์เตอร์
 */
export function PrebillScreen({ sessionId }: { sessionId: number }) {
  return (
    <PrintScreen sessionId={sessionId} doc="prebill" caption="ใบแจ้งยอด">
      {(data) => {
        const { settings, bill } = data;
        const promptPayId = settings.promptPayId?.trim();

        return (
          <>
            <ShopHeader data={data} withTaxId={false} />
            <div className="dash" />
            <div className="doc-h">ใบแจ้งยอด (ยังไม่ใช่ใบเสร็จ)</div>
            <div className="pk">
              <span>
                โต๊ะ {data.tableName} · {data.packageName}
              </span>
              <span>
                {bill.adults} ผู้ใหญ่
                {bill.children > 0 ? ` ${bill.children} เด็ก` : ""}
              </span>
            </div>
            <div className="pk">
              <span>เปิด</span>
              <span className="mono">{formatTime(data.openedAt)}</span>
            </div>
            <div className="dash" />
            <BuffetLines data={data} />
            <BillLines data={data} />
            <div className="dash" />
            {promptPayId ? (
              <>
                <div className="qr">
                  <QRCodeSVG
                    value={generatePayload(promptPayId, { amount: bill.total })}
                    size={132}
                    level="M"
                    marginSize={2}
                    bgColor="#ffffff"
                    fgColor="#111111"
                    title={`PromptPay QR ยอด ${formatBaht(bill.total)}`}
                  />
                </div>
                <div className="doc-h2">สแกนเพื่อชำระ</div>
                <div className="doc-c">
                  PromptPay <span className="mono">{promptPayId}</span>
                </div>
              </>
            ) : (
              <PaperRow label="รับชำระที่เคาน์เตอร์" value="" />
            )}
          </>
        );
      }}
    </PrintScreen>
  );
}
