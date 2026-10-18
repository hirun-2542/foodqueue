"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
import { apiFetchWithServerTime } from "@/lib/api";
import { useServerTime } from "@/lib/cart";
import { minutesUsed } from "@/lib/format";
import { useLiveEvent } from "@/lib/realtime";
import type { TableOverviewDto } from "@/lib/types";

/**
 * The floor as one request: every active table plus the open round of each, with the numbers the
 * screens display already computed by the server. Must be used inside `<LiveProvider>`.
 *
 * No polling (ticket 11): the overview is refetched when the hub says something on the floor
 * changed — `sessionChanged` (a table opened, moved, merged or adjusted), `orderCreated`,
 * `orderStatusChanged`/`orderEdited` (orders waiting and the running total) and `sessionClosed`
 * (paid or voided) — and after every reconnect (`useLiveConnection` invalidates every query).
 * Between refetches ใช้ไป N นาที keeps moving from the stored `openedAt` against the server clock
 * (the response's `Date` header plus the time elapsed since), never against the device's own clock.
 */
export const tableOverviewQueryKey = ["tables", "overview"] as const;

interface OverviewSnapshot {
  cards: TableOverviewDto[];
  serverNow: number;
}

export function useTableOverview() {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: tableOverviewQueryKey,
    queryFn: async (): Promise<OverviewSnapshot> => {
      const { data, serverNow } = await apiFetchWithServerTime<TableOverviewDto[]>("/api/tables/overview");
      return { cards: data, serverNow };
    },
  });

  const refresh = () => void queryClient.invalidateQueries({ queryKey: tableOverviewQueryKey });
  useLiveEvent("sessionChanged", refresh);
  useLiveEvent("sessionClosed", refresh);
  useLiveEvent("orderCreated", refresh);
  useLiveEvent("orderStatusChanged", refresh);
  useLiveEvent("orderEdited", refresh);

  // Before the first answer there are no cards to age, so the placeholder reference is never shown.
  const now = useServerTime(query.data?.serverNow ?? 0);
  const cards = useMemo(
    () =>
      query.data?.cards.map((card) =>
        card.openedAt == null || card.minutesUsed == null
          ? card
          : { ...card, minutesUsed: minutesUsed(card.openedAt, now) },
      ),
    [query.data, now],
  );

  return { ...query, data: cards };
}

/**
 * A table that holds guests. The server omits every session field for a free table (`null` is dropped
 * from the JSON), so this is exactly the set of present fields, not a copy of the wire shape.
 */
export interface OccupiedTable extends TableOverviewDto {
  sessionId: number;
  packageName: string;
  adults: number;
  children: number;
  openedAt: string;
  endsAt: string;
  minutesUsed: number;
  ordersWaiting: number;
  runningTotal: number;
}

/** The session facts of an occupied table, or `null` when the table is free. */
export function occupied(card: TableOverviewDto): OccupiedTable | null {
  return card.sessionId != null &&
    card.packageName != null &&
    card.adults != null &&
    card.children != null &&
    card.openedAt != null &&
    card.endsAt != null &&
    card.minutesUsed != null &&
    card.ordersWaiting != null &&
    card.runningTotal != null
    ? (card as OccupiedTable)
    : null;
}
