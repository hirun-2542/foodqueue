"use client";

import type { MenuItemDto, MenuItemKind, PackageDto } from "@/lib/types";

/**
 * Copy and small computations shared by the menu table and the edit drawer. Every Thai string here is
 * from `notes/10-THAI-COPY.md` §o09-menu / §o10-menu-edit, so no screen invents a second wording.
 */

export const KIND_LABEL: Record<MenuItemKind, string> = {
  Buffet: "บุฟเฟ่ต์",
  AlaCarte: "คิดเงินแยก",
};

/** The price cell of a buffet item: it is inside the buffet price, so there is no number to show. */
export const BUFFET_PRICE_LABEL = "รวมในบุฟเฟ่ต์";

/** The drawer's read-only price well for a buffet item (`o10-menu-edit`). */
export const BUFFET_PRICE_WELL = "รวมในราคาบุฟเฟ่ต์ · ราคา ฿0";

export const MENU_NAME_MAX = 80;
export const MENU_DESCRIPTION_MAX = 200;

/**
 * "Silver ขึ้นไป" for a minimum rank, and just "Platinum" when it is the top tier — nothing sits above
 * it, so "ขึ้นไป" would be noise (frame `o09-menu` shows exactly this for ranks 1, 2 and 3).
 */
export function minimumRankLabel(rank: number | null | undefined, packages: readonly PackageDto[]): string {
  if (rank == null) return "-";
  const named = packages.find((p) => p.rank === rank);
  if (!named) return `ระดับ ${rank} ขึ้นไป`;
  return packages.every((p) => p.rank <= rank) ? named.name : `${named.name} ขึ้นไป`;
}

/** The drawer's subtitle: "ริบอายสไลซ์ · เนื้อวัว · ระดับ Platinum". */
export function drawerSubtitle(item: MenuItemDto, packages: readonly PackageDto[]): string {
  if (item.kind !== "Buffet") return `${item.name} · ${item.categoryName} · ${KIND_LABEL[item.kind]}`;
  const named = packages.find((p) => p.rank === item.minPackageRank);
  return `${item.name} · ${item.categoryName} · ระดับ ${named?.name ?? item.minPackageRank}`;
}

/**
 * The no-photo fallback of the frames: a lucide category icon on a quiet tile. Keyed by the category
 * names of `design/DATA.md`; anything unknown gets the neutral utensils tile rather than guessing from
 * the item's name. The value is the lucide kebab-case name used as the key of `MENU_TILE_ICONS`.
 */
export const MENU_TILE_ICONS = {
  น้ำซุป: "soup",
  หมู: "ham",
  เนื้อวัว: "beef",
  ทะเล: "fish",
  "ลูกชิ้น/ของทอด": "drumstick",
  ผัก: "carrot",
  ของหวาน: "ice-cream",
  เครื่องดื่ม: "cup-soda",
} as const satisfies Record<string, string>;
