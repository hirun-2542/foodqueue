"use client";

import { useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError, apiFetch } from "@/lib/api";
import type { PackageDto, PackageInput } from "@/lib/types";

export const packagesQueryKey = ["packages"] as const;
export const buffetItemsQueryKey = ["menu-items", "buffet"] as const;

/**
 * Only the fields the per-tier ladder needs, declared here rather than in `lib/types.ts` so this
 * ticket does not pre-empt the menu module's own wire type for `GET /api/menu-items`.
 */
interface MenuItemWire {
  id: number;
  name: string;
  kind: string;
  minPackageRank: number | null;
  isActive: boolean;
}

/**
 * The edit form holds text, not numbers: an empty or half-typed box ("" or "1.") has to be
 * representable so the field can show its own error instead of silently becoming 0.
 */
export interface PackageDraft {
  name: string;
  rank: string;
  adultPrice: string;
  childPrice: string;
  durationMinutes: string;
  isActive: boolean;
}

export interface PackageFormErrors {
  name?: string;
  rank?: string;
  adultPrice?: string;
  childPrice?: string;
  durationMinutes?: string;
  form?: string;
}

export type SavePackageResult = { ok: true; id: number } | { ok: false; errors: PackageFormErrors };

export function draftOf(pkg: PackageDto): PackageDraft {
  return {
    name: pkg.name,
    rank: String(pkg.rank),
    adultPrice: String(pkg.adultPrice),
    childPrice: String(pkg.childPrice),
    durationMinutes: String(pkg.durationMinutes),
    isActive: pkg.isActive,
  };
}

/** Numeric field as a number, or null when it is not a number at all (`""`, `"1.2.3"`). */
function numberOf(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed.length === 0) return null;
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : null;
}

/**
 * Mirrors the words the server uses for each rule so the inline error reads the same whether it was
 * caught here or came back as `VALIDATION_FAILED`. Returns null when the draft is acceptable.
 */
function validateDraft(draft: PackageDraft, rows: readonly PackageDto[], editingId: number | null): PackageFormErrors {
  const errors: PackageFormErrors = {};

  if (draft.name.trim().length === 0) {
    errors.name = "กรุณากรอกชื่อแพ็กเกจ";
  }

  const rank = numberOf(draft.rank);
  if (rank === null || !Number.isInteger(rank)) {
    errors.rank = "กรอกระดับเป็นจำนวนเต็ม";
  } else if (rank < 1 || rank > 99) {
    errors.rank = "ระดับต้องอยู่ระหว่าง 1 ถึง 99";
  } else {
    // The frame's whole point: the duplicate is named, and a free rank is suggested.
    const clash = rows.find((p) => p.rank === rank && p.id !== editingId);
    if (clash) {
      errors.rank = `ระดับนี้ซ้ำกับ ${clash.name} · เลือกระดับ ${nextFreeRank(rows)} แทน`;
    }
  }

  const adult = numberOf(draft.adultPrice);
  if (adult === null) {
    errors.adultPrice = "กรอกราคาผู้ใหญ่เป็นตัวเลข";
  } else if (adult <= 0) {
    errors.adultPrice = "ราคาผู้ใหญ่ต้องมากกว่า 0 บาท";
  }

  const child = numberOf(draft.childPrice);
  if (child === null) {
    errors.childPrice = "กรอกราคาเด็กเป็นตัวเลข";
  } else if (child < 0) {
    errors.childPrice = "ราคาเด็กต้องไม่ติดลบ";
  } else if (adult !== null && child > adult) {
    errors.childPrice = "ราคาเด็กต้องไม่เกินราคาผู้ใหญ่";
  }

  const minutes = numberOf(draft.durationMinutes);
  if (minutes === null || !Number.isInteger(minutes)) {
    errors.durationMinutes = "กรอกเวลาเป็นจำนวนนาที";
  } else if (minutes < 1 || minutes > 600) {
    errors.durationMinutes = "เวลาต้องอยู่ระหว่าง 1 ถึง 600 นาที";
  }

  return errors;
}

function nextFreeRank(rows: readonly PackageDto[]): number {
  return rows.reduce((max, p) => Math.max(max, p.rank), 0) + 1;
}

/**
 * A `VALIDATION_FAILED` message is Thai prose about one field, so it is routed back to that field
 * using the same keyword the server used. Anything else is a form-level message.
 */
function toFieldErrors(error: ApiError): PackageFormErrors {
  const message = error.message;
  if (error.code === "VALIDATION_FAILED") {
    if (message.includes("ชื่อแพ็กเกจ")) return { name: message };
    if (message.includes("ระดับ")) return { rank: message };
    if (message.includes("ราคาผู้ใหญ่")) return { adultPrice: message };
    if (message.includes("ราคาเด็ก")) return { childPrice: message };
    if (message.includes("เวลา")) return { durationMinutes: message };
  }
  return { form: message };
}

export function usePackages() {
  const query = useQuery({
    queryKey: packagesQueryKey,
    queryFn: () => apiFetch<PackageDto[]>("/api/packages"),
    staleTime: 30_000,
  });

  /**
   * The buffet menu decides the per-tier ladder. `/api/menu-items` belongs to the menu module, so
   * until that screen exists the request fails and the ladder says so instead of inventing counts.
   */
  const menuQuery = useQuery({
    queryKey: buffetItemsQueryKey,
    queryFn: () => apiFetch<MenuItemWire[]>("/api/menu-items"),
    staleTime: 30_000,
    retry: false,
  });

  const rows = query.data ?? [];

  const save = async (editingId: number | null, draft: PackageDraft): Promise<SavePackageResult> => {
    const local = validateDraft(draft, rows, editingId);
    if (Object.keys(local).length > 0) {
      return { ok: false, errors: local };
    }

    const body: PackageInput = {
      name: draft.name.trim(),
      rank: Number(draft.rank),
      adultPrice: Number(draft.adultPrice),
      childPrice: Number(draft.childPrice),
      durationMinutes: Number(draft.durationMinutes),
      isActive: draft.isActive,
    };

    try {
      const saved =
        editingId === null
          ? await apiFetch<PackageDto>("/api/packages", { method: "POST", body: JSON.stringify(body) })
          : await apiFetch<PackageDto>(`/api/packages/${editingId}`, { method: "PUT", body: JSON.stringify(body) });
      await query.refetch();
      toast.success(editingId === null ? `เพิ่มแพ็กเกจ ${saved.name} แล้ว` : `บันทึกแพ็กเกจ ${saved.name} แล้ว`);
      return { ok: true, id: saved.id };
    } catch (error) {
      if (error instanceof ApiError) {
        const errors = toFieldErrors(error);
        // A field-level message is already shown under its input; only a form-level one is toasted.
        if (errors.form) toast.error(errors.form);
        return { ok: false, errors };
      }
      throw error;
    }
  };

  return {
    packages: query.data,
    isLoading: query.isPending,
    isError: query.isError,
    error: query.error,
    save,
    buffetItems: menuQuery.data,
    isMenuLoading: menuQuery.isPending,
    isMenuUnavailable: menuQuery.isError,
  };
}
