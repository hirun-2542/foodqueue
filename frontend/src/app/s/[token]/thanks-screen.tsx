"use client";

import { CheckIcon } from "lucide-react";

/**
 * Frame `c6-end`, the middle phone: the round was paid, so the customer sees the shop's thanks and knows the
 * QR is dead. Ticket 18 renders it from the `SESSION_CLOSED` answer, which carries `closedAs` and `shopName`
 * — and deliberately no money at all (spec 27: the thanks screen never shows an amount).
 */
export function ThanksScreen({ shopName }: { shopName: string | null }) {
  return (
    <div className="flex h-dvh flex-col items-center justify-center gap-3 bg-ground px-8 text-center">
      <span className="flex size-[72px] items-center justify-center rounded-full bg-ready-soft text-ready-ink">
        <CheckIcon className="size-8" strokeWidth={2} aria-hidden />
      </span>
      <h1 className="text-headline">ขอบคุณที่ใช้บริการ</h1>
      <p className="text-body text-ink-2">ชำระเงินเรียบร้อยแล้ว</p>
      {shopName ? <p className="mt-2 text-title">{shopName}</p> : null}
      <p className="text-label font-normal text-muted-foreground">QR นี้ปิดใช้งานแล้ว</p>
    </div>
  );
}
