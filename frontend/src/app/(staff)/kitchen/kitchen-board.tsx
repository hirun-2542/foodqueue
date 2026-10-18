"use client";

import { LayoutGridIcon, LogOutIcon, SoupIcon, Volume2Icon, VolumeOffIcon } from "lucide-react";
import Link from "next/link";
import { type ReactNode, useCallback, useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { staffRoutes, useLogout, useMe } from "@/lib/auth";
import { useServerTime } from "@/lib/cart";
import { formatTime, minutesUsed } from "@/lib/format";
import {
  BOARD_STATUSES,
  type BoardStatus,
  LATE_AFTER_MINUTES,
  useKitchenBoard,
  useKitchenLiveUpdates,
} from "@/lib/kitchen";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import type { OrderDto } from "@/lib/types";
import { OrderCard, STATUS_LABEL } from "./order-card";
import { type Chime, useChime } from "./use-chime";

/** How long a just-arrived card keeps the alert colour and the ใหม่ tag. */
const NEW_FOR_MS = 30_000;

/**
 * `/kitchen` — the kitchen display of frame k1 for a landscape tablet: three columns (รอรับ /
 * กำลังทำ / พร้อมเสิร์ฟ) with counters, the live-connection badge, the chime control and a clock.
 * Owner and Kitchen accounts both use it. Status buttons and cancel belong to ticket 12, which
 * passes them to `OrderCard` as `actions`.
 */
export function KitchenBoardScreen() {
  return (
    <LiveProvider>
      <KitchenBoardBody />
    </LiveProvider>
  );
}

/** Cards that arrived over the hub in the last NEW_FOR_MS; the first load highlights nothing. */
function useFreshOrders() {
  const [fresh, setFresh] = useState<ReadonlySet<number>>(() => new Set());
  const timers = useRef(new Map<number, number>());

  useEffect(() => {
    const pending = timers.current;
    return () => {
      for (const timer of pending.values()) window.clearTimeout(timer);
      pending.clear();
    };
  }, []);

  const mark = useCallback((id: number) => {
    setFresh((current) => new Set(current).add(id));
    window.clearTimeout(timers.current.get(id));
    timers.current.set(
      id,
      window.setTimeout(() => {
        timers.current.delete(id);
        setFresh((current) => {
          const next = new Set(current);
          next.delete(id);
          return next;
        });
      }, NEW_FOR_MS),
    );
  }, []);

  return { fresh, mark };
}

function KitchenBoardBody() {
  const board = useKitchenBoard();
  const chime = useChime();
  const { fresh, mark } = useFreshOrders();

  useKitchenLiveUpdates((order: OrderDto) => {
    mark(order.id);
    chime.play();
  });

  if (board.isPending) {
    return (
      <Frame chime={chime} now={null} pendingCount={null} lateCount={null}>
        {BOARD_STATUSES.map((status) => (
          <section key={status} className="flex min-h-0 flex-col gap-3">
            <ColumnHead status={status} count={null} />
            <Skeleton className="h-40 rounded-xl" />
            <Skeleton className="h-32 rounded-xl" />
          </section>
        ))}
      </Frame>
    );
  }

  if (board.isError) {
    return (
      <Frame chime={chime} now={null} pendingCount={null} lateCount={null}>
        <div className="col-span-3 flex flex-col items-start gap-3 pt-6">
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

  return <Board orders={board.data.orders} serverNow={board.data.serverNow} fresh={fresh} chime={chime} />;
}

function Board({
  orders,
  serverNow,
  fresh,
  chime,
}: {
  orders: OrderDto[];
  serverNow: number;
  fresh: ReadonlySet<number>;
  chime: Chime;
}) {
  const now = useServerTime(serverNow);
  const late = orders.filter((order) => minutesUsed(order.createdAt, now) > LATE_AFTER_MINUTES).length;

  return (
    <Frame chime={chime} now={now} pendingCount={orders.length} lateCount={late}>
      {BOARD_STATUSES.map((status) => {
        const column = orders.filter((order) => order.status === status);
        return (
          <section key={status} aria-label={STATUS_LABEL[status]} className="flex min-h-0 flex-col gap-3">
            <ColumnHead status={status} count={column.length} />
            {/* The padding keeps the 7px alert halo of a new card inside the scroll box. */}
            <div className="-mx-2 -mt-2 flex min-h-0 flex-1 flex-col gap-2.5 overflow-y-auto px-2 pt-2 pb-2">
              {column.length === 0 ? (
                <p className="rounded-xl border border-dashed border-line-strong p-5 text-body text-muted-foreground">
                  ยังไม่มีออเดอร์
                </p>
              ) : (
                column.map((order) => (
                  <OrderCard key={order.id} order={order} status={status} now={now} isNew={fresh.has(order.id)} />
                ))
              )}
            </div>
          </section>
        );
      })}
    </Frame>
  );
}

function ColumnHead({ status, count }: { status: BoardStatus; count: number | null }) {
  return (
    <div className="flex h-9 flex-none items-center gap-2.5">
      <h2 className="text-title">{STATUS_LABEL[status]}</h2>
      <span className="inline-flex h-7 min-w-7 items-center justify-center rounded-full bg-sunken px-2 text-sm font-semibold tabular-nums">
        {count ?? "–"}
      </span>
    </div>
  );
}

/** The k1 header bar and the three-column grid under it. */
function Frame({
  chime,
  now,
  pendingCount,
  lateCount,
  children,
}: {
  chime: Chime;
  now: number | null;
  pendingCount: number | null;
  lateCount: number | null;
  children: ReactNode;
}) {
  const { data: me } = useMe();
  const logout = useLogout();

  return (
    <div className="flex h-dvh min-h-[600px] flex-col bg-background">
      <header className="flex h-[68px] flex-none items-center gap-5 border-b border-border bg-surface px-6">
        <div className="flex items-center gap-3">
          <span className="flex size-9 flex-none items-center justify-center rounded-md bg-orange-strong text-on-accent">
            <SoupIcon className="size-6" strokeWidth={1.75} aria-hidden />
          </span>
          <div className="flex flex-col">
            <h1 className="text-base leading-tight font-semibold">จอครัว</h1>
            <span className="text-xs text-muted-foreground">FoodQueue</span>
          </div>
        </div>

        {pendingCount != null && lateCount != null ? (
          <span className="text-sm text-ink-2">
            ออเดอร์ค้าง <b className="tabular-nums">{pendingCount}</b> รอบ · ล่าช้าเกิน 10 นาที{" "}
            <b className="tabular-nums">{lateCount}</b> รอบ
          </span>
        ) : null}

        <div className="grow" />

        <LiveBadge />
        <SoundSwitch chime={chime} />
        <span className="w-[60px] font-mono text-[20px] tabular-nums">{now == null ? "" : formatTime(now)}</span>

        <div className="h-8 w-px bg-border" aria-hidden />

        {me?.role === "Owner" ? (
          <Button variant="ghost" size="sm" asChild>
            <Link href={staffRoutes.owner}>
              <LayoutGridIcon aria-hidden />
              ภาพรวม
            </Link>
          </Button>
        ) : null}
        <div className="flex items-center gap-2">
          <span className="flex size-8 flex-none items-center justify-center rounded-full bg-sunken text-[13px] font-semibold text-ink-2">
            {me?.displayName.charAt(0)}
          </span>
          <span className="text-[13px] font-semibold">{me?.displayName}</span>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="ออกจากระบบ"
            disabled={logout.isPending}
            onClick={() => logout.mutate()}
          >
            <LogOutIcon aria-hidden />
          </Button>
        </div>
      </header>

      <div className="grid min-h-0 flex-1 grid-cols-3 gap-4 px-6 pt-4 pb-6">{children}</div>
    </div>
  );
}

/** Green while the hub is up; the menu screen's offline wording while it is down (it reconnects itself). */
function LiveBadge() {
  const isOffline = useLiveOffline();
  return (
    <span
      role="status"
      className={`inline-flex h-8 items-center gap-2 rounded-full px-3 text-[13px] font-semibold whitespace-nowrap ${
        isOffline ? "bg-sunken text-ink-2" : "bg-ready-soft text-ready-ink"
      }`}
    >
      <i className={`size-[7px] rounded-full ${isOffline ? "bg-served-dot" : "bg-ready-dot"}`} aria-hidden />
      {isOffline ? "ออฟไลน์ · ข้อมูลอาจไม่ใหม่" : "เชื่อมต่อแล้ว"}
    </span>
  );
}

/**
 * "เสียงแจ้งเตือน · เปิด/ปิด". Before the first tap the browser blocks sound, so the pill says
 * "แตะเพื่อเปิด" and the switch shows off; that first tap (anywhere) arms the chime.
 */
function SoundSwitch({ chime }: { chime: Chime }) {
  const on = !chime.muted && chime.armed;
  const state = chime.muted ? "ปิด" : chime.armed ? "เปิด" : "แตะเพื่อเปิด";
  const Icon = on ? Volume2Icon : VolumeOffIcon;

  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      // While unarmed the tap itself arms the chime (a window listener), so it must not also mute.
      onClick={() => {
        if (chime.muted || chime.armed) chime.setMuted(!chime.muted);
      }}
      className="inline-flex h-10 items-center gap-2.5 rounded-full border border-border bg-surface pr-2.5 pl-3.5 text-[13.5px] font-medium whitespace-nowrap text-ink-2 outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <Icon className="size-4" strokeWidth={1.75} aria-hidden />
      <span>
        เสียงแจ้งเตือน · <b className="font-semibold text-ink">{state}</b>
      </span>
      <span
        aria-hidden
        className={`relative h-[26px] w-11 flex-none rounded-full transition-colors motion-reduce:transition-none ${
          on ? "bg-orange-strong" : "bg-control-border"
        }`}
      >
        <span
          className={`absolute top-[3px] size-5 rounded-full bg-on-accent transition-[left] motion-reduce:transition-none ${
            on ? "left-[21px]" : "left-[3px]"
          }`}
        />
      </span>
    </button>
  );
}
