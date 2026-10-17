"use client";

import { CircleAlertIcon, PencilIcon, SendIcon, Trash2Icon } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  CART_NOTE_MAX_LENGTH,
  CART_SENT_MESSAGE,
  cartErrorMessage,
  useCart,
  useSubmitOrder,
  type CartLine,
} from "@/lib/cart";
import { cn } from "@/lib/utils";
import { CartLineMeta, QuantityStepper } from "./quantity-stepper";

/** รูปย่อ 48px ในชีตตะกร้า (DESIGN: 48px ในตะกร้าและตารางเจ้าของร้าน) */
function LineThumb({ line }: { line: CartLine }) {
  return (
    <div className="flex size-12 flex-none items-center justify-center overflow-hidden rounded-[10px] bg-sunken text-title font-semibold text-ink-2">
      {line.imagePath ? (
        // รูปมาจาก /api/uploads ของร้านเอง จึงไม่ใช้ next/image optimizer
        // eslint-disable-next-line @next/next/no-img-element
        <img src={line.imagePath} alt="" className="size-full object-cover" />
      ) : (
        line.name.slice(0, 1)
      )}
    </div>
  );
}

/**
 * หมายเหตุต่อรายการ (story 15): ปุ่มดินสออยู่ในบรรทัดเดียวกับชื่อเมนูเสมอ (ไม่กินความสูงเพิ่ม
 * เพื่อให้ชีตครบลิมิตของ c5 พอดีจอเหมือน mockup) และเมื่อกดแล้วช่องกรอกจะโผล่ใต้บรรทัดนั้น
 * บรรทัดที่มีหมายเหตุอยู่แล้วแสดงช่องกรอกให้เลย
 */
function NoteButton({ line, onExpand }: { line: CartLine; onExpand: () => void }) {
  return (
    <button
      type="button"
      aria-label={`เพิ่มหมายเหตุของ ${line.name}`}
      onClick={onExpand}
      className="inline-flex size-7 flex-none items-center justify-center rounded-sm text-muted-foreground hover:bg-sunken"
    >
      <PencilIcon className="size-4" strokeWidth={1.75} aria-hidden />
    </button>
  );
}

/** ช่องกรอกหมายเหตุ ≤100 ตัวอักษร (ขอบเขตเดียวกับคอลัมน์ OrderItem.Note) พร้อมตัวนับเมื่อใกล้เต็ม */
function NoteField({ line, autoFocus }: { line: CartLine; autoFocus: boolean }) {
  const { setNote } = useCart();

  return (
    <div className="relative mt-1.5">
      <PencilIcon
        className="pointer-events-none absolute top-1/2 left-3.5 size-4 -translate-y-1/2 text-muted-foreground"
        strokeWidth={1.75}
        aria-hidden
      />
      <input
        value={line.note}
        maxLength={CART_NOTE_MAX_LENGTH}
        autoFocus={autoFocus}
        onChange={(event) => setNote(line.id, event.target.value)}
        placeholder="เพิ่มหมายเหตุ"
        aria-label={`หมายเหตุของ ${line.name}`}
        className={cn(
          "h-11 w-full rounded-md border border-input bg-surface pr-3.5 pl-10 text-[15px] outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-orange-soft",
          line.note === "" && "border-dashed",
        )}
      />
      {line.note.length >= CART_NOTE_MAX_LENGTH - 20 ? (
        <span className="absolute top-full right-1 mt-0.5 text-[11.5px] text-muted-foreground tabular-nums">
          {line.note.length} / {CART_NOTE_MAX_LENGTH}
        </span>
      ) : null}
    </div>
  );
}

/**
 * ชีตตะกร้า (frame c2 + สถานะ "ครบลิมิต" ของ c5) — self-contained: รับ `open`/`onOpenChange` เท่านั้น
 * แล้วอ่านทุกอย่างจาก `CartProvider` จึง mount เข้ากับหน้าเมนูของ ticket 09 ได้ด้วยสองบรรทัด
 */
export function CartSheet({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { token, lines, itemCount, isFull, limits, orderWindow, clear } = useCart();
  const submit = useSubmitOrder(token);
  // บรรทัดที่เพิ่งกด "เพิ่มหมายเหตุ" (frame c2 แสดงช่องกรอกของบรรทัดนั้นแบบเส้นประ)
  const [editingNote, setEditingNote] = useState<number | null>(null);
  const closed = orderWindow.isClosed;
  const empty = itemCount === 0;

  const send = () => {
    if (closed || empty || submit.isPending) return;

    submit.mutate(lines ?? [], {
      onSuccess: () => {
        onOpenChange(false);
        toast.success(CART_SENT_MESSAGE);
      },
    });
  };

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="bottom"
        showCloseButton={false}
        aria-describedby={undefined}
        className="max-h-[85dvh] gap-0 overflow-y-auto rounded-t-[20px] px-4 pb-5"
      >
        <div className="mx-auto mt-2 mb-1 h-1 w-10 rounded-full bg-control-border" aria-hidden />
        <SheetHeader className="mt-2 flex-row items-start justify-between gap-3 p-0">
          <div className="flex flex-col">
            <SheetTitle className="text-headline">ตะกร้า</SheetTitle>
            <SheetDescription className="text-label">ยังไม่ได้ส่ง</SheetDescription>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={empty}
            onClick={clear}
            className="text-ink-2"
          >
            <Trash2Icon aria-hidden />
            ล้างตะกร้า
          </Button>
        </SheetHeader>

        {empty ? (
          <p className="py-8 text-center text-body text-muted-foreground">
            ยังไม่มีรายการในตะกร้า · เลือกเมนูจากหน้าเมนูก่อน
          </p>
        ) : (
          <ul className="mt-1">
            {(lines ?? []).map((line) => {
              const showField = editingNote === line.id || line.note !== "";

              return (
                <li key={line.id} className="border-t border-border py-2.5 first:border-t-0">
                  <div className="flex items-center gap-3">
                    <LineThumb line={line} />
                    <div className="flex min-w-0 flex-1 items-center gap-1">
                      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                        <span className="text-[16px] leading-snug font-semibold">{line.name}</span>
                        <CartLineMeta kind={line.kind} price={line.price} />
                      </div>
                      {showField ? null : (
                        <NoteButton line={line} onExpand={() => setEditingNote(line.id)} />
                      )}
                    </div>
                    <QuantityStepper item={line} />
                  </div>
                  {showField ? (
                    <NoteField line={line} autoFocus={editingNote === line.id && line.note === ""} />
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}

        {isFull ? (
          <div
            role="status"
            className="mt-3 flex items-center gap-2.5 rounded-lg bg-cancel-soft px-3.5 py-2.5 text-[14px] font-medium text-cancel-ink"
          >
            <CircleAlertIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
            สั่งได้สูงสุด {limits.maxQtyPerItem} ต่อเมนูต่อรอบ
          </div>
        ) : null}

        <div className="mt-3 rounded-lg bg-sunken px-3.5 py-3">
          <div className="flex items-center justify-between">
            <span className="text-label text-muted-foreground">รายการในรอบนี้</span>
            <span className="text-title font-semibold tabular-nums">
              {itemCount} / {limits.maxItems} รายการ
            </span>
          </div>
          <div className="mt-0.5 text-[12px] leading-snug text-muted-foreground">
            {isFull ? null : `สั่งได้สูงสุด ${limits.maxQtyPerItem} ต่อเมนูต่อรอบ`}
          </div>
        </div>

        {closed ? (
          <p className="mt-3 text-center text-label text-cancel-ink">หมดเวลาสั่งอาหาร · กรุณาติดต่อพนักงาน</p>
        ) : null}

        <Button
          type="button"
          size="lg"
          className="mt-3 w-full"
          disabled={closed || empty || submit.isPending}
          onClick={send}
        >
          <SendIcon aria-hidden />
          ส่งออเดอร์
        </Button>

        {submit.isError ? (
          <p
            role="alert"
            className={cn(
              "mt-2 flex items-start gap-2 rounded-lg bg-cancel-soft px-3.5 py-2.5 text-[14px] font-medium text-cancel-ink",
            )}
          >
            <CircleAlertIcon className="mt-px size-4 flex-none" strokeWidth={1.75} aria-hidden />
            {cartErrorMessage(submit.error, "ส่งออเดอร์ไม่สำเร็จ ลองอีกครั้ง")}
          </p>
        ) : null}
      </SheetContent>
    </Sheet>
  );
}
