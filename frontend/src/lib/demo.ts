"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { ApiError, apiFetch } from "@/lib/api";
import type { DemoCustomerSessionDto, DemoRole, DemoStatusDto } from "@/lib/types";

/**
 * Data access for the demo card on `/login` (ticket 24). Demo Mode is one server switch (`Demo:Enabled`):
 * when it is off every demo endpoint answers **404**, and that 404 is exactly what the card reads as
 * "there is no demo here", so the browser never needs a second source of truth.
 *
 * Used on the login screen, which lives outside the staff shell and therefore outside `<LiveProvider>`
 * — nothing here subscribes to the hub, and nothing here polls.
 */
export const demoStatusQueryKey = ["demo", "status"] as const;

export interface DemoStatus {
  enabled: boolean;
}

/** `GET /api/demo/status` — no auth, so the login screen can ask before anyone is signed in. */
export function useDemoStatus() {
  return useQuery({
    queryKey: demoStatusQueryKey,
    queryFn: async (): Promise<DemoStatus> => {
      try {
        return await apiFetch<DemoStatusDto>("/api/demo/status");
      } catch (err) {
        // 404 is the switch being off, not a failure: the card stays hidden instead of showing an error.
        if (err instanceof ApiError && err.status === 404) return { enabled: false };
        throw err;
      }
    },
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** `POST /api/demo/login` — signs in as the demo account of that role; no password is sent. */
export function useDemoLogin() {
  return useMutation({
    mutationFn: (role: DemoRole) =>
      apiFetch<void>("/api/demo/login", { method: "POST", body: JSON.stringify({ role }) }),
  });
}

/**
 * `POST /api/demo/customer-session` — the token of a bot round (opening one on a free table if needed).
 * A refused table surfaces as `TABLE_OCCUPIED` with its Thai message, shown by the caller as-is.
 */
export function useDemoCustomerSession() {
  return useMutation({
    mutationFn: () =>
      apiFetch<DemoCustomerSessionDto>("/api/demo/customer-session", { method: "POST" }),
  });
}
