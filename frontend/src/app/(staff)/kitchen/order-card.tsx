import { HandPlatterIcon, PencilIcon } from "lucide-react";
import type { ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { formatTime, minutesUsed } from "@/lib/format";
import { type BoardStatus, LATE_AFTER_MINUTES } from "@/lib/kitchen";
import type { OrderDto } from "@/lib/types";

export const STATUS_LABEL: Record<BoardStatus, string> = {
  Pending: "รอรับ",
  Preparing: "กำลังทำ",
  Ready: "พร้อมเสิร์ฟ",
};

const STATUS_BADGE: Record<BoardStatus, "pending" | "preparing" | "ready"> = {
  Pending: "pending",
  Preparing: "preparing",
  Ready: "ready",
};

/**
 * One round on the kitchen board (frame k1): table, round number and send time, the wait since it
 * was sent on the rail (red past ten minutes), the lines with quantities, and every line note in the
 * pending tint so it cannot be missed.
 *
 * `now` is server time (see `useServerTime`), so the wait is server-stored `createdAt` against the
 * server clock: it only ever counts up and is never a countdown. A Ready card says
 * "รอพนักงานนำไปเสิร์ฟ" — Ready is the last status, nothing on this board marks a round as served.
 *
 * `actions` is the slot for the status buttons and the ⋮ menu of ticket 12; without it the card ends
 * after the lines (Pending/Preparing) or after the waiting chip (Ready).
 */
export function OrderCard({
  order,
  status,
  now,
  isNew,
  actions,
}: {
  order: OrderDto;
  status: BoardStatus;
  now: number;
  isNew: boolean;
  actions?: ReactNode;
}) {
  const waited = minutesUsed(order.createdAt, now);
  const isLate = waited > LATE_AFTER_MINUTES;
  // The rail fills over the ten minutes and then stays full; a fresh round still shows a sliver.
  const fill = Math.max(5, Math.min(100, (waited / LATE_AFTER_MINUTES) * 100));
  const notes = order.items.filter((item) => item.note);
  const hasFooter = Boolean(actions) || status === "Ready";

  return (
    <article
      aria-label={`โต๊ะ ${order.tableName} รอบที่ ${order.number}`}
      className={`flex-none overflow-hidden rounded-xl bg-surface ${
        isNew
          ? "border-2 border-alert shadow-alert-ring motion-safe:animate-alert-pulse"
          : "border border-border"
      }`}
    >
      <div className={`border-b px-3 pt-2 pb-1.5 ${isNew ? "border-alert bg-pending-soft" : "border-border"}`}>
        <div className="flex items-center justify-between gap-2">
          <div className="flex min-w-0 items-center gap-2">
            <span className="text-[28px] leading-[1.2] font-semibold tracking-[-0.01em]">{order.tableName}</span>
            <span className="text-sm font-medium whitespace-nowrap text-ink-2">
              รอบที่ {order.number} · ส่ง {formatTime(order.createdAt)}
            </span>
          </div>
          <div className="flex flex-none items-center gap-1.5">
            {isNew ? (
              <span className="inline-flex h-6 items-center rounded-full bg-alert px-2.5 text-[12.5px] font-semibold text-ink">
                ใหม่
              </span>
            ) : null}
            <Badge variant={STATUS_BADGE[status]}>{STATUS_LABEL[status]}</Badge>
          </div>
        </div>
        <div className="mt-1 flex items-center gap-3">
          <span className="block grow py-[3px]" aria-hidden>
            <span className="block h-2 overflow-hidden rounded-full bg-hairline">
              <span
                className={`block h-full rounded-full ${isLate ? "bg-cancel-dot" : "bg-orange"}`}
                style={{ width: `${fill}%` }}
              />
            </span>
          </span>
          <span
            className={`text-[17px] font-semibold whitespace-nowrap ${isLate ? "text-cancel-ink" : "text-ink-2"}`}
          >
            {status === "Ready" ? "รอเสิร์ฟ " : ""}
            <span className="font-mono">{waited < 1 ? "<1" : waited}</span> นาที
          </span>
        </div>
      </div>

      <ul
        className={`flex flex-col gap-0.5 px-3 pt-2 ${hasFooter || notes.length > 0 || order.editReason ? "" : "pb-3"}`}
      >
        {order.items.map((item, index) => (
          <li key={index} className="flex gap-2 text-[18px] leading-normal">
            <b className="min-w-8 font-semibold tabular-nums">×{item.quantity}</b>
            <span>{item.name}</span>
          </li>
        ))}
      </ul>

      {notes.length > 0 ? (
        <div className={`flex flex-col gap-1.5 px-3 pt-2 ${hasFooter || order.editReason ? "" : "pb-3"}`}>
          {notes.map((item, index) => (
            <p
              key={index}
              className="flex items-center gap-1.5 rounded-sm bg-pending-soft px-2.5 py-[3px] text-[15px] leading-normal font-medium text-pending-ink"
            >
              <PencilIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
              {item.name}: {item.note}
            </p>
          ))}
        </div>
      ) : null}

      {/* ticket 17 — the owner's last edit (lines reduced/removed) reads like a cancel reason */}
      {order.editReason ? (
        <p className={`px-3 pt-2 text-[15px] leading-normal text-ink-2 ${hasFooter ? "" : "pb-3"}`}>
          แก้ไขแล้ว · {order.editReason}
        </p>
      ) : null}

      {hasFooter ? (
        <div className="flex gap-2 px-3 pt-1.5 pb-2">
          {status === "Ready" ? (
            <span className="inline-flex h-16 grow items-center gap-2.5 rounded-xl bg-ready-soft px-4 text-base font-semibold whitespace-nowrap text-ready-ink">
              <HandPlatterIcon className="size-5 flex-none" strokeWidth={1.75} aria-hidden />
              รอพนักงานนำไปเสิร์ฟ
            </span>
          ) : null}
          {actions}
        </div>
      ) : null}
    </article>
  );
}
