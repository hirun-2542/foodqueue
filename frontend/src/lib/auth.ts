"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { ApiError, apiFetch } from "@/lib/api";
import type { MeDto, StaffRole } from "@/lib/types";

export const meQueryKey = ["auth", "me"] as const;

/** Every staff route that exists today, in one place so the guard and the login form agree. */
export const staffRoutes = {
  owner: "/owner",
  kitchen: "/kitchen",
  packages: "/owner/packages",
  settings: "/owner/settings",
} as const;

/** Home screen of each staff role — the one place this mapping is written down. */
export function staffHome(role: StaffRole): string {
  return role === "Kitchen" ? staffRoutes.kitchen : staffRoutes.owner;
}

/**
 * The signed-in staff member, or an `ApiError` with status 401 when nobody is signed in.
 * `retry: false` on purpose: the guard redirects on the first answer instead of after a backoff.
 */
export function useMe() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: () => apiFetch<MeDto>("/api/auth/me"),
    retry: false,
    staleTime: 30_000,
  });
}

/** True when the answer means "not signed in" (as opposed to the API being unreachable). */
export function isUnauthenticated(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 401 || error.status === 403);
}

/**
 * Signs out and returns to `/login`. `onSettled` on purpose: the session is cleared even if the
 * request failed, so the user is never left staring at a screen they may no longer be entitled to.
 */
export function useLogout() {
  const router = useRouter();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => apiFetch<void>("/api/auth/logout", { method: "POST" }),
    onSettled: () => {
      queryClient.clear();
      router.replace("/login");
    },
  });
}
