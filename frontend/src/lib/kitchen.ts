"use client";

import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError, apiFetch, apiFetchWithServerTime } from "@/lib/api";
import { toMs } from "@/lib/format";
import { useLiveEvent } from "@/lib/realtime";
import type { OrderDto, OrderStatus } from "@/lib/types";

/**
 * The kitchen board (ticket 11): every round of an open table that is Pending, Preparing or Ready,
 * oldest first, plus the server's clock at the moment it answered.
 *
 * - New rounds arrive as `orderCreated` and are merged into the cache immediately; status changes
 *   (`orderStatusChanged`) and line edits or cancellations (`orderEdited`) replace or drop the card in place.
 * - After a hub reconnect `useLiveConnection` invalidates every query, so the board refetches.
 * - Ready is terminal: a Ready card leaves the board only when its table pays or is voided, and the
 *   kitchen role is not in the groups that hear `sessionClosed`. A one-minute refetch reconciles that
 *   (and re-anchors the server clock); it is not how new orders arrive.
 * - The kitchen moves a round with `useAdvanceOrder` (เริ่มทำ / พร้อมเสิร์ฟ) and `useCancelOrder` (ticket 12).
 */
export const kitchenBoardQueryKey = ["orders", "board"] as const;

export const BOARD_STATUSES = ["Pending", "Preparing", "Ready"] as const satisfies readonly OrderStatus[];
export type BoardStatus = (typeof BOARD_STATUSES)[number];

/** Past this many whole minutes a card's wait turns red (story 36). */
export const LATE_AFTER_MINUTES = 10;

const RECONCILE_MS = 60_000;

export interface KitchenBoard {
  orders: OrderDto[];
  /** Server time (epoch ms) when the list was answered — the reference for every elapsed minute. */
  serverNow: number;
}

export function useKitchenBoard() {
  return useQuery({
    queryKey: kitchenBoardQueryKey,
    queryFn: async (): Promise<KitchenBoard> => {
      const { data, serverNow } = await apiFetchWithServerTime<OrderDto[]>("/api/orders");
      return { orders: data, serverNow };
    },
    refetchInterval: RECONCILE_MS,
  });
}

/**
 * The board with `order` inserted or replaced (or removed once it left the board), oldest first.
 * An answer older than the card already shown (a tap's response arriving after another tablet's
 * later change came over the hub) is ignored, so the card never steps backwards.
 */
function merge(board: KitchenBoard, order: OrderDto): KitchenBoard {
  const shown = board.orders.find((o) => o.id === order.id);
  if (shown && toMs(shown.updatedAt) > toMs(order.updatedAt)) return board;
  const others = board.orders.filter((o) => o.id !== order.id);
  const stays = (BOARD_STATUSES as readonly OrderStatus[]).includes(order.status);
  const orders = stays ? [...others, order] : others;
  orders.sort((a, b) => toMs(a.createdAt) - toMs(b.createdAt) || a.id - b.id);
  return { ...board, orders };
}

/**
 * Keeps the cached board in step with the hub. `onNewOrder` fires once per round the board had not
 * seen yet — the screen uses it for the chime and the highlight.
 */
export function useKitchenLiveUpdates(onNewOrder: (order: OrderDto) => void): void {
  const queryClient = useQueryClient();

  const apply = (order: OrderDto): boolean => {
    const board = queryClient.getQueryData<KitchenBoard>(kitchenBoardQueryKey);
    if (!board) {
      // Not loaded yet: the pending request will contain this round.
      void queryClient.invalidateQueries({ queryKey: kitchenBoardQueryKey });
      return true;
    }
    const seen = board.orders.some((o) => o.id === order.id);
    queryClient.setQueryData<KitchenBoard>(kitchenBoardQueryKey, merge(board, order));
    return !seen;
  };

  useLiveEvent("orderCreated", (order) => {
    if (apply(order)) onNewOrder(order);
  });
  useLiveEvent("orderStatusChanged", (order) => void apply(order));
  useLiveEvent("orderEdited", (order) => void apply(order));
}

// ------------------------------------------------------------------ เลื่อนสถานะ / ยกเลิก (ticket 12)

/** The one step each kitchen button takes. Ready has none: it is the last status, nothing marks a round served. */
export const NEXT_STATUS = {
  Pending: "Preparing",
  Preparing: "Ready",
} as const satisfies Partial<Record<BoardStatus, OrderStatus>>;

export type AdvanceTarget = (typeof NEXT_STATUS)[keyof typeof NEXT_STATUS];

/** The server's limit on a cancel reason (Order.CancelReason, 1–200 characters). */
export const CANCEL_REASON_MAX = 200;

/** The shortcut chips of the cancel dialog (frame k2); a tap starts the reason with "<chip>: ". */
export const CANCEL_REASON_CHIPS = ["วัตถุดิบหมด", "ลูกค้าแจ้งเปลี่ยน", "สั่งผิด/ซ้ำ"] as const;

/**
 * The round changed on another device (or vanished) since this screen drew it: the server refused
 * with `INVALID_STATUS_TRANSITION`, `SESSION_CLOSED` (its table paid) or 404. The board refetches and
 * the action that was aimed at the old state is dropped.
 */
export function isStaleOrderError(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    (error.code === "INVALID_STATUS_TRANSITION" || error.code === "SESSION_CLOSED" || error.status === 404)
  );
}

/** Puts the server's answer on the board right away; the hub event that follows changes nothing. */
function putOnBoard(queryClient: QueryClient, order: OrderDto) {
  const board = queryClient.getQueryData<KitchenBoard>(kitchenBoardQueryKey);
  if (board) {
    queryClient.setQueryData<KitchenBoard>(kitchenBoardQueryKey, merge(board, order));
  } else {
    void queryClient.invalidateQueries({ queryKey: kitchenBoardQueryKey });
  }
}

function reportFailure(queryClient: QueryClient, error: unknown, order: OrderDto, fallback: string) {
  if (isStaleOrderError(error)) {
    void queryClient.invalidateQueries({ queryKey: kitchenBoardQueryKey });
    const why =
      error instanceof ApiError && error.code === "SESSION_CLOSED"
        ? "โต๊ะนี้ปิดบิลแล้ว"
        : "ออเดอร์นี้ถูกอัปเดตจากเครื่องอื่นแล้ว";
    toast.warning(`โต๊ะ ${order.tableName} รอบที่ ${order.number}: ${why} · แสดงสถานะล่าสุดให้แล้ว`);
    return;
  }
  toast.error(error instanceof ApiError ? error.message : fallback);
}

/**
 * เริ่มทำ (Pending → Preparing) / พร้อมเสิร์ฟ (Preparing → Ready). The request names the target, not
 * "the next step": when two tablets tap เริ่มทำ together the second is refused instead of skipping to Ready.
 */
export function useAdvanceOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ order, to }: { order: OrderDto; to: AdvanceTarget }) =>
      apiFetch<OrderDto>(`/api/orders/${order.id}/status`, {
        method: "POST",
        body: JSON.stringify({ status: to }),
      }),
    onSuccess: (saved) => putOnBoard(queryClient, saved),
    onError: (error, { order }) => reportFailure(queryClient, error, order, "เปลี่ยนสถานะไม่สำเร็จ ลองอีกครั้ง"),
  });
}

/** Cancels a Pending/Preparing/Ready round with a reason the table's customers will see. */
export function useCancelOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ order, reason }: { order: OrderDto; reason: string }) =>
      apiFetch<OrderDto>(`/api/orders/${order.id}/cancel`, {
        method: "POST",
        body: JSON.stringify({ reason }),
      }),
    onSuccess: (saved) => {
      putOnBoard(queryClient, saved);
      toast.success(`ยกเลิกออเดอร์ ${saved.tableName} รอบที่ ${saved.number} แล้ว`);
    },
    onError: (error, { order }) => reportFailure(queryClient, error, order, "ยกเลิกออเดอร์ไม่สำเร็จ ลองอีกครั้ง"),
  });
}

