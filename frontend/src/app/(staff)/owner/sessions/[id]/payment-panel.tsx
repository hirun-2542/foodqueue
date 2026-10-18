"use client";

import {
  BanknoteIcon,
  CheckIcon,
  CircleAlertIcon,
  CreditCardIcon,
  PrinterIcon,
  QrCodeIcon,
} from "lucide-react";
import Link from "next/link";
import { type FormEvent, useState } from "react";
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
import { ApiError } from "@/lib/api";
import { formatBaht, formatTime } from "@/lib/format";
import { usePaySession } from "@/lib/sessions";
import type { BlockingOrderDto, PaymentMethod, SessionDetailDto } from "@/lib/types";

/**
 * Ticket 18 — frames `o06` (cash dialog) and `o07` (paid) of `/owner/sessions/[id]`.
 *
 * `PaymentPanel` is mounted at the bottom of the bill card (the mount point ticket 16 left): while the round
 * is Open it shows whether the bill can be closed and the `รับชำระเงิน` button; once the round is Paid it
 * shows ชำระเมื่อ / รับเงิน / เงินทอน / เลขที่ใบเสร็จ and the receipt link. `PaidNotice` is the
 * `ปิดบิลแล้ว` well that `o07` puts under the bill.
 *
 * Every rule is the server's (`POST /api/sessions/{id}/payment`): this file only decides what to show.
 * PromptPay renders disabled with a message until ticket 19 builds the QR — no empty QR is ever shown.
 */

const METHOD_LABELS: Record<PaymentMethod, string> = {
  Cash: "เงินสด",
  PromptPay: "PromptPay",
  Card: "บัตร",
};

/** The frame's verbatim cash label; the other two follow the same shape. */
const CONFIRM_LABELS: Record<PaymentMethod, string> = {
  Cash: "ยืนยันรับเงินสด",
  Card: "ยืนยันรับเงินบัตร",
  PromptPay: "ยืนยันว่าได้รับเงินแล้ว",
};

/** Ticket 19 owns the PromptPay QR; until it lands the option is shown, disabled and explained. */
const PROMPTPAY_PENDING = "ยังไม่เปิดให้ใช้ PromptPay · ระบบจะแสดง QR ตามยอดบิลเมื่อพร้อมใช้งาน";

/** The same Thai labels the bill's rounds carry (`session-detail-screen.tsx`), for the blocking list. */
const ROUND_STATUS: Record<BlockingOrderDto["status"], string> = {
  Pending: "รอรับ",
  Preparing: "กำลังทำ",
  Ready: "พร้อมเสิร์ฟ",
  Cancelled: "ยกเลิกแล้ว",
};

export function PaymentPanel({ session }: { session: SessionDetailDto }) {
  const [open, setOpen] = useState(false);
  const blocking = session.orders.filter(
    (order) => order.status === "Pending" || order.status === "Preparing",
  );

  // The dialog stays mounted until the owner closes it: a successful payment flips this round to Paid, and
  // `o07`'s "ชำระเงินสำเร็จ" view lives inside the dialog — swapping to the paid rows first would hide it.
  if (open) {
    return <PaymentDialog session={session} onClose={() => setOpen(false)} />;
  }

  if (session.status === "Paid") {
    return <PaidBill session={session} />;
  }

  if (session.status !== "Open") {
    return null;
  }

  return (
    <div className="mt-3 border-t border-border pt-3">
      {blocking.length === 0 ? (
        <div className="flex items-center gap-2 rounded-lg bg-ready-soft px-3 py-2 text-label text-ready-ink">
          <CheckIcon className="size-4" strokeWidth={2} aria-hidden />
          ออเดอร์เสิร์ฟครบแล้ว · ปิดบิลได้
        </div>
      ) : (
        <div className="rounded-lg bg-pending-soft px-3 py-2 text-label text-pending-ink">
          <span className="flex items-center gap-2 font-semibold">
            <CircleAlertIcon className="size-4" strokeWidth={1.75} aria-hidden />
            ยังปิดบิลไม่ได้ · มีออเดอร์ค้างในครัว
          </span>
          <ul className="mt-1 font-normal">
            {blocking.map((order) => (
              <li key={order.id}>
                รอบที่ <span className="tabular-nums">{order.number}</span> · {ROUND_STATUS[order.status]}
              </li>
            ))}
          </ul>
        </div>
      )}

      <Button className="mt-3 w-full" size="lg" disabled={blocking.length > 0} onClick={() => setOpen(true)}>
        <BanknoteIcon className="size-4" aria-hidden />
        รับชำระเงิน
      </Button>
    </div>
  );
}

/** `o07`: what was paid, when, and the backend's receipt number, in the bill card itself. */
function PaidBill({ session }: { session: SessionDetailDto }) {
  const row = "flex items-center justify-between gap-3 py-1.5 text-body";

  return (
    <div className="mt-3 border-t border-border pt-2">
      <div className={row}>
        <span className="text-ink-2">ชำระเมื่อ</span>
        <span>
          <span className="font-mono">{formatTime(session.payment!.paidAt)}</span> ·{" "}
          {METHOD_LABELS[session.payment!.method]}
        </span>
      </div>
      <div className={row}>
        <span className="text-ink-2">รับเงิน</span>
        <span className="tabular-nums">{formatBaht(session.payment!.amountReceived)}</span>
      </div>
      <div className={row}>
        <span className="text-ink-2">เงินทอน</span>
        <span className="tabular-nums">{formatBaht(session.payment!.change)}</span>
      </div>
      <div className={row}>
        <span className="text-ink-2">เลขที่ใบเสร็จ</span>
        <span className="font-mono">{session.payment!.receiptNo}</span>
      </div>
      <Button asChild variant="secondary" size="lg" className="mt-3 w-full">
        <Link href={`/print/receipt/${session.id}`} prefetch={false} target="_blank" rel="noreferrer">
          <PrinterIcon className="size-4" aria-hidden />
          พิมพ์ใบเสร็จ
        </Link>
      </Button>
    </div>
  );
}

/** `o07`'s well under the bill: the round is closed and the receipt can be reprinted at any time. */
export function PaidNotice({ session }: { session: SessionDetailDto }) {
  if (session.status !== "Paid") {
    return null;
  }

  return (
    <div className="rounded-xl bg-sunken p-4">
      <span className="flex items-center gap-2 text-label font-semibold text-ready-ink">
        <CheckIcon className="size-4" strokeWidth={2} aria-hidden />
        ปิดบิลแล้ว
      </span>
      <p className="mt-1 text-label font-normal text-muted-foreground">
        ชำระเมื่อ <span className="font-mono">{formatTime(session.payment!.paidAt)}</span> · พิมพ์ใบเสร็จซ้ำได้ทุกเมื่อ
      </p>
    </div>
  );
}

/** `o06`: the total, the three methods, the cash the owner received and the change, then `o07` on success. */
function PaymentDialog({ session, onClose }: { session: SessionDetailDto; onClose: () => void }) {
  const pay = usePaySession(session.id);
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [amount, setAmount] = useState("");
  const total = session.bill.total;
  const typed = amount.trim();
  const parsed = Number(typed);
  const cashValid = typed !== "" && Number.isFinite(parsed) && parsed >= 0;
  const change = cashValid ? parsed - total : null;
  const paid = pay.data?.payment ?? null;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (method === "Cash" && !cashValid) return;
    pay.mutate(method === "Cash" ? { method, amountReceived: parsed } : { method });
  };

  const error = pay.error instanceof ApiError ? pay.error : null;
  const blocking = error?.code === "ORDERS_IN_PROGRESS" ? (error.problem.orders ?? []) : [];
  const shortPaid = method === "Cash" && cashValid && change !== null && change < 0;

  if (paid) {
    return (
      <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
        <DialogContent className="sm:max-w-[440px]" showCloseButton={false}>
          <div className="flex flex-col items-center pt-2 text-center">
            <span className="flex size-12 items-center justify-center rounded-full bg-ready-soft text-ready-ink">
              <CheckIcon className="size-6" strokeWidth={2} aria-hidden />
            </span>
            <DialogHeader className="mt-3 items-center gap-0.5 text-center">
              <DialogTitle className="text-headline">ชำระเงินสำเร็จ</DialogTitle>
              <DialogDescription>
                โต๊ะ {session.tableName} · ปิดบิลแล้ว ลูกค้าสั่งเพิ่มไม่ได้อีก
              </DialogDescription>
            </DialogHeader>
          </div>

          <div className="flex items-center justify-between gap-3 border-t border-border pt-3">
            <span className="text-label text-muted-foreground">เลขที่ใบเสร็จ</span>
            <span className="font-mono text-label font-semibold">{paid.receiptNo}</span>
          </div>

          <div className="mt-1 rounded-lg bg-sunken p-3">
            <div className="flex items-center justify-between gap-3 text-label">
              <span className="text-muted-foreground">วิธีจ่าย</span>
              <span className="font-semibold">{METHOD_LABELS[paid.method]}</span>
            </div>
            <div className="mt-1 flex items-center justify-between gap-3 text-label">
              <span className="text-muted-foreground">รับเงิน</span>
              <span className="tabular-nums">{formatBaht(paid.amountReceived)}</span>
            </div>
            <div className="mt-1 flex items-center justify-between gap-3 text-label">
              <span className="text-muted-foreground">เงินทอน</span>
              <span className="tabular-nums">{formatBaht(paid.change)}</span>
            </div>
          </div>

          <DialogFooter className="flex-col gap-2 border-t-0 bg-transparent sm:flex-col">
            <Button asChild size="lg" className="w-full">
              <Link href={`/print/receipt/${session.id}`} prefetch={false} target="_blank" rel="noreferrer">
                <PrinterIcon className="size-4" aria-hidden />
                พิมพ์ใบเสร็จ
              </Link>
            </Button>
            <Button asChild variant="ghost" className="w-full">
              <Link href="/owner">กลับไปภาพรวม</Link>
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent className="sm:max-w-[560px]" showCloseButton={false}>
        <DialogHeader className="flex-row items-center justify-between gap-3">
          <DialogTitle>รับชำระเงิน · โต๊ะ {session.tableName}</DialogTitle>
          <Badge variant="accent" className="before:hidden">
            {session.packageName}
          </Badge>
        </DialogHeader>

        <div className="rounded-xl bg-sunken p-4">
          <span className="text-label text-muted-foreground">ยอดที่ต้องชำระ</span>
          <div className="text-display tabular-nums">{formatBaht(total)}</div>
          <p className="mt-1.5 text-xs text-muted-foreground">
            ค่าบุฟเฟ่ต์ {formatBaht(session.bill.buffetTotal)} · เมนูคิดเงินแยก{" "}
            {formatBaht(session.bill.alaCarteTotal)} · ปรับยอด {session.bill.adjustmentTotal > 0 ? "+" : ""}
            {formatBaht(session.bill.adjustmentTotal)} · ค่าบริการและ VAT{" "}
            {formatBaht(session.bill.serviceCharge + session.bill.vat)}
          </p>
        </div>

        <div className="flex w-full gap-1 rounded-lg bg-sunken p-1" role="group" aria-label="วิธีชำระเงิน">
          {(["Cash", "PromptPay", "Card"] as const).map((option) => {
            const disabled = option === "PromptPay";
            const selected = option === method;
            return (
              <button
                key={option}
                type="button"
                aria-pressed={selected}
                disabled={disabled}
                title={disabled ? PROMPTPAY_PENDING : undefined}
                onClick={() => setMethod(option)}
                className={`flex h-10 flex-1 items-center justify-center gap-2 rounded-md text-body font-semibold transition-colors outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
                  selected
                    ? "bg-surface text-ink shadow-xs"
                    : disabled
                      ? "text-muted-foreground"
                      : "text-ink-2 hover:bg-surface"
                }`}
              >
                {option === "Cash" ? (
                  <BanknoteIcon className="size-4" strokeWidth={1.75} aria-hidden />
                ) : option === "PromptPay" ? (
                  <QrCodeIcon className="size-4" strokeWidth={1.75} aria-hidden />
                ) : (
                  <CreditCardIcon className="size-4" strokeWidth={1.75} aria-hidden />
                )}
                {METHOD_LABELS[option]}
              </button>
            );
          })}
        </div>

        <p className="text-xs text-muted-foreground">
          {METHOD_LABELS.PromptPay}: {PROMPTPAY_PENDING}
        </p>

        <form onSubmit={submit} className="flex flex-col gap-3">
          {method === "Cash" ? (
            <>
              <label className="flex flex-col gap-1.5">
                <span className="text-label font-semibold">รับเงินมา</span>
                <span className="flex h-14 items-center gap-2 rounded-lg border border-control-border bg-surface px-3">
                  <span className="text-body text-muted-foreground">฿</span>
                  <Input
                    autoFocus
                    inputMode="decimal"
                    value={amount}
                    onChange={(event) => setAmount(event.target.value)}
                    placeholder="0.00"
                    aria-invalid={shortPaid}
                    className="h-auto border-0 bg-transparent px-0 text-2xl font-semibold shadow-none focus-visible:outline-none"
                  />
                </span>
              </label>

              <div className="flex flex-wrap items-center gap-2">
                <Chip label={`พอดี ${formatBaht(total)}`} onClick={() => setAmount(total.toFixed(2))} selected={cashValid && parsed === total} />
                {[1, 2].map((step) => {
                  const suggestion = Math.ceil(total / 500) * 500 + (step - 1) * 500;
                  return (
                    <Chip
                      key={step}
                      label={formatBaht(suggestion).replace(/\.00$/, "")}
                      onClick={() => setAmount(String(suggestion))}
                      selected={cashValid && parsed === suggestion}
                    />
                  );
                })}
              </div>

              <div className="flex items-center justify-between gap-3 rounded-lg bg-sunken px-4 py-3">
                <span className="text-body">เงินทอน</span>
                <span className="text-title tabular-nums">
                  {change === null ? "—" : formatBaht(Math.max(0, change))}
                </span>
              </div>
            </>
          ) : (
            <div className="flex items-center justify-between gap-3 rounded-lg bg-sunken px-4 py-3 text-label">
              <span className="text-muted-foreground">รับเต็มจำนวน</span>
              <span>
                {formatBaht(total)} · เงินทอน {formatBaht(0)}
              </span>
            </div>
          )}

          {shortPaid ? (
            <p className="rounded-lg bg-cancel-soft px-3 py-2 text-label text-cancel-ink" role="status">
              รับเงินมาไม่พอ · ขาดอีก {formatBaht(Math.abs(change ?? 0))}
            </p>
          ) : null}

          {error ? (
            <div className="rounded-lg bg-cancel-soft px-3 py-2 text-label text-cancel-ink" role="alert">
              <span className="font-semibold">{error.message}</span>
              {blocking.length > 0 ? (
                <ul className="mt-1 font-normal">
                  {blocking.map((order) => (
                    <li key={order.id}>
                      รอบที่ <span className="tabular-nums">{order.number}</span> · {ROUND_STATUS[order.status]}
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}

          <DialogFooter className="border-t border-border pt-4 sm:justify-between">
            <Button type="button" variant="ghost" onClick={onClose} disabled={pay.isPending}>
              ยกเลิก
            </Button>
            <Button
              type="submit"
              size="lg"
              disabled={pay.isPending || (method === "Cash" && !cashValid)}
            >
              {pay.isPending ? "กำลังบันทึก…" : CONFIRM_LABELS[method]}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function Chip({
  label,
  onClick,
  selected,
}: {
  label: string;
  onClick: () => void;
  selected: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      className={`h-8 rounded-full px-3 text-label outline-none transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
        selected
          ? "bg-orange-soft font-semibold text-orange-ink"
          : "border border-border bg-surface text-ink-2 hover:bg-sunken"
      }`}
    >
      {label}
    </button>
  );
}
