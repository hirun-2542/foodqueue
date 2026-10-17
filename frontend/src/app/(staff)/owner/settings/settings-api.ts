"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError, apiFetch } from "@/lib/api";
import type { SettingsDto } from "@/lib/types";

/** The shop's single settings row; every owner screen that needs it shares this key. */
export const settingsQueryKey = ["settings"] as const;

export function useSettings() {
  return useQuery({
    queryKey: settingsQueryKey,
    queryFn: () => apiFetch<SettingsDto>("/api/settings"),
    // The form holds unsaved edits: a background refetch would overwrite them. The row only changes
    // through this screen, and every mutation writes the server's answer straight into the cache.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

/** A server answer always wins: put the returned row straight into the cache. */
function useSettingsWriter() {
  const queryClient = useQueryClient();
  return (settings: SettingsDto) => queryClient.setQueryData(settingsQueryKey, settings);
}

function reportFailure(error: unknown, fallback: string) {
  toast.error(error instanceof ApiError ? error.message : fallback);
}

export type SettingsInput = Omit<SettingsDto, "bannerImagePath">;

/** The PUT replaces every field of the row, so the form always sends the whole object. */
export function useSaveSettings() {
  const write = useSettingsWriter();
  return useMutation({
    mutationFn: (input: SettingsInput) =>
      apiFetch<SettingsDto>("/api/settings", { method: "PUT", body: JSON.stringify(input) }),
    onSuccess: write,
    onError: (error) => reportFailure(error, "บันทึกการตั้งค่าไม่สำเร็จ ลองอีกครั้ง"),
  });
}

/**
 * The client-side half of the upload rule (the server is the authority): jpeg/png/webp whose extension
 * matches, at most 2 MB. Kept identical to the backend message so the user sees one sentence either way.
 */
export const BANNER_INVALID_MESSAGE = "ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB";

const BANNER_MAX_BYTES = 2 * 1024 * 1024;
const ALLOWED: Record<string, string[]> = {
  "image/jpeg": [".jpg", ".jpeg"],
  "image/png": [".png"],
  "image/webp": [".webp"],
};

export function bannerRejection(file: File): string | null {
  const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
  const matches = ALLOWED[file.type]?.includes(extension) ?? false;
  return file.size > 0 && file.size <= BANNER_MAX_BYTES && matches ? null : BANNER_INVALID_MESSAGE;
}

export function useUploadBanner() {
  const write = useSettingsWriter();
  return useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append("file", file);
      return apiFetch<SettingsDto>("/api/settings/banner", { method: "POST", body });
    },
    onSuccess: write,
    onError: (error) => reportFailure(error, "อัปโหลดรูปแบนเนอร์ไม่สำเร็จ ลองอีกครั้ง"),
  });
}

export function useClearBanner() {
  const write = useSettingsWriter();
  return useMutation({
    mutationFn: () => apiFetch<SettingsDto>("/api/settings/banner", { method: "DELETE" }),
    onSuccess: write,
    onError: (error) => reportFailure(error, "กลับไปใช้แบนเนอร์เริ่มต้นไม่สำเร็จ ลองอีกครั้ง"),
  });
}
