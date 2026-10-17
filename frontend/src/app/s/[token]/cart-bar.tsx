"use client";

import { ChevronRightIcon, ClockAlertIcon, ShoppingBasketIcon } from "lucide-react";
import { useCart } from "@/lib/cart";
import { cn } from "@/lib/utils";

/**
 * แถบตะกร้าลอย (frame c2/c5) — นับ "เมนู" เป็นจำนวนบรรทัด และ "รายการ" เป็นผลรวมจำนวน
 * เมื่อเลย EndsAt แถบเปลี่ยนเป็นสีเทาและกดไม่ได้ (c5 "หมดเวลาสั่งอาหาร")
 */
export function CartBar({ onOpen, className }: { onOpen: () => void; className?: string }) {
  const { lines, itemCount, limits, orderWindow } = useCart();
  const lineCount = lines?.length ?? 0;
  const disabled = itemCount === 0 || orderWindow.isClosed;

  return (
    <button
      type="button"
      onClick={onOpen}
      disabled={disabled}
      aria-label={`เปิดตะกร้า · ${lineCount} เมนู · ${itemCount} จาก ${limits.maxItems} รายการ`}
      className={cn(
        "flex h-13 w-full items-center justify-between gap-2 rounded-lg px-6 text-base font-semibold",
        disabled ? "bg-sunken text-muted-foreground" : "bg-primary text-primary-foreground shadow-float",
        className,
      )}
    >
      <span className="inline-flex items-center gap-2">
        <ShoppingBasketIcon className="size-5" strokeWidth={1.75} aria-hidden />
        ตะกร้า · {lineCount} เมนู
      </span>
      <span className="inline-flex items-center gap-2">
        <span className="font-medium tabular-nums">
          {itemCount} / {limits.maxItems} รายการ
        </span>
        <ChevronRightIcon className="size-5" strokeWidth={1.75} aria-hidden />
      </span>
    </button>
  );
}

/**
 * ประกาศเหนือรายการเมนู (frame c5 กล่องที่ 1 และ 3): "Last order" เป็นข้อความเท่านั้น
 * (ไม่มีตัวนับถอยหลัง) และเมื่อเลยเวลาสั่งอาหารจะขึ้นกล่องสีแดงแทนพร้อมปิดการสั่งทั้งหน้า
 */
export function OrderWindowBanner() {
  const { orderWindow } = useCart();

  if (orderWindow.isClosed) {
    return (
      <div
        role="status"
        className="flex items-center gap-2.5 rounded-lg bg-cancel-soft px-3.5 py-2.5 text-[14px] font-medium text-cancel-ink"
      >
        <ClockAlertIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
        หมดเวลาสั่งอาหาร · กรุณาติดต่อพนักงาน
      </div>
    );
  }

  if (orderWindow.isLastOrder) {
    return (
      <div
        role="status"
        className="flex items-center gap-2.5 rounded-lg bg-orange-soft px-3.5 py-2.5 text-[14px] font-medium text-orange-ink"
      >
        <ClockAlertIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
        Last order: ใกล้หมดเวลาแล้ว กรุณาสั่งรอบสุดท้าย
      </div>
    );
  }

  return null;
}
