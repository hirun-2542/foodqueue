"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetchWithServerTime } from "@/lib/api";
import { useLiveEvent } from "@/lib/realtime";
import type { ReportRange, ReportSummaryDto } from "@/lib/types";

/**
 * `GET /api/reports/summary` — the one source of the sales figures. `/owner/reports` and the KPI band
 * on `/owner` both read through this hook, so the two screens cannot show different numbers for today.
 * Must be used inside `<LiveProvider>`.
 *
 * A bill only enters the report when it is paid, and paying closes the round, so `sessionClosed` is the
 * event that refreshes it (a voided round also closes; the refetch then simply answers the same
 * figures). `useLiveConnection` invalidates every query after a reconnect. No polling.
 */
export const reportSummaryQueryKey = (range: ReportRange) => ["reports", "summary", range] as const;

export interface ReportSnapshot {
  summary: ReportSummaryDto;
  /** The server's clock when it answered: the report's "today", never the device's own date. */
  serverNow: number;
}

export function useReportSummary(range: ReportRange) {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: reportSummaryQueryKey(range),
    queryFn: async (): Promise<ReportSnapshot> => {
      const { data, serverNow } = await apiFetchWithServerTime<ReportSummaryDto>(
        `/api/reports/summary?range=${range}`,
      );
      return { summary: data, serverNow };
    },
  });

  useLiveEvent("sessionClosed", () => void queryClient.invalidateQueries({ queryKey: ["reports", "summary"] }));

  return query;
}

/** Thai label of each payment method as the frames write it. */
export const paymentMethodLabel = {
  PromptPay: "PromptPay",
  Cash: "เงินสด",
  Card: "บัตร",
} as const;

/** Thai label of each range: the switcher and the "ยอดขาย 7 วัน" headings. */
export const reportRangeLabel: Record<ReportRange, string> = {
  today: "วันนี้",
  "7d": "7 วัน",
  "30d": "30 วัน",
};
