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

export const demoSplitScreenQueryKey = ["demo", "split-screen"] as const;

/**
 * The two steps `/demo` (ticket 26) must take before it can draw anything, in this order: sign in as the
 * demo **Owner** (`POST /api/demo/login` — an Owner reaches the kitchen board too, so one sign-in covers
 * both staff panes) and then ask for the customer round (`POST /api/demo/customer-session`) whose token
 * the left pane opens.
 *
 * They are one query rather than two mutations because the order is the contract, not a user action:
 * a query runs them once per page load, keeps the pair in the cache (a remount or a tab refocus does not
 * sign in again), and hands the screen both a token and the failure to show. Every failure keeps its
 * server Thai message, so `TABLE_OCCUPIED` reads exactly as the login card shows it.
 */
export function useDemoSplitScreen() {
  return useQuery({
    queryKey: demoSplitScreenQueryKey,
    queryFn: async (): Promise<DemoCustomerSessionDto> => {
      await apiFetch<void>("/api/demo/login", { method: "POST", body: JSON.stringify({ role: "Owner" }) });
      return apiFetch<DemoCustomerSessionDto>("/api/demo/customer-session", { method: "POST" });
    },
    staleTime: Infinity,
    refetchOnWindowFocus: false,
    retry: false,
  });
}
