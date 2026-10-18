"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import { useLiveEvent } from "@/lib/realtime";
import type { CustomerMenuDto, CustomerOrdersDto, CustomerSessionDto, MenuItemChangedEvent } from "@/lib/types";

/**
 * Data access for the customer screens behind `/s/{token}` (tickets 09, 10, 13).
 * The token is the identity: no auth cookie, no staff query keys — one key space per token, so two
 * tables open in the same browser never share a cache entry.
 */
export const customerSessionQuery = (token: string) => ["customer", token, "session"] as const;
export const customerMenuQuery = (token: string) => ["customer", token, "menu"] as const;
export const customerOrdersQuery = (token: string) => ["customer", token, "orders"] as const;

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

/**
 * "ออเดอร์ของโต๊ะ" (ticket 13): everything this table has ordered — every phone on the token — collapsed
 * into one list per (menu item, note). No rounds, no status, no bill; cancelled orders and removed lines
 * are already gone server-side. Kept fresh by {@link useCustomerLive}, never by polling.
 */
export function useCustomerOrders(token: string) {
  return useQuery({
    queryKey: customerOrdersQuery(token),
    queryFn: () => apiFetch<CustomerOrdersDto>(`/api/s/${encodeURIComponent(token)}/orders`),
    retry: false,
  });
}

/**
 * Keeps the customer caches in step with the hub. Must be used inside `<LiveProvider token={token}>`,
 * which joins this round's group plus `menu` only (so `orderStatusChanged` never arrives here).
 *
 * - `orderCreated` / `orderEdited` (a friend submitted, the shop edited or cancelled): refetch the table
 *   list — the payload is the staff `OrderDto` with its round and status, which this screen never shows.
 * - `menuItemChanged`: flip the row in place so a sold-out dish greys out at once; a deactivated dish
 *   leaves the menu, and one this cache does not hold (re-activated, or never listed) refetches the menu
 *   so the server stays the judge of which dishes this package may see.
 *
 * Events missed while offline are covered by `useLiveConnection`, which invalidates every query on reconnect.
 */
export function useCustomerLive(token: string) {
  const queryClient = useQueryClient();

  useLiveEvent("orderCreated", () => void queryClient.invalidateQueries({ queryKey: customerOrdersQuery(token) }));
  useLiveEvent("orderEdited", () => void queryClient.invalidateQueries({ queryKey: customerOrdersQuery(token) }));
  // `sessionChanged`: the shop moved this round to another table (ticket 21) — the header shows the new name.
  useLiveEvent("sessionChanged", () => void queryClient.invalidateQueries({ queryKey: customerSessionQuery(token) }));
  useLiveEvent("menuItemChanged", (change) => {
    const menu = queryClient.getQueryData<CustomerMenuDto>(customerMenuQuery(token));
    const next = menu && applyMenuItemChange(menu, change);
    if (next) {
      queryClient.setQueryData(customerMenuQuery(token), next);
    } else if (change.isActive) {
      void queryClient.invalidateQueries({ queryKey: customerMenuQuery(token) });
    }
  });
}

/**
 * The menu after one `menuItemChanged`, or `null` when this menu does not list the item (the caller
 * decides whether that needs a refetch). Categories left empty disappear, matching the server's rule.
 */
export function applyMenuItemChange(menu: CustomerMenuDto, change: MenuItemChangedEvent): CustomerMenuDto | null {
  const listed = menu.categories.some((category) => category.items.some((item) => item.id === change.menuItemId));
  if (!listed) return null;

  return {
    ...menu,
    categories: menu.categories
      .map((category) => ({
        ...category,
        items: change.isActive
          ? category.items.map((item) =>
              item.id === change.menuItemId ? { ...item, isAvailable: change.isAvailable } : item,
            )
          : category.items.filter((item) => item.id !== change.menuItemId),
      }))
      .filter((category) => category.items.length > 0),
  };
}
