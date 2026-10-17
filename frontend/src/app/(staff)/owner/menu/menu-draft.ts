"use client";

import { ApiError } from "@/lib/api";
import type { MenuItemDto, MenuItemKind } from "@/lib/types";
import { MENU_DESCRIPTION_MAX, MENU_NAME_MAX } from "./menu-labels";

/**
 * The edit drawer holds text, not numbers: an empty or half-typed box has to be representable so the
 * field can show its own error instead of silently becoming 0 (same rule as the packages form).
 * `categoryId` and `minPackageRank` are strings because that is what a `<Select>` yields.
 */
export interface MenuItemDraft {
  name: string;
  categoryId: string;
  description: string;
  kind: MenuItemKind;
  minPackageRank: string;
  price: string;
  isAvailable: boolean;
  isActive: boolean;
}

/** One entry per field of the drawer, plus the form-level message for anything that is not one field. */
export interface MenuFormErrors {
  name?: string;
  categoryId?: string;
  description?: string;
  kind?: string;
  minPackageRank?: string;
  price?: string;
  form?: string;
}

export function draftOf(item: MenuItemDto): MenuItemDraft {
  return {
    name: item.name,
    categoryId: String(item.categoryId),
    description: item.description ?? "",
    kind: item.kind,
    minPackageRank: item.minPackageRank == null ? "" : String(item.minPackageRank),
    price: String(item.price),
    isAvailable: item.isAvailable,
    isActive: item.isActive,
  };
}

/** A new item starts as a buffet dish on the first tier, which is what most of the menu is. */
export function emptyDraft(categoryId: number | null, defaultRank: string): MenuItemDraft {
  return {
    name: "",
    categoryId: categoryId == null ? "" : String(categoryId),
    description: "",
    kind: "Buffet",
    minPackageRank: defaultRank,
    price: "0",
    isAvailable: true,
    isActive: true,
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
 * Mirrors the words the server uses for each rule, so an inline error reads the same whether it was
 * caught here or came back as `VALIDATION_FAILED`. Empty object = acceptable. Kinds are checked on the
 * draft's *current* kind: switching to คิดเงินแยก must not keep demanding a tier.
 */
export function validateDraft(draft: MenuItemDraft): MenuFormErrors {
  const errors: MenuFormErrors = {};
  const name = draft.name.trim();

  if (name.length === 0) {
    errors.name = "กรุณากรอกชื่อเมนู";
  } else if (name.length > MENU_NAME_MAX) {
    errors.name = `ชื่อเมนูยาวเกิน ${MENU_NAME_MAX} ตัวอักษร`;
  }

  if (draft.categoryId.length === 0) {
    errors.categoryId = "เลือกหมวดหมู่ของเมนู";
  }

  const description = draft.description.trim();
  if (description.length > MENU_DESCRIPTION_MAX) {
    errors.description = `ยาวเกิน ${MENU_DESCRIPTION_MAX} ตัวอักษร · ลบออก ${description.length - MENU_DESCRIPTION_MAX} ตัวอักษร`;
  }

  const price = numberOf(draft.price);
  if (price === null) {
    errors.price = "กรอกราคาเป็นตัวเลข";
  }

  if (draft.kind === "Buffet") {
    const rank = numberOf(draft.minPackageRank);
    if (rank === null || !Number.isInteger(rank)) {
      errors.minPackageRank = "เมนูบุฟเฟ่ต์ต้องเลือกแพ็กเกจขั้นต่ำ";
    } else if (rank < 1 || rank > 99) {
      errors.minPackageRank = "แพ็กเกจขั้นต่ำต้องอยู่ระหว่าง 1 ถึง 99";
    }
    if (price !== null && price !== 0) {
      errors.price = "เมนูบุฟเฟ่ต์รวมอยู่ในราคาบุฟเฟ่ต์แล้ว ราคาต้องเป็น 0 บาท";
    }
  } else if (price !== null && price <= 0) {
    errors.price = "เมนูคิดเงินแยกต้องมีราคามากกว่า 0 บาท";
  }

  return errors;
}

/**
 * A `VALIDATION_FAILED` message is Thai prose about one field, so it is routed back to that field using
 * the same keyword the server used. Anything else is a form-level message.
 */
export function toFieldErrors(error: ApiError): MenuFormErrors {
  const message = error.message;
  if (error.code === "VALIDATION_FAILED") {
    if (message.includes("ชื่อเมนู")) return { name: message };
    if (message.includes("คำอธิบาย")) return { description: message };
    if (message.includes("หมวดหมู่")) return { categoryId: message };
    if (message.includes("ชนิดเมนู")) return { kind: message };
    if (message.includes("แพ็กเกจขั้นต่ำ")) return { minPackageRank: message };
    if (message.includes("ราคา")) return { price: message };
  }
  return { form: message };
}
