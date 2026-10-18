"use client";

import { CalendarIcon, ReceiptTextIcon, ScrollTextIcon } from "lucide-react";
import Link from "next/link";
import { type ReactNode, useState } from "react";
import { EmptyState } from "@/components/empty-state";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { useServerTime } from "@/lib/cart";
import { bangkokDay, formatBaht, formatDate, formatDayHeading, formatTime } from "@/lib/format";
import { paymentMethodLabel, usePaidHistory } from "@/lib/print";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import { useSessionDetail } from "@/lib/sessions";
import type { PaidBillDto, PaymentMethod, SessionDetailDto } from "@/lib/types";

type MethodFilter = PaymentMethod | "all";

const METHOD_FILTERS: { value: MethodFilter; label: string }[] = [
  { value: "all", label: "ทั้งหมด" },
  { value: "Cash", label: "เงินสด" },
  { value: "PromptPay", label: "PromptPay" },
  { value: "Card", label: "บัตร" },
];

/**
 * `/owner/history` — frame `o15-history`: บิลที่ชำระแล้วของวันหนึ่ง (ตามเวลาไทย) เลือกแถวแล้วดู
 * รายละเอียดบิลทางขวา พร้อม "พิมพ์ใบเสร็จซ้ำ" ที่เปิดหน้าพิมพ์ 80 มม. ของ ticket 20
 *
 * วันตั้งต้นคือ "วันนี้" ตามนาฬิกาของเซิร์ฟเวอร์ (`usePaidHistory` คืน `serverNow`) ไม่ใช่นาฬิกาเครื่อง
 * และตัวกรองวิธีจ่ายเป็นการกรองฝั่งจอจากแถวของวันนั้น ไม่ยิงคำขอใหม่: ตัวเลขท้ายตารางจึงตรงกับที่เห็น
 */
export function HistoryScreen() {
  return (
    <LiveProvider>
      <HistoryBody />
    </LiveProvider>
  );
}

function HistoryBody() {
  // วันที่: null = "ให้เซิร์ฟเวอร์บอกวันนี้ตามเวลาไทย" (ไม่เชื่อนาฬิกาเครื่อง) · เลือกวันเองแล้วจึงระบุชัด
  const [pickedDay, setPickedDay] = useState<string | null>(null);
  const [filter, setFilter] = useState<MethodFilter>("all");
  const history = usePaidHistory(pickedDay);
  const isOffline = useLiveOffline();
  const serverNow = history.data?.serverNow;
  const now = useServerTime(serverNow ?? 0);
  const day = pickedDay ?? (serverNow == null ? null : bangkokDay(serverNow));

  return (
    <div className="flex flex-col pb-6 lg:pt-[18px]">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col">
          <h1 className="text-headline">ประวัติการชำระ</h1>
          <span className="text-label font-normal text-muted-foreground">
            {serverNow == null ? "บิลที่ชำระแล้วของวันนี้" : `${formatDayHeading(now)} · ${formatTime(now)}`}
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <MethodFilters bills={history.data?.bills ?? []} filter={filter} onPick={setFilter} />
          {/* ก่อนรู้วันของเซิร์ฟเวอร์ยังไม่มีวันให้เลือก — ปุ่มนี้จึงโผล่พร้อมคำตอบแรกเท่านั้น */}
          {day == null ? null : <DayPicker day={day} onPick={setPickedDay} />}
        </div>
      </div>

      {history.isPending ? (
        <div className="mt-5 grid gap-5 lg:grid-cols-[1fr_420px]">
          <Skeleton className="h-96 rounded-xl" />
          <Skeleton className="h-96 rounded-xl" />
        </div>
      ) : history.isError ? (
        <div className="mt-5 flex flex-col items-start gap-3">
          <p className="text-body text-muted-foreground">
            {history.error instanceof ApiError
              ? history.error.message
              : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
          </p>
          <Button variant="outline" onClick={() => void history.refetch()}>
            ลองอีกครั้ง
          </Button>
        </div>
      ) : (
        <PaidBills bills={history.data.bills} day={day} filter={filter} isOffline={isOffline} />
      )}
    </div>
  );
}

function PaidBills({
  bills,
  day,
  filter,
  isOffline,
}: {
  bills: PaidBillDto[];
  /** วันที่ของรายการที่โหลดมา (จากเซิร์ฟเวอร์) — ใช้ในข้อความว่างเท่านั้น */
  day: string | null;
  filter: MethodFilter;
  isOffline: boolean;
}) {
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const shown = filter === "all" ? bills : bills.filter((bill) => bill.method === filter);
  const selected = shown.find((bill) => bill.sessionId === selectedId) ?? shown[0] ?? null;

  if (bills.length === 0) {
    return (
      <div className="mt-5">
        <EmptyState
          icon={<ReceiptTextIcon className="size-5" strokeWidth={1.75} aria-hidden />}
          title="ยังไม่มีบิลที่ชำระในวันนี้"
          description={
            day == null
              ? "วันนี้ยังไม่มีบิลที่ปิดและรับเงินแล้ว — เลือกวันอื่นจากปุ่มวันที่ด้านบน"
              : `${formatDate(day)} ไม่มีบิลที่ปิดและรับเงินแล้ว — เลือกวันอื่นจากปุ่มวันที่ด้านบน`
          }
        />
      </div>
    );
  }

  return (
    <>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-title">บิลที่ชำระแล้ว</h2>
        <span className="text-label font-normal text-muted-foreground">
          {shown.length} รายการ · รวม{" "}
          <b className="tabular-nums">{formatBaht(shown.reduce((sum, bill) => sum + bill.total, 0))}</b>
        </span>
      </div>

      {isOffline ? (
        <p role="status" className="mt-3 text-label font-normal text-muted-foreground">
          ออฟไลน์ · ข้อมูลอาจไม่ใหม่
        </p>
      ) : null}

      {shown.length === 0 ? (
        <p className="mt-4 rounded-xl border border-dashed border-line-strong p-4 text-body text-muted-foreground">
          วันนี้ไม่มีบิลที่จ่ายด้วยวิธีนี้
        </p>
      ) : (
        <div className="mt-3 grid items-start gap-5 lg:grid-cols-[1fr_420px]">
          <div className="overflow-hidden rounded-xl border border-border bg-surface">
            <table className="hidden w-full table-fixed text-left md:table">
              <thead>
                <tr className="border-b border-border bg-sunken text-label text-muted-foreground">
                  <th className="h-[42px] px-3.5 font-semibold">เลขที่ใบเสร็จ</th>
                  <th className="h-[42px] w-14 px-3.5 font-semibold">โต๊ะ</th>
                  <th className="h-[42px] w-16 px-3.5 font-semibold">เวลา</th>
                  <th className="h-[42px] w-12 px-3.5 font-semibold">คน</th>
                  <th className="h-[42px] w-24 px-3.5 font-semibold">แพ็กเกจ</th>
                  <th className="h-[42px] w-28 px-3.5 font-semibold">วิธีจ่าย</th>
                  <th className="h-[42px] px-3.5 text-right font-semibold">ยอด</th>
                </tr>
              </thead>
              <tbody>
                {shown.map((bill) => (
                  <tr
                    key={bill.sessionId}
                    aria-selected={bill.sessionId === selected?.sessionId}
                    className={`cursor-pointer border-b border-border last:border-b-0 ${
                      bill.sessionId === selected?.sessionId ? "bg-orange-soft" : "hover:bg-sunken"
                    }`}
                    onClick={() => setSelectedId(bill.sessionId)}
                  >
                    <td className="px-3.5 py-3.5">
                      <span className="font-mono text-[13px]">{bill.receiptNo}</span>
                    </td>
                    <td className="px-3.5 py-3.5">
                      <button
                        type="button"
                        className="font-mono text-[13px] font-semibold underline-offset-4 hover:underline"
                        onClick={(event) => {
                          event.stopPropagation();
                          setSelectedId(bill.sessionId);
                        }}
                      >
                        {bill.tableName}
                      </button>
                    </td>
                    <td className="px-3.5 py-3.5">
                      <span className="font-mono text-[13px]">{formatTime(bill.paidAt)}</span>
                    </td>
                    <td className="px-3.5 py-3.5 tabular-nums">{bill.guests}</td>
                    <td className="px-3.5 py-3.5">
                      <Badge variant="neutral" className="before:hidden">
                        {bill.packageName}
                      </Badge>
                    </td>
                    <td className="px-3.5 py-3.5">
                      <Badge variant="neutral" className="before:hidden">
                        {paymentMethodLabel[bill.method]}
                      </Badge>
                    </td>
                    <td className="px-3.5 py-3.5 text-right font-semibold tabular-nums">
                      {formatBaht(bill.total)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {/* บนมือถือตาราง 7 คอลัมน์อ่านไม่ได้ → แสดงเป็นรายการ (ยังเลือกดูรายละเอียดได้เหมือนกัน) */}
            <ul className="md:hidden">
              {shown.map((bill) => (
                <li key={bill.sessionId} className="border-b border-border last:border-b-0">
                  <button
                    type="button"
                    aria-pressed={bill.sessionId === selected?.sessionId}
                    onClick={() => setSelectedId(bill.sessionId)}
                    className={`flex w-full flex-col gap-2 p-3.5 text-left ${
                      bill.sessionId === selected?.sessionId ? "bg-orange-soft" : ""
                    }`}
                  >
                    <span className="flex items-center justify-between gap-3">
                      <span className="font-mono text-[13px]">{bill.receiptNo}</span>
                      <b className="tabular-nums">{formatBaht(bill.total)}</b>
                    </span>
                    <span className="flex flex-wrap items-center gap-2 text-label font-normal text-muted-foreground">
                      <b className="text-ink">โต๊ะ {bill.tableName}</b>
                      <span className="font-mono">{formatTime(bill.paidAt)}</span>
                      <span>{bill.guests} คน</span>
                      <Badge variant="neutral" className="before:hidden">
                        {bill.packageName}
                      </Badge>
                      <Badge variant="neutral" className="before:hidden">
                        {paymentMethodLabel[bill.method]}
                      </Badge>
                    </span>
                  </button>
                </li>
              ))}
            </ul>

            <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border p-3.5">
              <span className="text-label font-normal text-muted-foreground">
                แสดง {shown.length} จาก {bills.length} บิลของวันนี้
              </span>
              <span className="text-label font-normal text-muted-foreground">เรียงจากเวลาชำระล่าสุด</span>
            </div>
          </div>

          {selected ? <SelectedBill key={selected.sessionId} bill={selected} /> : null}
        </div>
      )}
    </>
  );
}

/**
 * รายละเอียดบิลของแถวที่เลือก — ตัวเลขมาจาก `GET /api/sessions/{id}` (ตัวคำนวณจุดเดียวกับหน้าจอบิล)
 * และ "พิมพ์ใบเสร็จซ้ำ" เปิดหน้าพิมพ์ 80 มม. ของ session นั้นเป็นแท็บใหม่
 */
function SelectedBill({ bill }: { bill: PaidBillDto }) {
  const detail = useSessionDetail(bill.sessionId);

  return (
    <section className="rounded-xl border border-border bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">เลขที่ใบเสร็จ</span>
          <b className="font-mono text-[15px] leading-snug">{bill.receiptNo}</b>
        </div>
        <Badge variant="neutral" className="before:hidden">
          ชำระแล้ว
        </Badge>
      </div>

      {detail.isPending ? (
        <Skeleton className="mt-4 h-72 rounded-lg" />
      ) : detail.isError ? (
        <p className="mt-4 text-body text-muted-foreground">
          {detail.error instanceof ApiError ? detail.error.message : "โหลดรายละเอียดบิลไม่ได้"}
        </p>
      ) : (
        <BillDetail session={detail.data.session} bill={bill} />
      )}
    </section>
  );
}

function BillDetail({ session, bill }: { session: SessionDetailDto; bill: PaidBillDto }) {
  const liveOrders = session.orders.filter((order) => order.status !== "Cancelled");
  const waiting = liveOrders.filter((order) => order.status !== "Ready").length;
  const row = "flex items-center justify-between gap-3 py-1 text-body";

  return (
    <>
      <hr className="my-3 border-border" />
      <div className="flex items-center justify-between gap-3">
        <span className="text-label font-normal text-muted-foreground">โต๊ะ</span>
        <span>
          <b>{bill.tableName}</b> · {session.packageName} · {session.adults} ผู้ใหญ่
          {session.children > 0 ? ` ${session.children} เด็ก` : ""}
        </span>
      </div>
      <div className="flex items-center justify-between gap-3">
        <span className="text-label font-normal text-muted-foreground">รอบออเดอร์</span>
        <span>
          {liveOrders.length} รอบ · {waiting === 0 ? "เสิร์ฟครบทุกจาน" : `ค้างครัว ${waiting} รอบ`}
        </span>
      </div>

      <hr className="mt-2 border-border" />

      <div className="mt-2 text-xs font-semibold text-muted-foreground">บุฟเฟ่ต์</div>
      <div className={row}>
        <span>
          ผู้ใหญ่ {session.bill.adults} ×{" "}
          <span className="tabular-nums">{formatBaht(session.bill.adultPrice)}</span>
        </span>
        <span className="tabular-nums">{formatBaht(session.bill.adults * session.bill.adultPrice)}</span>
      </div>
      {session.bill.children > 0 ? (
        <div className={row}>
          <span>
            เด็ก {session.bill.children} ×{" "}
            <span className="tabular-nums">{formatBaht(session.bill.childPrice)}</span>
          </span>
          <span className="tabular-nums">{formatBaht(session.bill.children * session.bill.childPrice)}</span>
        </div>
      ) : null}

      <div className="mt-2 text-xs font-semibold text-muted-foreground">เมนูคิดเงินแยก</div>
      {session.bill.alaCarteLines.length === 0 ? (
        <div className={`${row} text-muted-foreground`}>
          <span>ไม่มีเมนูคิดเงินแยก</span>
        </div>
      ) : (
        session.bill.alaCarteLines.map((line) => (
          <div key={`${line.name}-${line.unitPrice}`} className={row}>
            <span className="min-w-0">
              {line.name} {line.quantity} ×{" "}
              <span className="tabular-nums">{formatBaht(line.unitPrice)}</span>
            </span>
            <span className="tabular-nums">{formatBaht(line.amount)}</span>
          </div>
        ))
      )}

      <div className="mt-2 text-xs font-semibold text-muted-foreground">ปรับยอด</div>
      {session.bill.adjustments.length === 0 ? (
        <div className={`${row} text-muted-foreground`}>
          <span>ไม่มีรายการปรับยอด</span>
        </div>
      ) : (
        session.bill.adjustments.map((adjustment) => (
          <div key={adjustment.id} className={row}>
            <span className="min-w-0">{adjustment.description}</span>
            <span className="tabular-nums">
              {adjustment.amount > 0 ? "+" : ""}
              {formatBaht(adjustment.amount)}
            </span>
          </div>
        ))
      )}

      <div className="mt-2 flex items-center justify-between gap-3 border-t border-border pt-2.5 text-body">
        <span>ยอดก่อนค่าบริการ</span>
        <span className="tabular-nums">{formatBaht(session.bill.subtotal)}</span>
      </div>
      <div className={row}>
        <span>ค่าบริการ {session.bill.serviceChargePercent}%</span>
        <span className="tabular-nums">{formatBaht(session.bill.serviceCharge)}</span>
      </div>
      <div className={row}>
        <span>VAT {session.bill.vatPercent}%</span>
        <span className="tabular-nums">{formatBaht(session.bill.vat)}</span>
      </div>
      <div className="flex items-center justify-between gap-3 border-t border-border pt-3 text-title">
        <span>รวมทั้งสิ้น</span>
        <span className="tabular-nums">{formatBaht(session.bill.total)}</span>
      </div>

      {session.payment ? (
        <div className="mt-3 flex flex-col gap-1 rounded-lg bg-sunken p-3">
          <DetailRow label="วิธีจ่าย">
            <Badge variant="neutral" className="before:hidden">
              {paymentMethodLabel[session.payment.method]}
            </Badge>
          </DetailRow>
          <DetailRow label="รับเงิน">
            <span className="tabular-nums">{formatBaht(session.payment.amountReceived)}</span>
          </DetailRow>
          <DetailRow label="เงินทอน">
            <span className="tabular-nums">{formatBaht(session.payment.change)}</span>
          </DetailRow>
          <DetailRow label="ชำระเมื่อ">
            <span className="font-mono">{formatTime(session.payment.paidAt)}</span>
          </DetailRow>
        </div>
      ) : null}

      <div className="mt-4 flex flex-wrap gap-3">
        <Button asChild className="grow">
          <Link href={`/print/receipt/${bill.sessionId}`} prefetch={false} target="_blank" rel="noreferrer">
            <ScrollTextIcon className="size-4" aria-hidden />
            พิมพ์ใบเสร็จซ้ำ
          </Link>
        </Button>
        <Button asChild variant="outline" className="grow">
          <Link href={`/owner/sessions/${bill.sessionId}`} prefetch={false}>
            ดูรายการออเดอร์
          </Link>
        </Button>
      </div>
    </>
  );
}

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 text-body">
      <span className="text-label font-normal text-muted-foreground">{label}</span>
      {children}
    </div>
  );
}

/** ปุ่มกรองตามวิธีจ่ายของ frame o15 — นับจากแถวของวันนั้น กรองฝั่งจอ ไม่ยิงคำขอใหม่ */
function MethodFilters({
  bills,
  filter,
  onPick,
}: {
  bills: PaidBillDto[];
  filter: MethodFilter;
  onPick: (filter: MethodFilter) => void;
}) {
  return (
    <div role="group" aria-label="กรองตามวิธีจ่าย" className="flex flex-wrap gap-2">
      {METHOD_FILTERS.map((option) => (
        <FilterChip
          key={option.value}
          label={option.label}
          count={
            option.value === "all" ? bills.length : bills.filter((bill) => bill.method === option.value).length
          }
          on={filter === option.value}
          onSelect={() => onPick(option.value)}
        />
      ))}
    </div>
  );
}

function FilterChip({
  label,
  count,
  on,
  onSelect,
}: {
  label: string;
  count: number;
  on: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={on}
      onClick={onSelect}
      className={`inline-flex h-[34px] items-center gap-2 rounded-full border px-3.5 text-sm whitespace-nowrap outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
        on
          ? "border-orange-line bg-orange-soft font-semibold text-orange-ink"
          : "border-border bg-surface font-medium text-ink-2 hover:bg-sunken"
      }`}
    >
      {label}
      <b className="font-semibold tabular-nums">{count}</b>
    </button>
  );
}

/**
 * ปุ่มเลือกวันของ frame o15 — เมนูวันที่เป็นไทย (พ.ศ.) 14 วันย้อนหลังจากวันที่กำลังดูอยู่
 * ใช้เมนูของเราเองแทน `<input type="date">` เพราะ native input แสดงตาม locale ของเบราว์เซอร์
 * และจัดรูปแบบปีพุทธศักราชตามที่ frame เขียนไม่ได้
 *
 * วันตั้งต้นมาจากเซิร์ฟเวอร์ (`serverNow` ของหน้าประวัติ) จึงไม่เรียกนาฬิกาของเครื่องระหว่าง render
 */
function DayPicker({ day, onPick }: { day: string; onPick: (day: string) => void }) {
  const startOfDay = Date.parse(`${day}T00:00:00Z`);
  const days = Array.from({ length: 14 }, (_, back) => bangkokDay(startOfDay - back * 86_400_000));

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline">
          <CalendarIcon className="size-4" aria-hidden />
          {formatDate(day)}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {days.map((option) => (
          <DropdownMenuItem key={option} onSelect={() => onPick(option)}>
            {formatDate(option)}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
