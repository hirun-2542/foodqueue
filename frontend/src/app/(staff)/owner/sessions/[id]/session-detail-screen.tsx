"use client";

import {
  BanIcon,
  ChevronRightIcon,
  CircleAlertIcon,
  FileTextIcon,
  QrCodeIcon,
  TimerOffIcon,
  Trash2Icon,
  UsersIcon,
} from "lucide-react";
import Link from "next/link";
import { type FormEvent, useId, useState } from "react";
import { TimeRail } from "@/components/time-rail";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { useServerTime } from "@/lib/cart";
import { formatBaht, formatBahtWhole, formatTime, minutesUsed, roundTime, type TimeTone } from "@/lib/format";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import {
  useAddAdjustment,
  useRemoveAdjustment,
  useSessionDetail,
  useVoidSession,
} from "@/lib/sessions";
import type { BillDto, OrderDto, OrderStatus, SessionDetailDto } from "@/lib/types";
import { QrResultDialog } from "../../open-table-flow";
import { useSettings } from "../../settings/settings-api";

/** A round's single status. Ready is the last one — nothing marks a round "served" (spec). */
const ORDER_STATUS: Record<OrderStatus, { label: string; badge: "pending" | "preparing" | "ready" | "cancelled" }> = {
  Pending: { label: "รอรับ", badge: "pending" },
  Preparing: { label: "กำลังทำ", badge: "preparing" },
  Ready: { label: "พร้อมเสิร์ฟ", badge: "ready" },
  Cancelled: { label: "ยกเลิกแล้ว", badge: "cancelled" },
};

const minutesTone: Record<TimeTone, string> = {
  elapsed: "text-ink-2",
  late: "text-orange-ink",
  over: "text-cancel-ink",
};

/** Server limit on `BillAdjustment.Description`. */
const ADJUSTMENT_DESCRIPTION_MAX = 100;
/** `+50`, `-20.00`, `12.5` — a sign, whole baht and at most two satang digits (the server's rule). */
const ADJUSTMENT_AMOUNT = /^[+-]?\d{1,6}(\.\d{1,2})?$/;

/**
 * `/owner/sessions/[id]` — frame `o04-session`. Every figure on the bill is the server's single
 * calculator (`GET /api/sessions/{id}` → `bill`); an adjustment answers with the recalculated round,
 * which replaces the cached one, so the bill moves the moment the request returns. Other devices'
 * changes arrive over the hub (see `useSessionDetail`).
 *
 * The header uses ticket 08's numbers: `openedAt`/`endsAt` are stored data and ใช้ไป N นาที is measured
 * against the server clock. Past `endsAt` it says ปิดการสั่งแล้ว — there is no extend button and no
 * countdown anywhere.
 */
export function SessionDetailScreen({ sessionId }: { sessionId: number }) {
  return (
    <LiveProvider>
      <SessionDetailBody sessionId={sessionId} />
    </LiveProvider>
  );
}

function SessionDetailBody({ sessionId }: { sessionId: number }) {
  const detail = useSessionDetail(sessionId);
  const settings = useSettings();
  const isOffline = useLiveOffline();
  const now = useServerTime(detail.data?.serverNow ?? 0);
  const [showQr, setShowQr] = useState(false);
  const [confirmVoid, setConfirmVoid] = useState(false);

  if (detail.isPending) {
    return (
      <div className="flex flex-col gap-5 pt-5">
        <Skeleton className="h-16 w-72" />
        <div className="grid gap-6 lg:grid-cols-[1fr_400px]">
          <Skeleton className="h-96 rounded-xl" />
          <Skeleton className="h-96 rounded-xl" />
        </div>
      </div>
    );
  }

  if (detail.isError) {
    return (
      <div className="flex flex-col items-start gap-4 pt-5">
        <Crumbs tableName={null} isOffline={isOffline} />
        <p className="text-body text-muted-foreground">
          {detail.error instanceof ApiError ? detail.error.message : "โหลดรายละเอียดโต๊ะไม่ได้ ลองอีกครั้ง"}
        </p>
        <Button variant="outline" onClick={() => void detail.refetch()}>
          ลองอีกครั้ง
        </Button>
      </div>
    );
  }

  const { session } = detail.data;
  const isOpen = session.status === "Open";
  const used = minutesUsed(session.openedAt, now);
  const time = roundTime(used, session.openedAt, session.endsAt, settings.data?.lastOrderWarningMinutes ?? 0);
  const liveOrders = session.orders.filter((order) => order.status !== "Cancelled");
  const guests = `${session.adults} ผู้ใหญ่${session.children > 0 ? ` ${session.children} เด็ก` : ""}`;

  return (
    <div className="flex flex-col pb-6 lg:pt-[18px]">
      <Crumbs tableName={session.tableName} isOffline={isOffline} />

      <div className="mt-4 flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-3">
            <h1 className="text-headline">โต๊ะ {session.tableName}</h1>
            <Badge variant="accent" className="h-7 px-3 text-label">
              {session.packageName}
            </Badge>
          </div>
          <span className="flex items-center gap-2 text-label font-normal text-muted-foreground">
            <UsersIcon className="size-4" strokeWidth={1.75} aria-hidden />
            {guests} · เปิด <span className="font-mono">{formatTime(session.openedAt)}</span> · หมดเวลา{" "}
            <span className="font-mono">{formatTime(session.endsAt)}</span>
          </span>
        </div>

        {isOpen ? (
          <div className="flex w-full flex-col gap-1.5 sm:w-80">
            <div className="flex items-center justify-end gap-2">
              {time.isOver ? (
                <>
                  <TimerOffIcon className="size-5 text-cancel-ink" strokeWidth={1.75} aria-hidden />
                  <span className="text-title text-cancel-ink">ปิดการสั่งแล้ว</span>
                </>
              ) : time.isLastOrder ? (
                <Badge variant="accent">Last order</Badge>
              ) : null}
              <span className={`text-label font-normal ${minutesTone[time.rail.tone]}`}>
                ใช้ไป <b className="font-mono tabular-nums">{used}</b> นาที
              </span>
            </div>
            <TimeRail {...time.rail} />
            <span className="text-right text-xs text-muted-foreground">
              {time.isOver ? `ครบ ${time.duration} นาที · ลูกค้าสั่งเพิ่มไม่ได้` : `แพ็กเกจ ${time.duration} นาที`}
            </span>
          </div>
        ) : (
          <Badge variant={session.status === "Paid" ? "ready" : "cancelled"} className="h-7 px-3 text-label">
            {session.status === "Paid" ? "ชำระเงินแล้ว" : "ยกเลิกโต๊ะแล้ว"}
          </Badge>
        )}
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        {/* หน้าพิมพ์ใบแจ้งยอด 80mm เป็นของ ticket 20 (`/print/prebill/[sessionId]`) — ลิงก์ชี้ไปที่เส้นทางจริงของมัน
            และปิด prefetch จนกว่า ticket 20 จะสร้าง (แบบเดียวกับปุ่มพิมพ์ QR slip ของ ticket 07) */}
        <Button asChild variant="outline">
          <Link href={`/print/prebill/${session.id}`} prefetch={false} target="_blank" rel="noreferrer">
            <FileTextIcon className="size-4" aria-hidden />
            พิมพ์ใบแจ้งยอด
          </Link>
        </Button>
        <Button variant="outline" disabled={!isOpen} onClick={() => setShowQr(true)}>
          <QrCodeIcon className="size-4" aria-hidden />
          แสดง QR
        </Button>
        {isOpen ? (
          <>
            <Button variant="destructive" disabled={liveOrders.length > 0} onClick={() => setConfirmVoid(true)}>
              <BanIcon className="size-4" aria-hidden />
              ยกเลิกโต๊ะ
            </Button>
            {liveOrders.length > 0 ? (
              <span className="text-label font-normal text-muted-foreground">ยกเลิกไม่ได้ เพราะมีออเดอร์แล้ว</span>
            ) : null}
          </>
        ) : null}
      </div>

      <div className="mt-6 grid items-start gap-6 lg:grid-cols-[1fr_400px]">
        <div className="flex min-w-0 flex-col gap-4">
          <OrdersCard orders={session.orders} />
          <AdjustmentsCard session={session} />
        </div>

        <div className="flex flex-col gap-4 lg:sticky lg:top-6">
          <BillCard session={session} bill={session.bill} durationMinutes={time.duration} />
          {isOpen && time.isOver ? (
            <div className="rounded-xl bg-sunken p-4">
              <span className="flex items-center gap-2 text-label text-orange-ink">
                <CircleAlertIcon className="size-4" strokeWidth={1.75} aria-hidden />
                ปิดการสั่งแล้ว
              </span>
              <p className="mt-1 text-label font-normal text-muted-foreground">
                ลูกค้าสั่งเพิ่มไม่ได้ · ยังปรับยอดและรับชำระเงินได้
              </p>
            </div>
          ) : null}
        </div>
      </div>

      {showQr ? <QrResultDialog session={session} onClose={() => setShowQr(false)} /> : null}
      {confirmVoid ? <VoidDialog session={session} onClose={() => setConfirmVoid(false)} /> : null}
    </div>
  );
}

function Crumbs({ tableName, isOffline }: { tableName: string | null; isOffline: boolean }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <nav aria-label="breadcrumb" className="flex items-center gap-2 text-label">
        <Link href="/owner" className="font-normal text-muted-foreground hover:text-ink">
          ภาพรวม
        </Link>
        {tableName ? (
          <>
            <ChevronRightIcon className="size-4 text-muted-foreground" aria-hidden />
            <span aria-current="page">โต๊ะ {tableName}</span>
          </>
        ) : null}
      </nav>
      <div className="flex items-center gap-3">
        <span
          className={`inline-flex h-8 items-center gap-2 rounded-full px-3 text-label font-semibold ${
            isOffline ? "bg-sunken text-ink-2" : "bg-ready-soft text-ready-ink"
          }`}
        >
          <i className={`size-[7px] rounded-full ${isOffline ? "bg-served-dot" : "bg-ready-dot"}`} aria-hidden />
          {isOffline ? "ออฟไลน์ · ข้อมูลอาจไม่ใหม่" : "เชื่อมต่อแบบเรียลไทม์"}
        </span>
        <span className="inline-flex h-[22px] items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
          DEMO DATA
        </span>
      </div>
    </div>
  );
}

/** The rounds in order, each with its status, send time and lines; à-la-carte lines carry their amount. */
function OrdersCard({ orders }: { orders: OrderDto[] }) {
  const live = orders.filter((order) => order.status !== "Cancelled");
  const waiting = live.filter((order) => order.status !== "Ready").length;

  return (
    <section className="overflow-hidden rounded-xl border border-border bg-surface">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 pt-4 pb-3">
        <h2 className="text-title">ออเดอร์</h2>
        <span className="text-label font-normal text-muted-foreground">
          {orders.length === 0
            ? "ยังไม่มีออเดอร์"
            : `${orders.length} รอบ · ${waiting === 0 ? "เสิร์ฟครบทุกจาน" : `ค้างครัว ${waiting} รอบ`}`}
        </span>
      </div>

      {orders.map((order) => {
        const status = ORDER_STATUS[order.status];
        const isCancelled = order.status === "Cancelled";
        const alaCarte = order.items
          .filter((item) => item.kind === "AlaCarte")
          .reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);

        return (
          <article key={order.id} className="border-t border-border px-4 pt-3.5 pb-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-3">
                <b className="text-title">รอบที่ {order.number}</b>
                <span className="font-mono text-label font-normal text-muted-foreground">
                  {formatTime(order.createdAt)}
                </span>
              </div>
              <div className="flex items-center gap-3">
                {alaCarte > 0 && !isCancelled ? (
                  <span className="text-xs text-muted-foreground">
                    คิดเงินแยก <span className="tabular-nums">{formatBaht(alaCarte)}</span>
                  </span>
                ) : null}
                {/* mount: ticket 17 — ปุ่ม "แก้ไขออเดอร์" ของรอบนี้ (เฉพาะ Pending/Preparing ของ session Open) */}
                <Badge variant={status.badge}>{status.label}</Badge>
              </div>
            </div>

            {isCancelled && order.cancelReason ? (
              <p className="mt-1.5 text-label font-normal text-cancel-ink">
                ไม่คิดเงิน · เหตุผล: {order.cancelReason}
              </p>
            ) : null}

            <ul
              className={`mt-2 grid gap-x-7 gap-y-1 sm:grid-cols-2 ${isCancelled ? "text-muted-foreground line-through" : ""}`}
            >
              {order.items.map((item, index) => (
                <li key={index} className="flex min-h-[26px] items-center justify-between gap-2.5 text-sm">
                  <span className="min-w-0">
                    {item.name}
                    {item.note ? <span className="ml-1.5 text-xs text-pending-ink">({item.note})</span> : null}
                  </span>
                  <span className="flex items-center gap-3 tabular-nums">
                    <span className="text-ink-2">×{item.quantity}</span>
                    {item.kind === "AlaCarte" ? <b>{formatBaht(item.unitPrice * item.quantity)}</b> : null}
                  </span>
                </li>
              ))}
            </ul>
          </article>
        );
      })}
    </section>
  );
}

/** Frame o04's ปรับยอด card: the adjustments with a remove button, and the add row (reason + signed amount). */
function AdjustmentsCard({ session }: { session: SessionDetailDto }) {
  const add = useAddAdjustment(session.id);
  const remove = useRemoveAdjustment(session.id);
  const ids = useId();
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<{ field: "reason" | "amount"; message: string } | null>(null);
  const isOpen = session.status === "Open";

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const reason = description.trim();
    const typed = amount.trim();
    if (reason.length === 0) {
      setError({ field: "reason", message: "ใส่เหตุผลของการปรับยอดก่อน" });
      return;
    }
    if (!ADJUSTMENT_AMOUNT.test(typed) || Number(typed) === 0) {
      setError({ field: "amount", message: "ใส่ยอดเป็นตัวเลข ทศนิยมไม่เกิน 2 ตำแหน่ง และไม่เป็น 0 เช่น 50 หรือ -20.00" });
      return;
    }
    setError(null);
    add.mutate(
      { description: reason, amount: Number(typed) },
      {
        onSuccess: () => {
          setDescription("");
          setAmount("");
        },
      },
    );
  };

  return (
    <section className="rounded-xl border border-border bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-title">ปรับยอด</h2>
        <span className="text-label font-normal text-muted-foreground">บวกเพิ่มยอด · ลบเป็นส่วนลด</span>
      </div>

      {session.adjustments.length > 0 ? (
        <ul className="flex flex-col">
          {session.adjustments.map((adjustment) => (
            <li key={adjustment.id} className="flex items-center justify-between gap-3 pt-3 pb-1">
              <div className="flex min-w-0 flex-col gap-0.5">
                <span className="text-body">{adjustment.description}</span>
                <span className="text-xs text-muted-foreground">ปรับยอดโดยเจ้าของร้าน</span>
              </div>
              <div className="flex items-center gap-3">
                <b className="text-title tabular-nums">
                  {adjustment.amount > 0 ? "+" : ""}
                  {formatBaht(adjustment.amount)}
                </b>
                {isOpen ? (
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`ลบรายการปรับยอด ${adjustment.description}`}
                    disabled={remove.isPending}
                    onClick={() => remove.mutate(adjustment.id)}
                  >
                    <Trash2Icon aria-hidden />
                  </Button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p className="pt-3 text-label font-normal text-muted-foreground">ยังไม่มีรายการปรับยอด</p>
      )}

      <hr className="mt-3 border-border" />

      {isOpen ? (
        <form className="pt-4" onSubmit={submit} noValidate>
          <div className="flex flex-col gap-2 sm:flex-row">
            <label htmlFor={`${ids}-reason`} className="sr-only">
              เหตุผลของการปรับยอด
            </label>
            <Input
              id={`${ids}-reason`}
              value={description}
              maxLength={ADJUSTMENT_DESCRIPTION_MAX}
              placeholder="เหตุผล เช่น ค่าปรับอาหารเหลือ"
              aria-invalid={error?.field === "reason"}
              aria-describedby={`${ids}-help`}
              onChange={(event) => setDescription(event.target.value)}
            />
            <label htmlFor={`${ids}-amount`} className="sr-only">
              ยอดที่ปรับ (บาท)
            </label>
            <Input
              id={`${ids}-amount`}
              value={amount}
              inputMode="decimal"
              placeholder="+0.00"
              className="text-right tabular-nums sm:w-[148px] sm:flex-none"
              aria-invalid={error?.field === "amount"}
              aria-describedby={`${ids}-help`}
              onChange={(event) => setAmount(event.target.value)}
            />
            <Button type="submit" variant="outline" disabled={add.isPending}>
              เพิ่ม
            </Button>
          </div>
          {error ? (
            <p role="alert" className="mt-2 text-label font-normal text-cancel-ink">
              {error.message}
            </p>
          ) : null}
          <p id={`${ids}-help`} className="mt-2 text-xs text-muted-foreground">
            ใส่เครื่องหมายลบนำหน้าเพื่อลดยอด เช่น -20.00 · ปรับยอดได้เฉพาะตอนยังไม่ปิดบิล
          </p>
        </form>
      ) : (
        <p className="pt-3 text-xs text-muted-foreground">ปิดบิลแล้ว · ปรับยอดไม่ได้</p>
      )}
    </section>
  );
}

/** The itemised bill exactly as the server computed it: buffet, à la carte, adjustments, service, VAT, total. */
function BillCard({
  session,
  bill,
  durationMinutes,
}: {
  session: SessionDetailDto;
  bill: BillDto;
  durationMinutes: number;
}) {
  const row = "flex items-center justify-between gap-3 py-1.5 text-body";
  const group = "mt-2 text-xs font-semibold text-muted-foreground";

  return (
    <section className="rounded-xl border border-border bg-surface p-4">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 className="text-title">บิล</h2>
        <Badge variant="accent" className="before:hidden">
          {session.packageName} · <span className="tabular-nums">{durationMinutes}</span> นาที
        </Badge>
      </div>

      <div className="text-xs font-semibold text-muted-foreground">ค่าบุฟเฟ่ต์</div>
      <div className={row}>
        <span>
          ผู้ใหญ่ {bill.adults} × <span className="tabular-nums">{formatBahtWhole(bill.adultPrice)}</span>
        </span>
        <span className="tabular-nums">{formatBaht(bill.adults * bill.adultPrice)}</span>
      </div>
      {bill.children > 0 ? (
        <div className={row}>
          <span>
            เด็ก {bill.children} × <span className="tabular-nums">{formatBahtWhole(bill.childPrice)}</span>
          </span>
          <span className="tabular-nums">{formatBaht(bill.children * bill.childPrice)}</span>
        </div>
      ) : null}

      {bill.alaCarteLines.length > 0 ? (
        <>
          <div className={group}>เมนูคิดเงินแยก</div>
          {bill.alaCarteLines.map((line) => (
            <div key={`${line.name}-${line.unitPrice}`} className={row}>
              <span className="min-w-0">
                {line.name} {line.quantity} × <span className="tabular-nums">{formatBahtWhole(line.unitPrice)}</span>
              </span>
              <span className="tabular-nums">{formatBaht(line.amount)}</span>
            </div>
          ))}
        </>
      ) : null}

      {bill.adjustments.length > 0 ? (
        <>
          <div className={group}>ปรับยอด</div>
          {bill.adjustments.map((adjustment) => (
            <div key={adjustment.id} className={row}>
              <span className="min-w-0">{adjustment.description}</span>
              <span className="tabular-nums">
                {adjustment.amount > 0 ? "+" : ""}
                {formatBaht(adjustment.amount)}
              </span>
            </div>
          ))}
        </>
      ) : null}

      <hr className="mt-2.5 mb-1.5 border-border" />
      <div className={row}>
        <span className="text-ink-2">ยอดก่อนค่าบริการ</span>
        <span className="tabular-nums">{formatBaht(bill.subtotal)}</span>
      </div>
      <div className={row}>
        <span className="text-ink-2">ค่าบริการ {bill.serviceChargePercent}%</span>
        <span className="tabular-nums">{formatBaht(bill.serviceCharge)}</span>
      </div>
      <div className={row}>
        <span className="text-ink-2">VAT {bill.vatPercent}%</span>
        <span className="tabular-nums">{formatBaht(bill.vat)}</span>
      </div>
      <div className="flex items-center justify-between gap-3 pt-2 text-title">
        <span>รวมทั้งสิ้น</span>
        <span className="tabular-nums">{formatBaht(bill.total)}</span>
      </div>

      {/* mount: ticket 18 — สถานะพร้อมปิดบิล ("ออเดอร์เสิร์ฟครบแล้ว · ปิดบิลได้") และปุ่ม "รับชำระเงิน" (primary) */}
    </section>
  );
}

/** ยกเลิกโต๊ะที่เปิดผิด: ticket 07's void, offered only while the round has no live orders. */
function VoidDialog({ session, onClose }: { session: SessionDetailDto; onClose: () => void }) {
  const voidSession = useVoidSession(session.id);

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent className="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle>ยกเลิกโต๊ะ {session.tableName}?</DialogTitle>
          <DialogDescription>
            ใช้เมื่อเปิดโต๊ะผิด · QR ของรอบนี้จะใช้สั่งอาหารไม่ได้อีก และโต๊ะจะว่างทันที
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="border-t-0 bg-transparent">
          <Button variant="ghost" onClick={onClose}>
            ไม่ยกเลิก
          </Button>
          <Button
            variant="destructive"
            disabled={voidSession.isPending}
            onClick={() => voidSession.mutate(undefined, { onSettled: onClose })}
          >
            ยืนยันยกเลิกโต๊ะ
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
