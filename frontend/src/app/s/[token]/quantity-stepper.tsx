"use client";

import { CheckIcon, MinusIcon, PlusIcon } from "lucide-react";
import { useCart, type CartMenuItem } from "@/lib/cart";
import { cn } from "@/lib/utils";

/** ปุ่มกลมของ stepper — 44px ตามกติกาสัมผัสของ DESIGN (frame c2/c5 ย่อเป็น 40px บนแถวเมนู) */
function StepButton({
  label,
  disabled,
  accent,
  compact,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  accent?: boolean;
  compact?: boolean;
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
        "inline-flex flex-none items-center justify-center rounded-full border",
        compact ? "size-10" : "size-11",
        accent
          ? "border-orange-strong bg-orange-strong text-on-accent"
          : "border-line-strong bg-surface text-ink",
        disabled && "border-border bg-sunken text-muted-foreground",
      )}
    >
      {children}
    </button>
  );
}

/**
 * ปุ่ม +/− ที่หยุดเมื่อถึงลิมิต (story 13) — ตะกร้าและแถวเมนูของหน้าลูกค้าใช้ตัวเดียวกัน
 * ตัวเลขอ่านจากตะกร้าเสมอ จึงไม่มีทางที่สองที่จะแสดงจำนวนไม่ตรงกัน
 * `compact` = ขนาดของแถวเมนูด้านหลัง (frame c2/c5) เทียบกับขนาดในชีตตะกร้า
 */
export function QuantityStepper({ item, compact }: { item: CartMenuItem; compact?: boolean }) {
  const { quantityOf, add, setQuantity, isFull, limits, orderWindow } = useCart();
  const quantity = quantityOf(item.id);
  const canAddMore = !isFull && quantity < limits.maxQtyPerItem && !orderWindow.isClosed;

  if (quantity === 0) {
    return (
      <StepButton
        label={`เพิ่ม ${item.name}`}
        compact={compact}
        accent
        disabled={!canAddMore}
        onClick={() => add(item)}
      >
        <PlusIcon className={compact ? "size-5" : "size-6"} strokeWidth={1.75} aria-hidden />
      </StepButton>
    );
  }

  return (
    <div className="inline-flex flex-none items-center gap-1">
      <StepButton
        label={`ลด ${item.name}`}
        compact={compact}
        disabled={orderWindow.isClosed}
        onClick={() => setQuantity(item.id, quantity - 1)}
      >
        <MinusIcon className={compact ? "size-5" : "size-6"} strokeWidth={1.75} aria-hidden />
      </StepButton>
      <b className="min-w-6.5 text-center text-body font-semibold tabular-nums">{quantity}</b>
      <StepButton
        label={`เพิ่ม ${item.name}`}
        compact={compact}
        accent
        disabled={!canAddMore}
        onClick={() => setQuantity(item.id, quantity + 1)}
      >
        <PlusIcon className={compact ? "size-5" : "size-6"} strokeWidth={1.75} aria-hidden />
      </StepButton>
    </div>
  );
}

/** บรรทัดใต้ชื่อเมนู: บุฟเฟ่ต์บอกว่ารวมแล้ว, à la carte บอกราคาที่คิดเงินแยก (story 7) */
export function CartLineMeta({ kind, price }: { kind: CartMenuItem["kind"]; price: number }) {
  if (kind === "Buffet") {
    return (
      <span className="inline-flex items-center gap-1 text-[12.5px] leading-tight font-medium text-orange-ink">
        <CheckIcon className="size-4" strokeWidth={2} aria-hidden />
        รวมในบุฟเฟ่ต์
      </span>
    );
  }

  return (
    <span className="text-[12.5px] leading-tight font-medium text-muted-foreground">
      {formatSeparatePrice(price)} คิดเงินแยก
    </span>
  );
}

/** `฿45` — ราคาที่คิดเงินแยกบนแถวเมนู/ตะกร้า (frame ใช้รูปแบบไม่มีทศนิยม) */
function formatSeparatePrice(price: number): string {
  return `฿${price.toLocaleString("th-TH", { maximumFractionDigits: 2 })}`;
}
