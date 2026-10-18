"use client";

import { EllipsisVerticalIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { type BoardStatus, NEXT_STATUS, useAdvanceOrder } from "@/lib/kitchen";
import type { OrderDto } from "@/lib/types";

const ADVANCE_LABEL = {
  Pending: "เริ่มทำ",
  Preparing: "พร้อมเสิร์ฟ",
} as const satisfies Record<keyof typeof NEXT_STATUS, string>;

/**
 * The footer of a kitchen card (frame k1), passed to `OrderCard` as `actions`: one large primary button
 * for the next step (เริ่มทำ / พร้อมเสิร์ฟ) sized for wet hands, and ⋮, which opens the cancel dialog.
 * A Ready card has only ⋮ — `OrderCard` already shows "รอพนักงานนำไปเสิร์ฟ" there, and Ready is the last
 * status. The button stays disabled while its request is in flight so a double tap sends one request.
 */
export function OrderActions({
  order,
  status,
  onCancel,
}: {
  order: OrderDto;
  status: BoardStatus;
  onCancel: () => void;
}) {
  const advance = useAdvanceOrder();

  return (
    <>
      {status === "Ready" ? null : (
        <Button
          className="h-16 grow rounded-xl px-7 text-[18px]"
          disabled={advance.isPending}
          onClick={() => advance.mutate({ order, to: NEXT_STATUS[status] })}
        >
          {ADVANCE_LABEL[status]}
        </Button>
      )}
      <Button
        variant="outline"
        className="size-16 flex-none rounded-xl p-0"
        aria-label={`ยกเลิกออเดอร์ ${order.tableName} รอบที่ ${order.number}`}
        aria-haspopup="dialog"
        onClick={onCancel}
      >
        <EllipsisVerticalIcon className="size-6" strokeWidth={1.75} aria-hidden />
      </Button>
    </>
  );
}
