"use client";

import { PrinterIcon } from "lucide-react";
import Link from "next/link";
import { type ReactNode, useEffect, useRef } from "react";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api";
import { formatBaht, formatBahtWhole } from "@/lib/format";
import { usePrintData } from "@/lib/print";
import type { PrintDataDto, PrintDocument } from "@/lib/types";
import "./print.css";

/**
 * เปลือกของหน้าพิมพ์ทั้งสาม (`/print/qr|prebill|receipt/[sessionId]`) — frame `p1-print`.
 *
 * อยู่นอกกรอบพนักงานของ `(staff)` โดยเจตนา: หน้านี้คือกระดาษ ไม่มีเมนูข้าง toolbar มีปุ่มเดียวคือ
 * "พิมพ์อีกครั้ง" (ซ่อนเมื่อพิมพ์จริงด้วย `@media print`) และเรียก `window.print()` เองหลังข้อมูลมาถึง
 * เพราะผู้ใช้มาที่นี่จากปุ่มพิมพ์ของหน้าอื่น (ticket 07 QR dialog · ticket 16 หน้าบิล · ticket 18 ใบเสร็จ)
 *
 * เงื่อนไขของเอกสารอยู่ที่เซิร์ฟเวอร์: ใบเสร็จของรอบที่ยังไม่ Paid ถูกปฏิเสธด้วย `SESSION_CLOSED`
 * หน้าจอจึงแสดงข้อความไทยของเซิร์ฟเวอร์ตรง ๆ ไม่เดาเงื่อนไขเอง
 */
export function PrintScreen({
  sessionId,
  doc,
  caption,
  children,
}: {
  sessionId: number;
  doc: PrintDocument;
  caption: string;
  children: (data: PrintDataDto, serverNow: number) => ReactNode;
}) {
  const query = usePrintData(sessionId, doc);
  const printed = useRef(false);

  // เปิดกล่องพิมพ์เองหนึ่งครั้งต่อการโหลดหน้า (หน้านี้ถูกเปิดด้วย target=_blank จากปุ่มพิมพ์)
  useEffect(() => {
    if (!query.data || printed.current) return undefined;
    printed.current = true;
    const timer = window.setTimeout(() => window.print(), 250);
    return () => window.clearTimeout(timer);
  }, [query.data]);

  if (query.isPending) {
    return (
      <div className="print-screen">
        <p className="p-6 text-body text-muted-foreground">กำลังเตรียม{caption}…</p>
      </div>
    );
  }

  if (query.isError) {
    return (
      <div className="print-screen">
        <div className="flex max-w-md flex-col items-start gap-3 p-6">
          <h1 className="text-title">พิมพ์{caption}ไม่ได้</h1>
          <p className="text-body text-muted-foreground">
            {query.error instanceof ApiError
              ? query.error.message
              : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
          </p>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => void query.refetch()}>
              ลองอีกครั้ง
            </Button>
            <Button asChild variant="ghost">
              <Link href="/owner" prefetch={false}>
                กลับหน้าภาพรวม
              </Link>
            </Button>
          </div>
        </div>
      </div>
    );
  }

  const { data, serverNow } = query.data;

  return (
    <div className="print-screen">
      <div className="print-toolbar">
        <div className="flex flex-col">
          <b className="text-body">{caption}</b>
          <span className="text-[13px] text-muted-foreground">
            กระดาษความร้อน 80 มม. · พิมพ์จากเบราว์เซอร์ได้ทันที ไม่ต้องติดตั้งไดรเวอร์ · ไม่มีใบสั่งครัว
          </span>
        </div>
        <Button onClick={() => window.print()}>
          <PrinterIcon aria-hidden />
          พิมพ์อีกครั้ง
        </Button>
      </div>
      <div className="print-sheet">
        <div className="paper">{children(data, serverNow)}</div>
      </div>
    </div>
  );
}

/** หัวร้านของทั้งสามเอกสาร: ชื่อ ที่อยู่ โทร (และเลขผู้เสียภาษีเมื่อใบเสร็จมี) */
export function ShopHeader({ data, withTaxId }: { data: PrintDataDto; withTaxId: boolean }) {
  const { settings } = data;
  return (
    <div className="shop">
      <b>{settings.name}</b>
      <span>{settings.address}</span>
      <span>
        โทร <span className="mono">{settings.phone}</span>
      </span>
      {withTaxId && settings.taxId ? (
        <span>
          เลขผู้เสียภาษี <span className="mono">{settings.taxId}</span>
        </span>
      ) : null}
    </div>
  );
}

/** หนึ่งบรรทัดของสลิป: ข้อความซ้าย · ยอด/ค่าขวา (tabular) */
export function PaperRow({ label, value }: { label: ReactNode; value: ReactNode }) {
  return (
    <div className="pk">
      <span>{label}</span>
      <span className="num">{value}</span>
    </div>
  );
}

/** กลุ่มรายการบนสลิป เช่น บุฟเฟ่ต์ / เมนูคิดเงินแยก / ปรับยอด */
export function PaperGroup({ children }: { children: ReactNode }) {
  return <div className="doc-g">{children}</div>;
}

/** บรรทัดบุฟเฟ่ต์ของทั้งสามเอกสาร: ผู้ใหญ่ n × ฿399 → ฿798.00 และบรรทัดเด็กเมื่อมี */
export function BuffetLines({ data }: { data: PrintDataDto }) {
  const { bill } = data;
  return (
    <>
      <PaperGroup>บุฟเฟ่ต์</PaperGroup>
      <PaperRow
        label={
          <>
            ผู้ใหญ่ {bill.adults} × <span className="mono">{formatBahtWhole(bill.adultPrice)}</span>
          </>
        }
        value={<span className="num">{formatBaht(bill.adults * bill.adultPrice)}</span>}
      />
      {bill.children > 0 ? (
        <PaperRow
          label={
            <>
              เด็ก {bill.children} × <span className="mono">{formatBahtWhole(bill.childPrice)}</span>
            </>
          }
          value={<span className="num">{formatBaht(bill.children * bill.childPrice)}</span>}
        />
      ) : null}
    </>
  );
}

/** รายการที่คิดเงินแยก รายการปรับยอด และยอดรวมค่าบริการ/VAT ของทั้งสามเอกสาร */
export function BillLines({ data }: { data: PrintDataDto }) {
  const { bill } = data;
  return (
    <>
      {bill.alaCarteLines.length > 0 ? (
        <>
          <PaperGroup>เมนูคิดเงินแยก</PaperGroup>
          {bill.alaCarteLines.map((line) => (
            <PaperRow
              key={`${line.name}-${line.unitPrice}`}
              label={
                <>
                  {line.name} {line.quantity} × <span className="mono">{formatBahtWhole(line.unitPrice)}</span>
                </>
              }
              value={<span className="num">{formatBaht(line.amount)}</span>}
            />
          ))}
        </>
      ) : null}

      <PaperGroup>ปรับยอด</PaperGroup>
      {bill.adjustments.length === 0 ? (
        <div className="doc-g">ไม่มีรายการปรับยอด</div>
      ) : (
        bill.adjustments.map((adjustment) => (
          <PaperRow
            key={adjustment.id}
            label={adjustment.description}
            value={
              <span className="num">
                {adjustment.amount > 0 ? "+" : ""}
                {formatBaht(adjustment.amount)}
              </span>
            }
          />
        ))
      )}

      <div className="dash" />
      <PaperRow label="ยอดก่อนค่าบริการ" value={<span className="num">{formatBaht(bill.subtotal)}</span>} />
      <PaperRow
        label={`ค่าบริการ ${bill.serviceChargePercent}%`}
        value={<span className="num">{formatBaht(bill.serviceCharge)}</span>}
      />
      <PaperRow label={`VAT ${bill.vatPercent}%`} value={<span className="num">{formatBaht(bill.vat)}</span>} />
      <div className="dash" />
      <div className="pk total">
        <span>รวมทั้งสิ้น</span>
        <span className="num">{formatBaht(bill.total)}</span>
      </div>
    </>
  );
}
