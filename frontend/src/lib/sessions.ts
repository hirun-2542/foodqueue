"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import { tableOverviewQueryKey } from "@/lib/overview";
import type { SessionDetailDto } from "@/lib/types";

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
