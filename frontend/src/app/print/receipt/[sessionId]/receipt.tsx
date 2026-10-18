"use client";

import { formatBaht, formatDate, formatTime } from "@/lib/format";
import { paymentMethodLabel } from "@/lib/print";
import { BillLines, BuffetLines, PaperRow, PrintScreen, ShopHeader } from "../../print-shell";

/**
 * `/print/receipt/[sessionId]` — frame `p1-print`, ใบกลาง: ใบเสร็จรับเงิน
 *
 * หัวร้านพร้อมเลขผู้เสียภาษี · เลขที่ใบเสร็จและวันเวลาที่ชำระ · บิลแยกบรรทัด · วิธีจ่าย รับเงิน เงินทอน ·
 * "ขอบคุณที่ใช้บริการ". เซิร์ฟเวอร์ยอมให้เปิดหน้านี้เฉพาะบิลที่ Paid (ไม่งั้น `SESSION_CLOSED`)
 * จึงมี `payment` มาเสมอ — ตรวจไว้อีกชั้นเพื่อให้ตรงกับสัญญาของ DTO และพิมพ์ซ้ำได้ทุกเมื่อ
 */
export function ReceiptScreen({ sessionId }: { sessionId: number }) {
  return (
    <PrintScreen sessionId={sessionId} doc="receipt" caption="ใบเสร็จ">
      {(data) => {
        const { payment } = data;

        return (
          <>
            <ShopHeader data={data} withTaxId />
            <div className="dash" />
            <div className="doc-h">ใบเสร็จรับเงิน</div>
            {payment ? (
              <>
                <PaperRow label="เลขที่" value={<span className="mono">{payment.receiptNo}</span>} />
                <PaperRow
                  label="วันเวลา"
                  value={
                    <span>
                      {formatDate(payment.paidAt)} <span className="mono">{formatTime(payment.paidAt)}</span>
                    </span>
                  }
                />
              </>
            ) : null}
            <div className="pk">
              <span>
                โต๊ะ {data.tableName} · {data.packageName}
              </span>
              <span>
                {data.bill.adults} ผู้ใหญ่
                {data.bill.children > 0 ? ` ${data.bill.children} เด็ก` : ""}
              </span>
            </div>
            <div className="pk">
              <span>เปิด</span>
              <span className="mono">{formatTime(data.openedAt)}</span>
            </div>
            <div className="dash" />
            <BuffetLines data={data} />
            <BillLines data={data} />
            {payment ? (
              <>
                <div className="dash" />
                <PaperRow label="วิธีจ่าย" value={paymentMethodLabel[payment.method]} />
                <PaperRow
                  label="รับเงิน"
                  value={<span className="num">{formatBaht(payment.amountReceived)}</span>}
                />
                <PaperRow label="เงินทอน" value={<span className="num">{formatBaht(payment.change)}</span>} />
                <div className="dash" />
                <div className="doc-h2">ขอบคุณที่ใช้บริการ</div>
              </>
            ) : null}
          </>
        );
      }}
    </PrintScreen>
  );
}
