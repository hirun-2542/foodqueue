"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import type { TableDto } from "@/lib/types";

export const tablesQueryKey = ["tables"] as const;

/** Every table of the shop, active and disabled, in floor order. */
export function useTables() {
  return useQuery({
    queryKey: tablesQueryKey,
    queryFn: () => apiFetch<TableDto[]>("/api/tables"),
  });
}

/**
 * The fields the owner edits. Creating a table leaves `sortOrder` to the server (append to the floor)
 * and starts active; updating always sends `isActive` explicitly so a disable sticks.
 */
export interface TableInput {
  name: string;
  seats: number;
  zone: string | null;
}

async function createTable(input: TableInput): Promise<TableDto> {
  return apiFetch<TableDto>("/api/tables", { method: "POST", body: JSON.stringify(input) });
}

/**
 * Update one table. The PUT carries the whole record, so the caller passes the values it wants to keep.
 * Disabling a table is this same call with `isActive:false` — there is no DELETE route by design.
 */
async function updateTable(id: number, input: TableInput & { isActive: boolean }): Promise<TableDto> {
  return apiFetch<TableDto>(`/api/tables/${id}`, { method: "PUT", body: JSON.stringify(input) });
}

export interface SaveFloorPlan {
  create: TableInput[];
  update: (TableInput & { id: number; isActive: boolean })[];
}

/**
 * Applies the whole floor plan one request at a time, in the order the owner sees it. Saving one row
 * per call (rather than a bulk endpoint that does not exist) means a failure part-way leaves the rows
 * already saved on the server, which the success path then re-reads; the draft stays on screen either
 * way, so nothing the owner typed is lost.
 */
export function useSaveFloorPlan() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (plan: SaveFloorPlan) => {
      const created: TableDto[] = [];
      for (const input of plan.create) {
        created.push(await createTable(input));
      }

      const updated: TableDto[] = [];
      for (const input of plan.update) {
        updated.push(await updateTable(input.id, input));
      }

      return { created, updated };
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: tablesQueryKey });
    },
  });
}
