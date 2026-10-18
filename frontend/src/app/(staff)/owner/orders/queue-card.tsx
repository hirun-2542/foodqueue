"use client";

import { EllipsisVerticalIcon, HandPlatterIcon, PencilIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { formatTime, minutesUsed } from "@/lib/format";
import { type BoardStatus, LATE_AFTER_MINUTES, NEXT_STATUS, useAdvanceOrder } from "@/lib/kitchen";
import type { OrderDto } from "@/lib/types";
import { STATUS_LABEL } from "../../kitchen/order-card";
import { MoveTableAction } from "./move-table-dialog";

const STATUS_BADGE = {
  Pending: "pending",
  Preparing: "preparing",
  Ready: "ready",
} as const satisfies Record<BoardStatus, "pending" | "preparing" | "ready">;

const ADVANCE_LABEL = {
  Pending: "เริ่มทำ",
  Preparing: "พร้อมเสิร์ฟ",
} as const satisfies Record<keyof typeof NEXT_STATUS, string>;

/**
 * One round in the owner's queue (frame o08): table, round and send time, the wait on the rail (red
 * past ten minutes, counting up from the server-stored `createdAt` — never a countdown), the lines and
 * their notes, then exactly one status button — เริ่มทำ (Pending) or พร้อมเสิร์ฟ (Preparing) — through
 * ticket 12's shared workflow. Ready is the last status: the card says "รอพนักงานนำไปเสิร์ฟ" instead.
 * ⋮ opens the kitchen's cancel-with-reason dialog. A round that just arrived over the hub wears the
 * alert colour and the ใหม่ tag.
 */
export function QueueCard({
  order,
  status,
  now,
  isNew,
  onCancel,
}: {
  order: OrderDto;
  status: BoardStatus;
  /** Server time (epoch ms). */
  now: number;
  isNew: boolean;
  onCancel: () => void;
}) {
  const advance = useAdvanceOrder();
  const waited = minutesUsed(order.createdAt, now);
  const isLate = waited > LATE_AFTER_MINUTES;
  // The rail fills over the ten minutes and then stays full; a fresh round still shows a sliver.
  const fill = Math.max(5, Math.min(100, (waited / LATE_AFTER_MINUTES) * 100));
  const notes = order.items.filter((item) => item.note);

  return (
    <article
      aria-label={`โต๊ะ ${order.tableName} รอบที่ ${order.number}`}
      className={`flex h-full flex-col rounded-2xl bg-surface p-4 ${
        isNew
          ? "border-2 border-alert shadow-alert-ring motion-safe:animate-alert-pulse"
          : "border border-border"
      }`}
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex min-w-0 items-baseline gap-2">
          <span className="text-[24px] leading-[1.25] font-semibold tracking-[-0.01em]">{order.tableName}</span>
          <span className="text-sm whitespace-nowrap text-ink-2">
            รอบที่ {order.number} · ส่ง <span className="tabular-nums">{formatTime(order.createdAt)}</span>
          </span>
        </div>
        <div className="flex flex-none items-center gap-1.5">
          {isNew ? (
            <span className="inline-flex h-6 items-center gap-1.5 rounded-full bg-alert px-2.5 text-[12.5px] font-semibold text-ink before:size-1.5 before:rounded-full before:bg-ink before:content-['']">
              ใหม่
            </span>
          ) : null}
          <Badge variant={STATUS_BADGE[status]}>{STATUS_LABEL[status]}</Badge>
        </div>
      </div>

      <div className="mt-2.5 flex items-center gap-3">
        <span className="block h-1.5 grow overflow-hidden rounded-full bg-hairline" aria-hidden>
          <span
            className={`block h-full rounded-full ${isLate ? "bg-cancel-dot" : "bg-orange"}`}
            style={{ width: `${fill}%` }}
          />
        </span>
        <span className={`text-[15px] font-semibold whitespace-nowrap ${isLate ? "text-cancel-ink" : "text-ink-2"}`}>
          <span className="tabular-nums">{waited < 1 ? "<1" : waited}</span> นาที
        </span>
      </div>

      <ul className="mt-3 flex flex-col gap-1">
        {order.items.map((item, index) => (
          <li key={index} className="flex gap-3 text-[15px] leading-normal">
            <b className="min-w-7 font-semibold tabular-nums">×{item.quantity}</b>
            <span>{item.name}</span>
          </li>
        ))}
      </ul>

      {notes.length > 0 ? (
        <div className="mt-2.5 flex flex-col gap-1.5">
          {notes.map((item, index) => (
            <p
              key={index}
              className="flex items-center gap-1.5 rounded-sm bg-pending-soft px-2.5 py-1 text-sm font-medium text-pending-ink"
            >
              <PencilIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
              {item.name}: {item.note}
            </p>
          ))}
        </div>
      ) : null}

      <div className="mt-4 grow" />
      <div className="flex gap-2 border-t border-border pt-3">
        {status === "Ready" ? (
          <span className="inline-flex h-[52px] grow items-center justify-center gap-2.5 rounded-xl bg-ready-soft px-4 text-[15px] font-semibold text-ready-ink">
            <HandPlatterIcon className="size-5 flex-none" strokeWidth={1.75} aria-hidden />
            รอพนักงานนำไปเสิร์ฟ
          </span>
        ) : (
          <Button
            className="h-[52px] grow rounded-xl text-base"
            disabled={advance.isPending}
            onClick={() => advance.mutate({ order, to: NEXT_STATUS[status] })}
          >
            {ADVANCE_LABEL[status]}
          </Button>
        )}
        <Button
          variant="outline"
          className="size-[52px] flex-none rounded-xl p-0"
          aria-label={`ยกเลิกออเดอร์ ${order.tableName} รอบที่ ${order.number}`}
          aria-haspopup="dialog"
          onClick={onCancel}
        >
          <EllipsisVerticalIcon className="size-5" strokeWidth={1.75} aria-hidden />
        </Button>
      </div>
      {/*
        Mount point: the card's secondary action row of frame o08 goes here, under the status button —
        - ticket 17 `แก้ไข` (owner edits a Pending/Preparing round, POST /api/orders/{id}/edit; not on Ready),
        - ticket 21 `ย้ายโต๊ะ` (POST /api/sessions/{sessionId}/move),
        - ticket 22 `รวมบิล` (POST /api/sessions/{sessionId}/merge).
        None is rendered until its ticket lands (ticket 14 acceptance).
      */}
      {/* ticket 21 — ย้ายโต๊ะ (every status: a Ready round still belongs to a seated table) */}
      <div className="mt-2 flex gap-2">
        <MoveTableAction order={order} />
      </div>
    </article>
  );
}
