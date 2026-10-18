"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import { bangkokDay, toMs } from "@/lib/format";
import { type KitchenBoard, kitchenBoardQueryKey, useKitchenLiveUpdates } from "@/lib/kitchen";
import { useLiveEvent } from "@/lib/realtime";
import type { OrderDto, OrderStatus } from "@/lib/types";

/**
 * The owner's order queue (ticket 14, frame o08) reads two lists from ticket 11's `GET /api/orders`;
 * there is no owner-only endpoint.
 *
 * - The queue cards are the kitchen board itself — same query key, same live merge — so a round the
 *   owner starts here moves on the kitchen tablets and vice versa, through `useAdvanceOrder` /
 *   `useCancelOrder` (ticket 12's shared workflow).
 * - "เสร็จแล้ววันนี้" is `?status=Ready,Cancelled&date=<Bangkok today>`: every round sent today that
 *   reached its last status, including those of tables that have since paid. Ready is terminal (there
 *   is no served step), so a Ready round shows both as a queue card (until its table pays) and here.
 */
export const DONE_STATUSES = ["Ready", "Cancelled"] as const satisfies readonly OrderStatus[];

export const completedTodayQueryKey = (day: string) => ["orders", "done", day] as const;

const newestFirst = (a: OrderDto, b: OrderDto) => toMs(b.updatedAt) - toMs(a.updatedAt) || b.id - a.id;

/** Today's finished rounds, newest change first. `day` is the Bangkok date of the server clock. */
export function useCompletedToday(day: string) {
  return useQuery({
    queryKey: completedTodayQueryKey(day),
    queryFn: async () => {
      const orders = await apiFetch<OrderDto[]>(`/api/orders?status=${DONE_STATUSES.join(",")}&date=${day}`);
      return orders.sort(newestFirst);
    },
  });
}

/**
 * Keeps both lists in step with the hub while the queue is open:
 * - the cards through the kitchen board's own merge (`orderCreated`/`orderStatusChanged`/`orderEdited`);
 *   `onNewOrder` fires once per round the board had not seen, for the alert highlight;
 * - a round that turns Ready or Cancelled (or a finished round whose lines were edited) is put into
 *   today's list in place, and dropped from it if it ever left those statuses;
 * - `sessionClosed` (the owner group hears it, the kitchen does not) takes that table's Ready cards off
 *   the queue at once instead of at the board's next reconcile.
 */
export function useOrderQueueLiveUpdates(day: string, onNewOrder: (order: OrderDto) => void): void {
  const queryClient = useQueryClient();
  useKitchenLiveUpdates(onNewOrder);

  const putInToday = (order: OrderDto) => {
    if (bangkokDay(order.createdAt) !== day) return;
    const key = completedTodayQueryKey(day);
    const list = queryClient.getQueryData<OrderDto[]>(key);
    if (!list) {
      void queryClient.invalidateQueries({ queryKey: key });
      return;
    }
    const shown = list.find((o) => o.id === order.id);
    if (shown && toMs(shown.updatedAt) > toMs(order.updatedAt)) return;
    const others = list.filter((o) => o.id !== order.id);
    const finished = (DONE_STATUSES as readonly OrderStatus[]).includes(order.status);
    queryClient.setQueryData<OrderDto[]>(key, finished ? [order, ...others].sort(newestFirst) : others);
  };

  useLiveEvent("orderStatusChanged", putInToday);
  useLiveEvent("orderEdited", putInToday);
  useLiveEvent("sessionClosed", ({ sessionId }) => {
    const board = queryClient.getQueryData<KitchenBoard>(kitchenBoardQueryKey);
    if (!board) return;
    queryClient.setQueryData<KitchenBoard>(kitchenBoardQueryKey, {
      ...board,
      orders: board.orders.filter((o) => o.sessionId !== sessionId),
    });
  });
}
