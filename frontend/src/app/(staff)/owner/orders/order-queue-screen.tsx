"use client";

import { type ReactNode, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { useServerTime } from "@/lib/cart";
import { bangkokDay, formatDayHeading, formatTime, minutesUsed } from "@/lib/format";
import { BOARD_STATUSES, type BoardStatus, LATE_AFTER_MINUTES, useKitchenBoard } from "@/lib/kitchen";
import { useCompletedToday, useOrderQueueLiveUpdates } from "@/lib/order-queue";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import type { OrderDto } from "@/lib/types";
import { CancelOrderDialog } from "../../kitchen/cancel-order-dialog";
import { useFreshOrders } from "../../kitchen/kitchen-board";
import { STATUS_LABEL } from "../../kitchen/order-card";
import { QueueCard } from "./queue-card";

type Filter = BoardStatus | "all";

/**
 * `/owner/orders` — frame o08: every round of a table that has not paid yet as a queue card, filtered
 * by status with a count per status and the late count, one status button per card (the owner's own
 * account, ticket 12's endpoints), cancel with a reason, and "เสร็จแล้ววันนี้" — today's Ready and
 * Cancelled rounds. Both lists follow the hub (`useOrderQueueLiveUpdates`); a reconnect refetches.
 * Kitchen accounts never reach this route: the staff layout's guard sends them to `/kitchen`.
 */
export function OrderQueueScreen() {
  return (
    <LiveProvider>
      <OrderQueueBody />
    </LiveProvider>
  );
}

function OrderQueueBody() {
  const board = useKitchenBoard();

  if (board.isPending) {
    return (
      <Frame now={null}>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {[0, 1, 2].map((key) => (
            <Skeleton key={key} className="h-72 rounded-2xl" />
          ))}
        </div>
      </Frame>
    );
  }

  if (board.isError) {
    return (
      <Frame now={null}>
        <div className="flex flex-col items-start gap-3 pt-2">
          <p className="text-body text-ink-2">
            {board.error instanceof ApiError
              ? board.error.message
              : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
          </p>
          <Button variant="outline" onClick={() => void board.refetch()}>
            ลองอีกครั้ง
          </Button>
        </div>
      </Frame>
    );
  }

  return <Queue orders={board.data.orders} serverNow={board.data.serverNow} />;
}

function Queue({ orders, serverNow }: { orders: OrderDto[]; serverNow: number }) {
  const now = useServerTime(serverNow);
  const today = bangkokDay(now);
  const { fresh, mark } = useFreshOrders();
  useOrderQueueLiveUpdates(today, (order) => mark(order.id));

  const [filter, setFilter] = useState<Filter>("all");
  // The round whose ⋮ was tapped; the dialog keeps this snapshot even if the card changes meanwhile.
  const [cancelling, setCancelling] = useState<OrderDto | null>(null);

  const late = orders.filter((order) => minutesUsed(order.createdAt, now) > LATE_AFTER_MINUTES).length;
  // รอรับ, then กำลังทำ, then พร้อมเสิร์ฟ (frame o08); oldest first inside each status, as the board answers.
  const shown = BOARD_STATUSES.filter((status) => filter === "all" || filter === status).flatMap((status) =>
    orders.filter((order) => order.status === status).map((order) => ({ order, status })),
  );

  return (
    <Frame now={now}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div role="group" aria-label="กรองตามสถานะ" className="flex flex-wrap gap-2">
          <FilterChip label="ทั้งหมด" count={orders.length} on={filter === "all"} onSelect={() => setFilter("all")} />
          {BOARD_STATUSES.map((status) => (
            <FilterChip
              key={status}
              label={STATUS_LABEL[status]}
              count={orders.filter((order) => order.status === status).length}
              on={filter === status}
              onSelect={() => setFilter(status)}
            />
          ))}
        </div>
        <p className="text-[13px] text-muted-foreground">
          ล่าช้าเกิน {LATE_AFTER_MINUTES} นาที <b className="text-ink tabular-nums">{late}</b> รอบ ·
          ครัวกดเริ่มทำแล้วสถานะบนการ์ดจะเปลี่ยนทันที
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {shown.length === 0 ? (
          <p className="rounded-2xl border border-dashed border-line-strong p-6 text-body text-muted-foreground md:col-span-2 xl:col-span-3">
            {filter === "all" ? "ไม่มีออเดอร์ค้าง" : `ไม่มีออเดอร์ที่${STATUS_LABEL[filter]}`}
          </p>
        ) : (
          shown.map(({ order, status }) => (
            <QueueCard
              key={order.id}
              order={order}
              status={status}
              now={now}
              isNew={fresh.has(order.id)}
              onCancel={() => setCancelling(order)}
            />
          ))
        )}
        <CompletedToday day={today} />
      </div>

      {cancelling ? <CancelOrderDialog order={cancelling} now={now} onClose={() => setCancelling(null)} /> : null}
    </Frame>
  );
}

function FilterChip({
  label,
  count,
  on,
  onSelect,
}: {
  label: string;
  count: number;
  on: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={on}
      onClick={onSelect}
      className={`inline-flex h-[34px] items-center gap-2 rounded-full border px-3.5 text-sm whitespace-nowrap outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
        on
          ? "border-orange-line bg-orange-soft font-semibold text-orange-ink"
          : "border-border bg-surface font-medium text-ink-2 hover:bg-sunken"
      }`}
    >
      {label}
      <b className="font-semibold tabular-nums">{count}</b>
    </button>
  );
}

/**
 * "เสร็จแล้ววันนี้": today's rounds that reached Ready or were cancelled, newest change first, with the
 * time of that change. Ready is the last status (there is no served step), so its row says พร้อมเสิร์ฟ;
 * a cancelled row carries its reason.
 */
function CompletedToday({ day }: { day: string }) {
  const done = useCompletedToday(day);
  const rows = done.data ?? [];
  const ready = rows.filter((order) => order.status === "Ready").length;

  return (
    <section
      aria-labelledby="done-today"
      className="flex flex-col rounded-2xl border border-border bg-surface p-4 md:col-span-2"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2 pb-2">
        <h2 id="done-today" className="text-title">
          เสร็จแล้ววันนี้
        </h2>
        {done.data ? (
          <span className="text-[13px] text-muted-foreground">
            ล่าสุด <span className="tabular-nums">{rows.length}</span> รอบ · พร้อมเสิร์ฟ{" "}
            <span className="tabular-nums">{ready}</span> · ยกเลิก{" "}
            <span className="tabular-nums">{rows.length - ready}</span>
          </span>
        ) : null}
      </div>

      {done.isPending ? (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-11" />
          <Skeleton className="h-11" />
        </div>
      ) : done.isError ? (
        <div className="flex flex-wrap items-center gap-3 py-2">
          <p className="text-body text-ink-2">
            {done.error instanceof ApiError ? done.error.message : "โหลดรายการของวันนี้ไม่ได้"}
          </p>
          <Button variant="outline" size="sm" onClick={() => void done.refetch()}>
            ลองอีกครั้ง
          </Button>
        </div>
      ) : rows.length === 0 ? (
        <p className="py-3 text-body text-muted-foreground">ยังไม่มีรอบที่เสร็จวันนี้</p>
      ) : (
        <ul className="max-h-[420px] divide-y divide-border overflow-y-auto">
          {rows.map((order) => (
            <li key={order.id} className="flex min-h-14 items-center gap-4 py-2">
              <span className="w-12 flex-none text-base font-semibold">{order.tableName}</span>
              <span className="min-w-0 grow truncate text-sm text-ink-2">
                รอบที่ {order.number}
                {order.status === "Cancelled" && order.cancelReason ? ` · ${order.cancelReason}` : ""}
              </span>
              <span className="flex-none text-sm text-muted-foreground tabular-nums">{formatTime(order.updatedAt)}</span>
              {order.status === "Cancelled" ? (
                <Badge variant="cancelled">ยกเลิก</Badge>
              ) : (
                <Badge variant="ready">พร้อมเสิร์ฟ</Badge>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** The o08 heading — title, today's date and the server clock, live badge — above the queue. */
function Frame({ now, children }: { now: number | null; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex min-h-20 flex-wrap items-center justify-between gap-4">
        <div className="flex flex-col">
          <h1 className="text-headline">คิวออเดอร์</h1>
          <span className="text-label font-normal text-muted-foreground">
            {now == null ? "ทุกรอบที่ครัวยังไม่ปิดบิล" : `${formatDayHeading(now)} · ${formatTime(now)} · ทุกรอบที่ครัวยังไม่ปิดบิล`}
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <LiveBadge />
          <span className="inline-flex h-[22px] items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
            DEMO DATA
          </span>
        </div>
      </div>
      {children}
    </div>
  );
}

/** Green while the hub is up; the offline wording of the other staff screens while it reconnects. */
function LiveBadge() {
  const isOffline = useLiveOffline();
  return (
    <span
      role="status"
      className={`inline-flex h-8 items-center gap-2 rounded-full px-3 text-label font-semibold ${
        isOffline ? "bg-sunken text-ink-2" : "bg-ready-soft text-ready-ink"
      }`}
    >
      <i className={`size-[7px] rounded-full ${isOffline ? "bg-served-dot" : "bg-ready-dot"}`} aria-hidden />
      {isOffline ? "ออฟไลน์ · ข้อมูลอาจไม่ใหม่" : "เชื่อมต่อแบบเรียลไทม์"}
    </span>
  );
}
