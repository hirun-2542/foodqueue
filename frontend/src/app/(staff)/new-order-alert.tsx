"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRightIcon,
  BellRingIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  PencilIcon,
  Volume2Icon,
  VolumeOffIcon,
} from "lucide-react";
import { usePathname, useRouter } from "next/navigation";
import { type ReactNode, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { apiFetch } from "@/lib/api";
import { staffRoutes } from "@/lib/auth";
import { formatBahtWhole, formatTime } from "@/lib/format";
import { occupied, useTableOverview } from "@/lib/overview";
import { LiveProvider, useLiveEvent } from "@/lib/realtime";
import type { OrderDto } from "@/lib/types";
import { type Chime, useChime } from "./kitchen/use-chime";

/** The owner's popup mutes on its own; muting it leaves the kitchen board's chime on this device alone. */
const OWNER_MUTED_KEY = "fq-owner-new-order-muted";

const pendingOrdersQueryKey = ["orders", "pending"] as const;

/**
 * Wraps every owner screen of the staff layout (ticket 15). For an Owner it opens the one live
 * connection that the screens' own `<LiveProvider>`s then reuse, and mounts the new-order popup;
 * for anyone else it renders the screen untouched, so a Kitchen account never sees the popup.
 */
export function NewOrderAlertScope({ enabled, children }: { enabled: boolean; children: ReactNode }) {
  if (!enabled) return children;
  return (
    <LiveProvider>
      <NewOrderAlert />
      {children}
    </LiveProvider>
  );
}

/**
 * Frames o20 (collapsed) / o21 (lines expanded): every `orderCreated` the owner group hears opens a
 * centred dialog for that round and chimes once. A round arriving while it is open replaces it (and
 * chimes again); the rounds still waiting are counted under the buttons.
 *
 * Sound follows the browser's autoplay rule through `useChime`: nothing can play until this page has
 * had a tap or key press (or was reached by in-app navigation after one, e.g. from the login form).
 * Until then the dialog says the browser is holding the sound back instead of claiming it plays; a
 * round that arrived before that first interaction is not replayed afterwards.
 *
 * "ไปหน้าออเดอร์" only navigates to `/owner/orders`; starting the round happens there, never here.
 */
function NewOrderAlert() {
  const chime = useChime(OWNER_MUTED_KEY);
  const queryClient = useQueryClient();
  const [order, setOrder] = useState<OrderDto | null>(null);

  useLiveEvent("orderCreated", (created) => {
    setOrder(created);
    chime.play();
    void queryClient.invalidateQueries({ queryKey: pendingOrdersQueryKey });
  });

  // The round on screen can be moved or cancelled elsewhere (the kitchen board, another owner
  // tablet) while the popup is up: a round that reached Ready or was cancelled is no longer new, so
  // the popup closes instead of claiming it is; edits to its lines replace what is shown.
  useLiveEvent("orderStatusChanged", (changed) =>
    setOrder((shown) =>
      shown?.id === changed.id && changed.status !== "Pending" && changed.status !== "Preparing" ? null : shown,
    ),
  );
  useLiveEvent("orderEdited", (edited) =>
    setOrder((shown) =>
      shown?.id === edited.id
        ? edited.status === "Pending" || edited.status === "Preparing"
          ? edited
          : null
        : shown,
    ),
  );

  return (
    <Dialog open={order !== null} onOpenChange={(open) => (open ? undefined : setOrder(null))}>
      {order ? <NewOrderDialog key={order.id} order={order} chime={chime} onClose={() => setOrder(null)} /> : null}
    </Dialog>
  );
}

function NewOrderDialog({ order, chime, onClose }: { order: OrderDto; chime: Chime; onClose: () => void }) {
  const router = useRouter();
  const pathname = usePathname();
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const goRef = useRef<HTMLButtonElement>(null);

  const overview = useTableOverview();
  const table = overview.data?.map(occupied).find((card) => card?.sessionId === order.sessionId) ?? null;

  const pending = useQuery({
    queryKey: pendingOrdersQueryKey,
    queryFn: () => apiFetch<OrderDto[]>("/api/orders?status=Pending"),
  });
  const refreshPending = () => void queryClient.invalidateQueries({ queryKey: pendingOrdersQueryKey });
  useLiveEvent("orderStatusChanged", refreshPending);
  useLiveEvent("orderEdited", refreshPending);
  useLiveEvent("sessionClosed", refreshPending);

  const others = (pending.data ?? []).filter((waiting) => waiting.id !== order.id);
  const otherTables = [...new Set(others.map((waiting) => waiting.tableName))];

  const pieces = order.items.reduce((sum, item) => sum + item.quantity, 0);
  const facts = [
    table?.packageName,
    table ? `${table.adults} ผู้ใหญ่${table.children > 0 ? ` ${table.children} เด็ก` : ""}` : null,
    `${pieces} ชิ้น`,
  ]
    .filter(Boolean)
    .join(" · ");

  const goToOrders = () => {
    onClose();
    if (!pathname.startsWith(staffRoutes.orders)) router.push(staffRoutes.orders);
  };

  return (
    <DialogContent
      showCloseButton={false}
      onOpenAutoFocus={(event) => {
        event.preventDefault();
        goRef.current?.focus();
      }}
      className="flex max-h-[calc(100dvh-2rem)] flex-col items-center gap-0 overflow-y-auto rounded-xl bg-surface p-7 text-center shadow-float sm:max-w-[520px]"
    >
      {/* Still bell: the popup itself is the alert, nothing on it blinks or pulses. */}
      <span className="flex size-16 flex-none items-center justify-center rounded-full bg-orange-soft text-orange-ink">
        <BellRingIcon className="size-8" strokeWidth={1.5} aria-hidden />
      </span>
      <p className="mt-5 text-[14px] font-semibold text-orange-ink">มีออเดอร์ใหม่</p>
      <DialogTitle className="mt-1.5 text-[40px] leading-[1.2] font-semibold tracking-[-0.02em]">
        โต๊ะ {order.tableName}
      </DialogTitle>
      <DialogDescription className="text-body text-ink-2">
        รอบที่ {order.number} · ส่ง <span className="font-mono">{formatTime(order.createdAt)}</span>
      </DialogDescription>
      <p className="mt-1 text-body text-ink-2">{facts}</p>

      <SoundRow chime={chime} />

      <Button
        type="button"
        variant="secondary"
        className="mt-4 w-full"
        aria-expanded={expanded}
        aria-controls={`new-order-${order.id}-items`}
        onClick={() => setExpanded((open) => !open)}
      >
        {expanded ? "ซ่อนรายการ" : "ดูว่าลูกค้าสั่งอะไร"}
        {expanded ? (
          <ChevronUpIcon className="size-4" aria-hidden />
        ) : (
          <ChevronDownIcon className="size-4" aria-hidden />
        )}
      </Button>

      {expanded ? (
        <ul id={`new-order-${order.id}-items`} className="mt-2 w-full divide-y divide-border">
          {order.items.map((item, index) => (
            <li key={index} className="flex items-start gap-3 py-3 text-left">
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="text-[15px] leading-[1.5] font-semibold">{item.name}</span>
                {item.kind === "AlaCarte" ? (
                  <span className="text-xs leading-[1.5] text-muted-foreground">
                    {formatBahtWhole(item.unitPrice)} คิดเงินแยก
                  </span>
                ) : null}
                {item.note ? (
                  <span className="mt-1.5 flex items-center gap-1.5 rounded-sm bg-pending-soft px-2.5 py-[3px] text-[13px] font-medium text-pending-ink">
                    <PencilIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
                    {item.note}
                  </span>
                ) : null}
              </span>
              <span className="ms-auto flex-none text-[15px] font-semibold tabular-nums">x{item.quantity}</span>
            </li>
          ))}
        </ul>
      ) : null}

      <div className={`${expanded ? "mt-1" : "mt-3"} grid w-full grid-cols-2 gap-3`}>
        <Button type="button" variant="ghost" onClick={onClose}>
          ไว้ก่อน
        </Button>
        <Button ref={goRef} type="button" onClick={goToOrders}>
          ไปหน้าออเดอร์
          <ArrowRightIcon className="size-4" aria-hidden />
        </Button>
      </div>

      {others.length > 0 ? (
        <p className="mt-3.5 inline-flex items-center gap-2 text-[13px] leading-[1.5] text-muted-foreground">
          <i className="size-1.5 flex-none rounded-full bg-orange" aria-hidden />
          ยังมีออเดอร์รอรับอีก {others.length} รอบ · {otherTables.join(", ")}
        </p>
      ) : null}
    </DialogContent>
  );
}

/**
 * The sound indicator with its mute control. Three honest states: muted; not yet allowed by the
 * browser (no interaction with this page yet); or on — the chime played when this round arrived.
 */
function SoundRow({ chime }: { chime: Chime }) {
  const audible = !chime.muted && chime.armed;
  const Icon = audible ? Volume2Icon : VolumeOffIcon;
  const status = chime.muted
    ? "ปิดเสียงแจ้งเตือนอยู่"
    : chime.armed
      ? "กำลังเล่นเสียงแจ้งเตือน"
      : "เบราว์เซอร์ยังไม่ให้เล่นเสียง · แตะที่ใดก็ได้บนหน้าจอเพื่อเปิด";

  return (
    <div className="mt-5 flex w-full items-center gap-2.5 rounded-lg bg-sunken px-3.5 py-3 text-left">
      <Icon className="size-4 flex-none text-ink-2" strokeWidth={1.75} aria-hidden />
      <span className="flex-1 text-[13px] leading-[1.5] text-ink-2" aria-live="polite">
        {status}
      </span>
      <Button type="button" variant="ghost" size="sm" onClick={() => chime.setMuted(!chime.muted)}>
        {chime.muted ? "เปิดเสียง" : "ปิดเสียง"}
      </Button>
    </div>
  );
}
