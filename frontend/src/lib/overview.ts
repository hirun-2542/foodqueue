"use client";

import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import type { TableOverviewDto } from "@/lib/types";

/**
 * The floor as one request: every active table plus the open round of each, with the numbers the
 * screens display already computed by the server.
 *
 * Ticket 11 replaces the interval with `sessionChanged`/`orderCreated` from the hub; until then a
 * fixed refetch is what makes a table opened on another device show up as ใช้งานอยู่, and what keeps
 * ใช้ไป N นาที moving without the client ever reading its own clock.
 */
const REFRESH_MS = 10_000;

export const tableOverviewQueryKey = ["tables", "overview"] as const;

export function useTableOverview() {
  return useQuery({
    queryKey: tableOverviewQueryKey,
    queryFn: () => apiFetch<TableOverviewDto[]>("/api/tables/overview"),
    refetchInterval: REFRESH_MS,
  });
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
