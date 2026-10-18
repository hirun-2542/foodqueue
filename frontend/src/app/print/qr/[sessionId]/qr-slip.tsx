"use client";

import { QRCodeSVG } from "qrcode.react";
import { formatDate, formatTime } from "@/lib/format";
import { PrintScreen, ShopHeader } from "../../print-shell";

/**
 * `/print/qr/[sessionId]` — frame `p1-print`, slip ขวา: โต๊ะ · แพ็กเกจและจำนวนคน · เวลาเปิด–หมดเวลา ·
 * QR ที่ลูกค้าสแกน (URL จริงจากเซิร์ฟเวอร์ ไม่ใช่รูปประกอบ) · ลิมิตต่อรอบจากค่าตั้งของร้าน.
 *
 * QR พิมพ์ที่ 185px = 37 โมดูล × 5 พอดี (ขนาดที่ mockup วางไว้) และมี quiet zone 2 โมดูล
 * เปิดได้เฉพาะรอบที่ยังไม่ปิดบิล — เซิร์ฟเวอร์ปฏิเสธด้วย `SESSION_CLOSED` เมื่อบิลปิดแล้ว
 */
export function QrSlipScreen({ sessionId }: { sessionId: number }) {
  return (
    <PrintScreen sessionId={sessionId} doc="qr" caption="QR slip">
      {(data, serverNow) => {
        const { settings } = data;
        // frame p1 ย่อ URL บนกระดาษ เพราะ token 43 ตัวไม่พอดีความกว้าง 72 มม.
        const shortUrl = data.qrUrl.replace(/^https?:\/\/[^/]+/, "…");
        const displayUrl = shortUrl.length > 24 ? `${shortUrl.slice(0, 21)}…` : shortUrl;
        return (
          <>
            <ShopHeader data={data} withTaxId={false} />
            <div className="dash" />
            <div className="doc-table">โต๊ะ {data.tableName}</div>
            <div className="doc-c">
              {data.packageName} · {data.adults} ผู้ใหญ่
              {data.children > 0 ? ` ${data.children} เด็ก` : ""}
            </div>
            <div className="dash" />
            <div className="pk">
              <span>เปิด</span>
              <span className="mono">{formatTime(data.openedAt)}</span>
            </div>
            <div className="pk">
              <span>หมดเวลา</span>
              <span className="mono">{formatTime(data.endsAt)}</span>
            </div>
            <div className="dash" />
            <div className="qr">
              <QRCodeSVG
                value={data.qrUrl}
                size={185}
                level="M"
                marginSize={2}
                bgColor="#ffffff"
                fgColor="#111111"
                title={`QR สั่งอาหารของโต๊ะ ${data.tableName}`}
              />
            </div>
            <div className="doc-h2">สแกนเพื่อสั่งอาหาร</div>
            <div className="doc-c mono">{displayUrl}</div>
            <div className="dash" />
            <div className="doc-c">QR ใช้ได้จนกว่าจะปิดบิล</div>
            <div className="doc-c">
              ลิมิต {settings.maxQtyPerItemPerOrder} ต่อเมนู · {settings.maxItemsPerOrder} รายการต่อรอบ
            </div>
            <div className="dash" />
            <div className="doc-c">
              พิมพ์เมื่อ {formatDate(serverNow)} <span className="mono">{formatTime(serverNow)}</span>
            </div>
          </>
        );
      }}
    </PrintScreen>
  );
}
