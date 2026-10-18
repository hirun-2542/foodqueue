"use client";

import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError, apiFetch, apiFetchWithServerTime } from "@/lib/api";
import { tableOverviewQueryKey } from "@/lib/overview";
import { useLiveEvent } from "@/lib/realtime";
import type { PaymentInput, SessionDetailDto } from "@/lib/types";

/** Body of `POST /api/sessions` — counts are the wire names the API freezes. */
export interface OpenSessionInput {
  tableId: number;
  packageId: number;
  adultCount: number;
  childCount: number;
}

/**
 * Opens one table. The server answers with the full round — including the `qrUrl` that the screen
 * renders — and a refusal keeps its Thai message (`TABLE_OCCUPIED`, `VALIDATION_FAILED`), which the
 * screen shows as-is.
 *
 * "Which table is taken" is not a separate query: `GET /api/tables/overview` (ticket 08) is the one
 * source, so opening a round invalidates that key and the grid redraws from the server's answer.
 */
export function useOpenSession() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: OpenSessionInput) =>
      apiFetch<SessionDetailDto>("/api/sessions", { method: "POST", body: JSON.stringify(input) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tableOverviewQueryKey }),
  });
}

// ------------------------------------------------------------------ รายละเอียด session (ticket 16)

export const sessionDetailQueryKey = (sessionId: number) => ["sessions", sessionId] as const;

interface SessionSnapshot {
  session: SessionDetailDto;
  /** The server's clock when it answered (epoch ms), so ใช้ไป N นาที never trusts this device's clock. */
  serverNow: number;
}

/**
 * One round as the owner sees it (`GET /api/sessions/{id}`): its orders, adjustments and the bill the
 * server's single calculator produced. Must be used inside `<LiveProvider>`.
 *
 * No polling: it refetches when the hub says something on this bill changed — `sessionChanged` /
 * `sessionClosed` for this round, and every order event (a round merged into this bill sends its
 * orders under its own session id, so they are not filtered) — and after every reconnect
 * (`useLiveConnection` invalidates every query).
 */
export function useSessionDetail(sessionId: number) {
  const queryClient = useQueryClient();
  const queryKey = sessionDetailQueryKey(sessionId);
  const query = useQuery({
    queryKey,
    queryFn: async (): Promise<SessionSnapshot> => {
      const { data, serverNow } = await apiFetchWithServerTime<SessionDetailDto>(`/api/sessions/${sessionId}`);
      return { session: data, serverNow };
    },
    enabled: Number.isInteger(sessionId) && sessionId > 0,
  });

  const refresh = () => void queryClient.invalidateQueries({ queryKey });
  useLiveEvent("sessionChanged", (summary) => (summary.id === sessionId ? refresh() : undefined));
  useLiveEvent("sessionClosed", (event) => (event.sessionId === sessionId ? refresh() : undefined));
  useLiveEvent("orderCreated", refresh);
  useLiveEvent("orderStatusChanged", refresh);
  useLiveEvent("orderEdited", refresh);

  return query;
}

/** The answer of every write on a round is the whole round: put it straight into the cache. */
function writeSession(queryClient: QueryClient, session: SessionDetailDto) {
  queryClient.setQueryData<SessionSnapshot>(sessionDetailQueryKey(session.id), (previous) =>
    previous ? { ...previous, session } : previous,
  );
  // The overview card shows the running total, which an adjustment changes.
  void queryClient.invalidateQueries({ queryKey: tableOverviewQueryKey });
}

/**
 * The bill changed under this screen (paid or voided elsewhere, or the adjustment was already
 * removed on another device): show the latest state instead of keeping the stale one.
 */
function reportFailure(queryClient: QueryClient, sessionId: number, error: unknown, fallback: string) {
  if (error instanceof ApiError && (error.code === "SESSION_CLOSED" || error.status === 404)) {
    void queryClient.invalidateQueries({ queryKey: sessionDetailQueryKey(sessionId) });
  }
  toast.error(error instanceof ApiError ? error.message : fallback);
}

/** Body of `POST /api/sessions/{id}/adjustments`: a positive amount adds, a negative one discounts. */
export interface AdjustmentInput {
  description: string;
  amount: number;
}

export function useAddAdjustment(sessionId: number) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: AdjustmentInput) =>
      apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}/adjustments`, {
        method: "POST",
        body: JSON.stringify(input),
      }),
    onSuccess: (session) => writeSession(queryClient, session),
    onError: (error) => reportFailure(queryClient, sessionId, error, "ปรับยอดไม่สำเร็จ ลองอีกครั้ง"),
  });
}

export function useRemoveAdjustment(sessionId: number) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (adjustmentId: number) =>
      apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}/adjustments/${adjustmentId}`, { method: "DELETE" }),
    onSuccess: (session) => writeSession(queryClient, session),
    onError: (error) => reportFailure(queryClient, sessionId, error, "ลบรายการปรับยอดไม่สำเร็จ ลองอีกครั้ง"),
  });
}

/**
 * รับชำระเงิน (`POST /api/sessions/{id}/payment`, ticket 18): closes the bill and answers with the whole round
 * including `payment` (method, amount received, change, receipt number). The server owns every rule — Open
 * round, no Pending/Preparing order, Cash ≥ total, one receipt per round — so a refusal arrives as an
 * `ApiError` with the rule's `code` and Thai message; `ORDERS_IN_PROGRESS` also names the blocking rounds.
 * A success is put into the cache, and the hub's `sessionClosed` refreshes every other device.
 */
export function usePaySession(sessionId: number) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: PaymentInput) =>
      apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}/payment`, {
        method: "POST",
        body: JSON.stringify(input),
      }),
    onSuccess: (session) => writeSession(queryClient, session),
  });
}

/** ยกเลิกโต๊ะ (`POST /api/sessions/{id}/void`, ticket 07's rule): only an Open round without live orders. */
export function useVoidSession(sessionId: number) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}/void`, { method: "POST" }),
    onSuccess: (session) => {
      writeSession(queryClient, session);
      toast.success(`ยกเลิกโต๊ะ ${session.tableName} แล้ว`);
    },
    onError: (error) => reportFailure(queryClient, sessionId, error, "ยกเลิกโต๊ะไม่สำเร็จ ลองอีกครั้ง"),
  });
}

// ------------------------------------------------------------------ ย้ายโต๊ะ (ticket 21)

/**
 * ย้ายโต๊ะ (`POST /api/sessions/{id}/move`): the whole round — orders, bill, adjustments, times, token/QR —
 * goes to a free active table and the old one is free at once. The answer is the round at its new table.
 *
 * A refusal keeps the server's Thai message. `TABLE_OCCUPIED` means the destination was taken a moment
 * ago (another device opened or moved a round onto it): the floor refetches so the picker greys it out.
 * The kitchen cards take the new table name from the hub (`orderStatusChanged` re-sends them).
 */
export function useMoveSession(sessionId: number) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (tableId: number) =>
      apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}/move`, {
        method: "POST",
        body: JSON.stringify({ tableId }),
      }),
    onSuccess: (session) => {
      writeSession(queryClient, session);
      toast.success(`ย้ายไปโต๊ะ ${session.tableName} แล้ว`);
    },
    onError: (error) => {
      if (error instanceof ApiError && error.code === "TABLE_OCCUPIED") {
        void queryClient.invalidateQueries({ queryKey: tableOverviewQueryKey });
      }
      reportFailure(queryClient, sessionId, error, "ย้ายโต๊ะไม่สำเร็จ ลองอีกครั้ง");
    },
  });
}
