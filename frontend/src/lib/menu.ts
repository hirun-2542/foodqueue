"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError, apiFetch } from "@/lib/api";
import type { CategoryDto, CategoryInput, MenuItemDto, MenuItemInput, PackageDto } from "@/lib/types";

/**
 * Menu data access for `/owner/menu` (ticket 05), and the client half of the two rules the API
 * enforces. The query keys are the shared ones: `["menu-items"]` is a prefix of ticket 04's
 * `["menu-items", "buffet"]`, so invalidating the menu refreshes that screen's per-tier ladder too.
 */
export const categoriesQueryKey = ["categories"] as const;
export const menuItemsQueryKey = ["menu-items"] as const;
export const packagesQueryKey = ["packages"] as const;

/** The packages decide the minimum-package-rank wording ("Silver ขึ้นไป"); the owner screen reads them. */
export function usePackagesForMenu() {
  return useQuery({
    queryKey: packagesQueryKey,
    queryFn: () => apiFetch<PackageDto[]>("/api/packages"),
    staleTime: 30_000,
  });
}

export function useCategories() {
  return useQuery({
    queryKey: categoriesQueryKey,
    queryFn: () => apiFetch<CategoryDto[]>("/api/categories"),
    staleTime: 30_000,
  });
}

export function useMenuItems() {
  return useQuery({
    queryKey: menuItemsQueryKey,
    queryFn: () => apiFetch<MenuItemDto[]>("/api/menu-items"),
    staleTime: 30_000,
  });
}

function reportFailure(error: unknown, fallback: string) {
  toast.error(error instanceof ApiError ? error.message : fallback);
}

// ------------------------------------------------------------------ หมวดหมู่

export function useCreateCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CategoryInput) =>
      apiFetch<CategoryDto>("/api/categories", { method: "POST", body: JSON.stringify(input) }),
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: categoriesQueryKey });
      toast.success(`เพิ่มหมวด ${created.name} แล้ว`);
    },
    onError: (error) => reportFailure(error, "เพิ่มหมวดหมู่ไม่สำเร็จ ลองอีกครั้ง"),
  });
}

export function useUpdateCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number; input: CategoryInput }) =>
      apiFetch<CategoryDto>(`/api/categories/${id}`, { method: "PUT", body: JSON.stringify(input) }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: categoriesQueryKey });
      await queryClient.invalidateQueries({ queryKey: menuItemsQueryKey });
      toast.success(`บันทึกชื่อหมวด ${saved.name} แล้ว`);
    },
    onError: (error) => reportFailure(error, "บันทึกหมวดหมู่ไม่สำเร็จ ลองอีกครั้ง"),
  });
}

/**
 * Deleting a category that still has items is refused by the API with a Thai sentence naming the
 * count; it is shown next to the row rather than only as a toast, because it is about that row.
 */
export function useDeleteCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiFetch<void>(`/api/categories/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: categoriesQueryKey });
      toast.success("ลบหมวดหมู่แล้ว");
    },
  });
}

// ------------------------------------------------------------------ เมนู

export function useSaveMenuItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number | null; input: MenuItemInput }) =>
      id === null
        ? apiFetch<MenuItemDto>("/api/menu-items", { method: "POST", body: JSON.stringify(input) })
        : apiFetch<MenuItemDto>(`/api/menu-items/${id}`, { method: "PUT", body: JSON.stringify(input) }),
    onSuccess: async (saved, { id }) => {
      await queryClient.invalidateQueries({ queryKey: menuItemsQueryKey });
      await queryClient.invalidateQueries({ queryKey: categoriesQueryKey });
      toast.success(id === null ? `เพิ่มเมนู ${saved.name} แล้ว` : `บันทึกเมนู ${saved.name} แล้ว`);
    },
    onError: (error) => reportFailure(error, "บันทึกเมนูไม่สำเร็จ ลองอีกครั้ง"),
  });
}

/**
 * The availability switch writes immediately and the row changes before the answer comes back, because
 * the owner is standing in the dining room with a dish that just ran out. A failure (or the server's
 * own value) replaces the guess.
 */
export function useSetAvailability() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, isAvailable }: { id: number; isAvailable: boolean }) =>
      apiFetch<MenuItemDto>(`/api/menu-items/${id}/availability`, {
        method: "PATCH",
        body: JSON.stringify({ isAvailable }),
      }),
    onMutate: async ({ id, isAvailable }) => {
      await queryClient.cancelQueries({ queryKey: menuItemsQueryKey });
      const previous = queryClient.getQueryData<MenuItemDto[]>(menuItemsQueryKey);
      queryClient.setQueryData<MenuItemDto[]>(menuItemsQueryKey, (items) =>
        items?.map((item) => (item.id === id ? { ...item, isAvailable } : item)),
      );
      return { previous };
    },
    onError: (error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(menuItemsQueryKey, context.previous);
      }
      reportFailure(error, "สลับสถานะไม่สำเร็จ ลองอีกครั้ง");
    },
    onSuccess: (saved, { isAvailable }) => {
      toast.success(isAvailable ? `เปิดขาย ${saved.name} แล้ว` : `ตั้ง ${saved.name} เป็น “หมดชั่วคราว” แล้ว`);
    },
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: menuItemsQueryKey });
    },
  });
}

/**
 * The client-side half of the upload rule (the server is the authority): jpeg/png/webp whose extension
 * matches, at most 2 MB. The sentence is the one frozen in `notes/00-CONTRACTS.md`; ticket 03 holds the
 * same literal for the shop banner, in `settings-api.ts`, because that route owns its own copy.
 */
export const MENU_IMAGE_INVALID_MESSAGE = "ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB";

const IMAGE_MAX_BYTES = 2 * 1024 * 1024;
const ALLOWED_IMAGE_TYPES: Record<string, string[]> = {
  "image/jpeg": [".jpg", ".jpeg"],
  "image/png": [".png"],
  "image/webp": [".webp"],
};

export function menuImageRejection(file: File): string | null {
  const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
  const matches = ALLOWED_IMAGE_TYPES[file.type]?.includes(extension) ?? false;
  return file.size > 0 && file.size <= IMAGE_MAX_BYTES && matches ? null : MENU_IMAGE_INVALID_MESSAGE;
}

export function useUploadMenuImage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, file }: { id: number; file: File }) => {
      const body = new FormData();
      body.append("file", file);
      return apiFetch<MenuItemDto>(`/api/menu-items/${id}/image`, { method: "POST", body });
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: menuItemsQueryKey });
      toast.success(`เปลี่ยนรูป ${saved.name} แล้ว`);
    },
    onError: (error) => reportFailure(error, "อัปโหลดรูปไม่สำเร็จ ลองอีกครั้ง"),
  });
}

export function useDeleteMenuImage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiFetch<MenuItemDto>(`/api/menu-items/${id}/image`, { method: "DELETE" }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: menuItemsQueryKey });
      toast.success(`ลบรูป ${saved.name} แล้ว`);
    },
    onError: (error) => reportFailure(error, "ลบรูปไม่สำเร็จ ลองอีกครั้ง"),
  });
}
