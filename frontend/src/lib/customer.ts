"use client";

import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import type { CustomerMenuDto, CustomerSessionDto } from "@/lib/types";

/**
 * Data access for the customer screens behind `/s/{token}` (tickets 09, 10, 13).
 * The token is the identity: no auth cookie, no staff query keys — one key space per token, so two
 * tables open in the same browser never share a cache entry.
 */
export const customerSessionQuery = (token: string) => ["customer", token, "session"] as const;
export const customerMenuQuery = (token: string) => ["customer", token, "menu"] as const;

/**
 * The round this QR belongs to: table, package, guest counts, the shop's limits and `serverNow`
 * (the reference clock every time decision on this screen is made against). `SESSION_NOT_FOUND`
 * means the QR does not exist, `SESSION_CLOSED` that the round is over — both surface as `ApiError`.
 */
export function useCustomerSession(token: string) {
  return useQuery({
    queryKey: customerSessionQuery(token),
    queryFn: () => apiFetch<CustomerSessionDto>(`/api/s/${encodeURIComponent(token)}`),
    // A round's package, guests and end time never change once it is open; the shop's banner is the
    // only thing that can, and a customer reloading the page gets that anyway.
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** The menu this package may order: buffet items up to the session's rank plus every active à la carte item. */
export function useCustomerMenu(token: string) {
  return useQuery({
    queryKey: customerMenuQuery(token),
    queryFn: () => apiFetch<CustomerMenuDto>(`/api/s/${encodeURIComponent(token)}/menu`),
    staleTime: 5 * 60_000,
    retry: false,
  });
}
