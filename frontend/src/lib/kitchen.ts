"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetchWithServerTime } from "@/lib/api";
import { toMs } from "@/lib/format";
import { useLiveEvent } from "@/lib/realtime";
import type { OrderDto, OrderStatus } from "@/lib/types";

/**
 * The kitchen board (ticket 11): every round of an open table that is Pending, Preparing or Ready,
 * oldest first, plus the server's clock at the moment it answered.
 *
 * - New rounds arrive as `orderCreated` and are merged into the cache immediately; status changes
 *   (`orderStatusChanged`, ticket 12) and line edits (`orderEdited`) replace the card in place.
 * - After a hub reconnect `useLiveConnection` invalidates every query, so the board refetches.
 * - Ready is terminal: a Ready card leaves the board only when its table pays or is voided, and the
 *   kitchen role is not in the groups that hear `sessionClosed`. A one-minute refetch reconciles that
 *   (and re-anchors the server clock); it is not how new orders arrive.
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

/** The board with `order` inserted or replaced (or removed once it left the board), oldest first. */
function merge(board: KitchenBoard, order: OrderDto): KitchenBoard {
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

