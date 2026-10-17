"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import type { SessionDetailDto, SessionSummaryDto } from "@/lib/types";

/** Every round that is still open — the source of "which table is taken" until ticket 08's overview exists. */
const openSessionsQueryKey = ["sessions", "Open"] as const;

export function useOpenSessions() {
  return useQuery({
    queryKey: openSessionsQueryKey,
    queryFn: () => apiFetch<SessionSummaryDto[]>("/api/sessions?status=Open"),
  });
}

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
 * screen shows as-is. Only after success does the open-rounds list refresh.
 */
export function useOpenSession() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: OpenSessionInput) =>
      apiFetch<SessionDetailDto>("/api/sessions", { method: "POST", body: JSON.stringify(input) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: openSessionsQueryKey }),
  });
}
