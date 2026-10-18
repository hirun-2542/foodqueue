"use client";

import {
  ArrowRightIcon,
  BanIcon,
  MinusIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
  Undo2Icon,
  XIcon,
} from "lucide-react";
import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/lib/api";
import { formatBaht } from "@/lib/format";
import {
  EDIT_REASON_CHIPS,
  EDIT_REASON_MAX,
  type OrderEditLine,
  isStaleOrderError,
  useEditOrder,
} from "@/lib/kitchen";
import { useMenuItems } from "@/lib/menu";
import { billTotalWithout, useSessionDetail } from "@/lib/sessions";
import type { OrderDto, OrderItemDto } from "@/lib/types";
import { cn } from "@/lib/utils";

/**
 * `แก้ไข` on a queue card (ticket 17, frame o17): opens the edit dialog for a Pending/Preparing round.
 * Self-contained — the card mounts it in its action row and passes only the round; Ready rounds do not
 * render it (the server would answer ORDER_NOT_EDITABLE anyway).
 */
export function EditOrderAction({ order }: { order: OrderDto }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        variant="ghost"
        className="h-11 flex-1 gap-2 px-2 text-[15px] text-ink-2"
        aria-haspopup="dialog"
        aria-label={`แก้ไขออเดอร์ ${order.tableName} รอบที่ ${order.number}`}
        onClick={() => setOpen(true)}
      >
        <PencilIcon className="size-[18px]" strokeWidth={1.75} aria-hidden />
        แก้ไข
      </Button>
      {/* The dialog keeps the round as it was when tapped, like the cancel dialog; a stale save closes it. */}
      {open ? <EditOrderDialog order={order} onClose={() => setOpen(false)} /> : null}
    </>
  );
}

/**
 * Reduce or remove lines (a removed line can be taken back before saving), pick or type the reason,
 * and see the table's bill before → after (SC/VAT re-rounded as the server does) before saving.
 * Quantities only go down: + restores up to what was ordered, never past it. Removing every line —
 * or ยกเลิกทั้งออเดอร์ — cancels the round with the reason; otherwise its status stays.
 */
function EditOrderDialog({ order, onClose }: { order: OrderDto; onClose: () => void }) {
  const ids = useId();
  const edit = useEditOrder();
  const menu = useMenuItems();
  const [quantities, setQuantities] = useState<Record<number, number>>(() =>
    Object.fromEntries(order.items.map((item) => [item.id, item.quantity])),
  );
  const [reason, setReason] = useState("");
  const trimmed = reason.trim();

  // A round merged into another table's bill moves that bill, so preview the primary's.
  const own = useSessionDetail(order.sessionId);
  const primaryId = own.data?.session.mergedIntoSessionId ?? 0;
  const primary = useSessionDetail(primaryId);
  const billOwner = primaryId > 0 ? primary.data?.session : own.data?.session;

  const changes: OrderEditLine[] = order.items
    .filter((item) => quantities[item.id] !== item.quantity)
    .map((item) => ({ itemId: item.id, quantity: quantities[item.id] }));
  const removedAmount = order.items
    .filter((item) => item.kind === "AlaCarte")
    .reduce((sum, item) => sum + (item.quantity - quantities[item.id]) * item.unitPrice, 0);
  const cancelsOrder = order.items.every((item) => quantities[item.id] === 0);
  const imageOf = new Map((menu.data ?? []).map((item) => [item.id, item.imagePath]));

  const save = (items: OrderEditLine[]) => {
    if (trimmed.length === 0 || items.length === 0 || edit.isPending) return;
    edit.mutate(
      { order, reason: trimmed, items },
      {
        onSuccess: onClose,
        onError: (error) => {
          if (isStaleOrderError(error) || (error instanceof ApiError && error.code === "ORDER_NOT_EDITABLE")) onClose();
        },
      },
    );
  };

  return (
    <Dialog open onOpenChange={(next) => (next ? undefined : onClose())}>
      <DialogContent showCloseButton={false} className="flex flex-col gap-0 p-0 sm:max-w-[600px]">
        <div className="flex items-start gap-3 px-6 pt-5 pb-4">
          <DialogHeader className="grow gap-1">
            <DialogTitle className="text-[24px] leading-[1.35] font-semibold tracking-[-0.01em]">
              แก้ไขออเดอร์ · <span className="font-mono">{order.tableName}</span> รอบที่{" "}
              <span className="font-mono">{order.number}</span>
            </DialogTitle>
            <DialogDescription className="text-[13px] leading-[1.55]">
              ลูกค้าอาจเอารายการออก · แก้ไขได้ขณะรอรับหรือกำลังทำ
            </DialogDescription>
          </DialogHeader>
          <DialogClose asChild>
            <Button variant="ghost" size="icon" aria-label="ปิด">
              <XIcon className="size-6" strokeWidth={1.75} aria-hidden />
            </Button>
          </DialogClose>
        </div>
        <hr className="border-border" />

        <div className="max-h-[calc(100dvh-16rem)] overflow-y-auto px-6 pt-1 pb-5">
          <ul className="flex flex-col">
            {order.items.map((item) => (
              <EditLine
                key={item.id}
                item={item}
                quantity={quantities[item.id]}
                imagePath={imageOf.get(item.menuItemId) ?? null}
                onQuantity={(quantity) =>
                  setQuantities((current) => ({
                    ...current,
                    [item.id]: Math.max(0, Math.min(item.quantity, quantity)),
                  }))
                }
              />
            ))}
          </ul>

          <div className="mt-4 flex flex-col gap-2">
            <span id={`${ids}-reason`} className="text-[13px] font-semibold text-ink-2">
              เหตุผลในการแก้ไข
            </span>
            <div role="group" aria-labelledby={`${ids}-reason`} className="flex flex-wrap gap-2">
              {EDIT_REASON_CHIPS.map((chip) => {
                const on = trimmed === chip;
                return (
                  <button
                    key={chip}
                    type="button"
                    aria-pressed={on}
                    onClick={() => setReason(chip)}
                    className={cn(
                      "inline-flex h-[34px] items-center rounded-full border px-3.5 text-sm whitespace-nowrap focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
                      on
                        ? "border-orange-line bg-orange-soft font-semibold text-orange-ink"
                        : "border-border bg-surface font-medium text-ink-2 hover:bg-sunken",
                    )}
                  >
                    {chip}
                  </button>
                );
              })}
            </div>
            <input
              value={reason}
              maxLength={EDIT_REASON_MAX}
              onChange={(event) => setReason(event.target.value)}
              placeholder="หรือพิมพ์เหตุผล"
              aria-labelledby={`${ids}-reason`}
              aria-describedby={`${ids}-reason-help`}
              required
              className="h-10 w-full rounded-md border border-input bg-surface px-3.5 text-[15px] outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-orange-soft"
            />
            {changes.length > 0 && trimmed.length === 0 ? (
              <span id={`${ids}-reason-help`} className="text-[12.5px] text-cancel-ink">
                ต้องระบุเหตุผลในการแก้ไข
              </span>
            ) : null}
          </div>

          <BillImpact
            tableName={billOwner?.tableName ?? order.tableName}
            before={billOwner?.bill.total ?? null}
            after={billOwner ? billTotalWithout(billOwner.bill, removedAmount) : null}
            cancelsOrder={cancelsOrder}
          />
        </div>
        <hr className="border-border" />

        <div className="flex items-center justify-between gap-3 px-6 py-4">
          <Button
            variant="destructive"
            disabled={trimmed.length === 0 || edit.isPending}
            onClick={() => save(order.items.map((item) => ({ itemId: item.id, quantity: 0 })))}
          >
            <BanIcon className="size-4" strokeWidth={1.75} aria-hidden />
            ยกเลิกทั้งออเดอร์
          </Button>
          <div className="flex gap-2">
            <Button variant="ghost" onClick={onClose}>
              ปิด
            </Button>
            <Button
              disabled={trimmed.length === 0 || changes.length === 0 || edit.isPending}
              onClick={() => save(changes)}
            >
              บันทึกการแก้ไข
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** One line: thumbnail, name, its note (read-only — an edit only reduces), − n + and remove; or, once removed, the struck line and เอาคืน. */
function EditLine({
  item,
  quantity,
  imagePath,
  onQuantity,
}: {
  item: OrderItemDto;
  quantity: number;
  imagePath: string | null;
  onQuantity: (quantity: number) => void;
}) {
  const thumb = (
    <span
      className={cn(
        "flex size-9 flex-none items-center justify-center overflow-hidden rounded-sm bg-sunken text-sm font-semibold text-ink-2",
        quantity === 0 && "opacity-45",
      )}
    >
      {imagePath ? (
        // รูปมาจาก /api/uploads ของร้านเอง จึงไม่ใช้ next/image optimizer
        // eslint-disable-next-line @next/next/no-img-element
        <img src={imagePath} alt="" className="size-full object-cover" />
      ) : (
        item.name.slice(0, 1)
      )}
    </span>
  );

  if (quantity === 0) {
    return (
      <li className="flex items-center gap-3 border-border py-3 text-muted-foreground not-first:border-t">
        {thumb}
        <div className="flex grow flex-col gap-0.5">
          <b className="text-[17px] leading-[1.4] font-medium line-through">{item.name}</b>
          <span className="text-[12.5px]">
            นำออกจากรอบนี้
            {item.kind === "AlaCarte" ? (
              <>
                {" "}
                · คิดเงินแยก <span className="tabular-nums">{formatBaht(item.unitPrice * item.quantity)}</span>
              </>
            ) : null}
          </span>
        </div>
        <Button variant="ghost" size="sm" className="text-ink" onClick={() => onQuantity(item.quantity)}>
          <Undo2Icon className="size-4" strokeWidth={1.75} aria-hidden />
          เอาคืน
        </Button>
      </li>
    );
  }

  return (
    <li className="flex items-center gap-3 border-border py-3 not-first:border-t">
      {thumb}
      <div className="flex min-w-0 grow flex-col gap-1.5">
        <b className="text-[17px] leading-[1.4] font-semibold">{item.name}</b>
        {item.note ? (
          <span
            aria-label={`หมายเหตุของ ${item.name}`}
            className="flex h-[34px] items-center gap-2 rounded-sm border border-input bg-surface px-3 text-sm text-ink"
          >
            <PencilIcon className="size-4 flex-none text-muted-foreground" strokeWidth={1.75} aria-hidden />
            <span className="truncate">{item.note}</span>
          </span>
        ) : null}
      </div>
      <div className="flex flex-none items-center gap-1">
        <StepButton label={`ลด ${item.name}`} disabled={quantity <= 1} onClick={() => onQuantity(quantity - 1)}>
          <MinusIcon className="size-5" strokeWidth={1.75} aria-hidden />
        </StepButton>
        <b className="min-w-7 text-center text-base font-semibold tabular-nums" aria-live="polite">
          {quantity}
        </b>
        <StepButton
          label={`เพิ่ม ${item.name}`}
          accent
          disabled={quantity >= item.quantity}
          onClick={() => onQuantity(quantity + 1)}
        >
          <PlusIcon className="size-5" strokeWidth={1.75} aria-hidden />
        </StepButton>
      </div>
      <Button variant="ghost" size="icon" aria-label={`นำ ${item.name} ออก`} onClick={() => onQuantity(0)}>
        <Trash2Icon className="size-5" strokeWidth={1.75} aria-hidden />
      </Button>
    </li>
  );
}

/** Round 40px stepper button of frame o17 (DESIGN `.stepper .s-btn`, `.add` orange, `.off` sunken). */
function StepButton({
  label,
  disabled,
  accent,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  accent?: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        "inline-flex size-10 flex-none items-center justify-center rounded-full border focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
        accent ? "border-orange-strong bg-orange-strong text-on-accent" : "border-line-strong bg-surface text-ink",
        disabled && "border-border bg-sunken text-muted-foreground",
      )}
    >
      {children}
    </button>
  );
}

/** ยอดบิลโต๊ะ before → after with the difference; the server's bill is the "before". */
function BillImpact({
  tableName,
  before,
  after,
  cancelsOrder,
}: {
  tableName: string;
  before: number | null;
  after: number | null;
  cancelsOrder: boolean;
}) {
  const changed = before !== null && after !== null && after !== before;

  return (
    <div className="mt-4 rounded-xl bg-sunken px-3.5 py-3">
      <div className="flex items-center justify-between gap-3">
        <span className="text-[13px] font-semibold text-ink">
          ยอดบิลโต๊ะ <span className="font-mono">{tableName}</span>
        </span>
        {changed ? (
          <span className="text-[15px] text-ready-ink tabular-nums">{formatBaht(after - before)}</span>
        ) : null}
      </div>
      <div className="mt-1 flex items-baseline gap-3">
        {before === null || after === null ? (
          <span className="text-[20px] leading-[1.4] font-semibold text-muted-foreground">…</span>
        ) : (
          <>
            {changed ? (
              <>
                <span className="text-[20px] leading-[1.4] font-semibold text-muted-foreground tabular-nums line-through">
                  {formatBaht(before)}
                </span>
                <ArrowRightIcon className="size-4 self-center text-muted-foreground" strokeWidth={1.75} aria-hidden />
              </>
            ) : null}
            <span className="text-[20px] leading-[1.4] font-semibold tabular-nums">{formatBaht(after)}</span>
          </>
        )}
      </div>
      <p className="mt-1.5 text-[12.5px] text-muted-foreground">
        {cancelsOrder ? "นำออกครบทุกรายการ = ยกเลิกทั้งออเดอร์ · " : ""}ลูกค้าจะเห็นรายการที่เหลือในหน้าออเดอร์
      </p>
    </div>
  );
}
