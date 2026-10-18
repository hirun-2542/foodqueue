"use client";

import { TriangleAlertIcon, XIcon } from "lucide-react";
import { useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { formatTime, minutesUsed } from "@/lib/format";
import { CANCEL_REASON_CHIPS, CANCEL_REASON_MAX, isStaleOrderError, useCancelOrder } from "@/lib/kitchen";
import type { OrderDto } from "@/lib/types";

/**
 * Frame k2: cancel one round with a reason. It names the round, says how long it waited (server time,
 * counting up), warns that its lines will not be billed, offers the common reasons as chips (a tap
 * starts the reason with "<chip>: " for the cook to finish), and requires 1–200 characters — the
 * customer sees the reason on their orders page.
 *
 * When the round changed on another device meanwhile (already cancelled, or its table paid), the
 * board refetches with a notice and the dialog closes; any other failure keeps what was typed.
 */
export function CancelOrderDialog({
  order,
  now,
  onClose,
}: {
  order: OrderDto;
  /** Server time (epoch ms) from the board, so the wait matches the card. */
  now: number;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  const area = useRef<HTMLTextAreaElement>(null);
  const cancel = useCancelOrder();
  const ids = useId();
  const trimmed = reason.trim();
  const waited = minutesUsed(order.createdAt, now);
  const lines = order.items;

  const pick = (chip: string) => {
    const next = `${chip}: `;
    setReason(next);
    const input = area.current;
    if (input) {
      input.focus();
      // The value is applied on the next render; put the caret after the chip text then.
      requestAnimationFrame(() => input.setSelectionRange(next.length, next.length));
    }
  };

  const submit = () => {
    if (trimmed.length === 0 || cancel.isPending) return;
    cancel.mutate(
      { order, reason: trimmed },
      {
        onSuccess: onClose,
        onError: (error) => {
          if (isStaleOrderError(error)) onClose();
        },
      },
    );
  };

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent showCloseButton={false} className="gap-4 p-5 sm:max-w-[520px]">
        <div className="flex items-start justify-between gap-3">
          <DialogHeader className="gap-1">
            <DialogTitle className="text-[24px] leading-[1.35] font-semibold tracking-[-0.01em]">
              ยกเลิกออเดอร์ {order.tableName} · รอบที่ {order.number}
            </DialogTitle>
            <DialogDescription className="text-[13px] leading-[1.55]">
              ส่ง {formatTime(order.createdAt)} · รอมา <span className="font-mono">{waited < 1 ? "<1" : waited}</span>{" "}
              นาที · <span className="tabular-nums">{lines.length}</span> รายการ
            </DialogDescription>
          </DialogHeader>
          <DialogClose asChild>
            <Button variant="ghost" size="icon" aria-label="ปิด">
              <XIcon className="size-6" strokeWidth={1.75} aria-hidden />
            </Button>
          </DialogClose>
        </div>

        <div className="flex items-start gap-2.5 rounded-lg bg-cancel-soft px-3 py-2.5 text-cancel-ink">
          <TriangleAlertIcon className="mt-0.5 size-5 flex-none" strokeWidth={1.75} aria-hidden />
          <div className="flex flex-col gap-1 text-[13px] leading-[1.55]">
            <span className="font-semibold">ยกเลิกแล้ว {lines.length} รายการนี้จะไม่ถูกคิดเงิน</span>
            <span>
              {lines
                .map((item) => `${item.name} ×${item.quantity}${item.note ? ` (${item.note})` : ""}`)
                .join(" · ")}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <span id={`${ids}-chips`} className="text-[13px] font-semibold text-ink-2">
            เหตุผลที่พบบ่อย
          </span>
          <div role="group" aria-labelledby={`${ids}-chips`} className="flex flex-wrap gap-2">
            {CANCEL_REASON_CHIPS.map((chip) => {
              const on = trimmed.startsWith(chip);
              return (
                <button
                  key={chip}
                  type="button"
                  aria-pressed={on}
                  onClick={() => pick(chip)}
                  className={`inline-flex h-[34px] items-center rounded-full border px-3.5 text-sm whitespace-nowrap focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
                    on
                      ? "border-orange-line bg-orange-soft font-semibold text-orange-ink"
                      : "border-border bg-surface font-medium text-ink-2 hover:bg-sunken"
                  }`}
                >
                  {chip}
                </button>
              );
            })}
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between">
            <Label htmlFor={`${ids}-reason`} className="text-[13px] font-semibold text-ink-2">
              เหตุผล (จำเป็น)
            </Label>
            <span className="text-[12.5px] text-muted-foreground tabular-nums" aria-live="polite">
              {reason.length} / {CANCEL_REASON_MAX}
            </span>
          </div>
          <Textarea
            id={`${ids}-reason`}
            ref={area}
            value={reason}
            maxLength={CANCEL_REASON_MAX}
            required
            aria-describedby={`${ids}-help`}
            onChange={(event) => setReason(event.target.value)}
            className="min-h-[88px] resize-none text-[15px] leading-[1.6]"
          />
          <span id={`${ids}-help`} className="text-[12.5px] text-muted-foreground">
            ลูกค้าจะเห็นเหตุผลนี้ในหน้าออเดอร์
          </span>
        </div>

        <div className="flex gap-3">
          <Button variant="ghost" className="h-16 rounded-xl px-7 text-[18px]" onClick={onClose}>
            ไม่ยกเลิก
          </Button>
          <Button
            variant="destructive"
            className="h-16 grow rounded-xl px-7 text-[18px]"
            disabled={trimmed.length === 0 || cancel.isPending}
            onClick={submit}
          >
            ยืนยันยกเลิกออเดอร์
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
