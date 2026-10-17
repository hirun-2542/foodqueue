"use client";

import {
  BeefIcon,
  BookOpenIcon,
  CarrotIcon,
  CheckIcon,
  ChevronsUpDownIcon,
  CircleAlertIcon,
  CupSodaIcon,
  DrumstickIcon,
  EllipsisVerticalIcon,
  FishIcon,
  HamIcon,
  IceCreamBowlIcon,
  ImagePlusIcon,
  Loader2Icon,
  PlusIcon,
  SearchIcon,
  SoupIcon,
  Trash2Icon,
  UtensilsIcon,
} from "lucide-react";
import { useState, type ReactNode } from "react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/lib/api";
import { formatBahtWhole } from "@/lib/format";
import {
  menuImageRejection,
  useCategories,
  useCreateCategory,
  useDeleteCategory,
  useDeleteMenuImage,
  useMenuItems,
  usePackagesForMenu,
  useSaveMenuItem,
  useSetAvailability,
  useUpdateCategory,
  useUploadMenuImage,
} from "@/lib/menu";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import type { CategoryDto, MenuItemDto, PackageDto } from "@/lib/types";
import { draftOf, emptyDraft, toFieldErrors, validateDraft, type MenuFormErrors, type MenuItemDraft } from "./menu-draft";
import {
  BUFFET_PRICE_LABEL,
  BUFFET_PRICE_WELL,
  KIND_LABEL,
  MENU_DESCRIPTION_MAX,
  MENU_TILE_ICONS,
  drawerSubtitle,
  minimumRankLabel,
} from "./menu-labels";

/**
 * `/owner/menu` — frame `o09-menu` (category rail + item table with the availability switch) and
 * `o10-menu-edit` (the right-hand drawer whose fields follow the item's kind).
 *
 * The live badge of the frame needs a hub connection, which is opened here for this screen only;
 * `lib/realtime.ts` wants the provider mounted once per screen tree, so the day the staff layout grows
 * one (ticket 11/13), this one is deleted rather than nested.
 */
export function MenuScreen() {
  return (
    <LiveProvider>
      <MenuScreenBody />
    </LiveProvider>
  );
}

function MenuScreenBody() {
  const isOffline = useLiveOffline();
  const categories = useCategories();
  const items = useMenuItems();
  const packages = usePackagesForMenu();
  const saveItem = useSaveMenuItem();
  const setAvailability = useSetAvailability();
  const uploadImage = useUploadMenuImage();
  const deleteImage = useDeleteMenuImage();

  const [search, setSearch] = useState("");
  const [categoryFilter, setCategoryFilter] = useState<number | null>(null);
  const [sort, setSort] = useState<SortState>(null);
  const [drawer, setDrawer] = useState<{ id: number | null; draft: MenuItemDraft } | null>(null);
  const [errors, setErrors] = useState<MenuFormErrors>({});
  const [isCategoriesOpen, setIsCategoriesOpen] = useState(false);

  const categoryRows = categories.data ?? [];
  const packageRows = packages.data ?? [];
  const allItems = items.data ?? [];

  const needle = search.trim().toLowerCase();
  const filtered = allItems.filter(
    (item) =>
      (categoryFilter === null || item.categoryId === categoryFilter) &&
      (needle.length === 0 || item.name.toLowerCase().includes(needle)),
  );
  const visibleItems = sort ? sortItems(filtered, sort) : filtered;
  const unavailableCount = allItems.filter((item) => !item.isAvailable).length;

  /** None → ascending → descending → none, so the server's own order is always one click away. */
  const cycleSort = (key: SortKey) =>
    setSort((current) => {
      if (current?.key !== key) return { key, direction: "asc" };
      return current.direction === "asc" ? { key, direction: "desc" } : null;
    });

  const editingItem = drawer?.id == null ? null : (allItems.find((item) => item.id === drawer.id) ?? null);

  const openCreate = () => {
    setErrors({});
    setDrawer({
      id: null,
      draft: emptyDraft(
        categoryFilter ?? categoryRows[0]?.id ?? null,
        // The lowest tier is the one every package can order, so it is the natural default.
        packageRows.length === 0 ? "1" : String(Math.min(...packageRows.map((p) => p.rank))),
      ),
    });
  };

  const openEdit = (item: MenuItemDto) => {
    setErrors({});
    setDrawer({ id: item.id, draft: draftOf(item) });
  };

  const closeDrawer = () => {
    setDrawer(null);
    setErrors({});
  };

  const submit = async () => {
    if (!drawer) return;
    const draft = drawer.draft;
    const local = validateDraft(draft);
    if (Object.keys(local).length > 0) {
      setErrors(local);
      return;
    }

    const result = await saveItem
      .mutateAsync({
        id: drawer.id,
        input: {
          name: draft.name.trim(),
          categoryId: Number(draft.categoryId),
          description: draft.description.trim() === "" ? null : draft.description.trim(),
          kind: draft.kind,
          minPackageRank: draft.kind === "Buffet" ? Number(draft.minPackageRank) : null,
          // The kind's rule owns the price: a buffet item is always 0, whatever is left in the box.
          price: draft.kind === "Buffet" ? 0 : Number(draft.price),
          isAvailable: draft.isAvailable,
          isActive: draft.isActive,
        },
      })
      .catch((error: unknown) => {
        setErrors(error instanceof ApiError ? toFieldErrors(error) : { form: "บันทึกเมนูไม่สำเร็จ ลองอีกครั้ง" });
        return null;
      });

    if (!result) return;
    // A created item stays on screen with its new id, so the image controls below become usable
    // without closing and reopening the drawer.
    setErrors({});
    setDrawer({ id: result.id, draft: draftOf(result) });
  };

  return (
    <div className="flex flex-col">
      <div className="flex min-h-20 flex-wrap items-center justify-between gap-4">
        <div className="flex flex-col">
          <h1 className="text-headline">เมนูอาหาร</h1>
          <span className="text-label font-normal text-muted-foreground">
            {allItems.length} รายการ · {categoryRows.length} หมวดหมู่ · หมดชั่วคราว {unavailableCount} รายการ
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative w-[236px]">
            <SearchIcon
              className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
              strokeWidth={1.75}
              aria-hidden
            />
            <Input
              type="search"
              value={search}
              aria-label="ค้นหาเมนู"
              placeholder="ค้นหาเมนู…"
              className="pl-9"
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <Button type="button" onClick={openCreate} disabled={categoryRows.length === 0}>
            <PlusIcon aria-hidden />
            เพิ่มเมนู
          </Button>
          <LiveBadge isOffline={isOffline} />
          <span className="inline-flex h-[22px] items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
            DEMO DATA
          </span>
        </div>
      </div>

      <div className="mt-2 grid items-start gap-4 lg:grid-cols-[240px_1fr]">
        <Card className="gap-0 overflow-hidden p-0">
          <div className="flex flex-col p-2">
            <CategoryRow
              label="ทั้งหมด"
              count={allItems.length}
              selected={categoryFilter === null}
              onSelect={() => setCategoryFilter(null)}
            />
            {categoryRows.map((category) => (
              <CategoryRow
                key={category.id}
                label={category.name}
                count={category.itemCount}
                selected={categoryFilter === category.id}
                onSelect={() => setCategoryFilter(category.id)}
              />
            ))}
          </div>
          <button
            type="button"
            onClick={() => setIsCategoriesOpen(true)}
            className="flex items-center gap-2 border-t border-border px-5 py-[13px] text-[13.5px] font-semibold text-orange-ink hover:bg-orange-soft"
          >
            <UtensilsIcon className="size-4" strokeWidth={1.75} aria-hidden />
            จัดการหมวดหมู่
          </button>
        </Card>

        <Card className="gap-0 overflow-hidden p-0">
          {items.isPending ? (
            <div className="flex flex-col gap-3 p-4">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-14 w-full" />
              <Skeleton className="h-14 w-full" />
              <Skeleton className="h-14 w-full" />
            </div>
          ) : items.isError ? (
            <div className="flex items-start gap-3 p-4">
              <CircleAlertIcon className="mt-0.5 size-5 text-cancel-ink" strokeWidth={1.75} aria-hidden />
              <div className="flex flex-col gap-0.5">
                <b className="text-title">โหลดเมนูไม่ได้</b>
                <span className="text-body text-muted-foreground">
                  {items.error instanceof ApiError
                    ? items.error.message
                    : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
                </span>
              </div>
            </div>
          ) : allItems.length === 0 ? (
            <div className="flex flex-col items-start gap-2 p-6">
              <BookOpenIcon className="size-5 text-ink-2" strokeWidth={1.75} aria-hidden />
              <b className="text-title">ยังไม่มีเมนู</b>
              <span className="text-body text-muted-foreground">
                สร้างหมวดหมู่ก่อน แล้วกด “เพิ่มเมนู” เพื่อเพิ่มรายการแรก เช่น สันคอหมูสไลซ์ (บุฟเฟ่ต์ · Silver ขึ้นไป)
              </span>
            </div>
          ) : (
            <>
              <table className="w-full border-collapse text-[14px]">
                <thead>
                  <tr className="bg-sunken">
                    <Th>
                      <SortHeader label="เมนู" sortKey="name" sort={sort} onSort={cycleSort} />
                    </Th>
                    <Th>
                      <SortHeader label="ชนิด" sortKey="kind" sort={sort} onSort={cycleSort} />
                    </Th>
                    <Th>
                      <SortHeader label="แพ็กเกจขั้นต่ำ" sortKey="rank" sort={sort} onSort={cycleSort} />
                    </Th>
                    <Th className="w-[170px] text-right">
                      <SortHeader label="ราคา" sortKey="price" sort={sort} align="end" onSort={cycleSort} />
                    </Th>
                    <Th className="w-[120px]">พร้อมขาย</Th>
                    <Th className="w-[56px]">{""}</Th>
                  </tr>
                </thead>
                <tbody>
                  {visibleItems.map((item) => (
                    <ItemRow
                      key={item.id}
                      item={item}
                      packages={packageRows}
                      onEdit={() => openEdit(item)}
                      onToggle={(isAvailable) => setAvailability.mutate({ id: item.id, isAvailable })}
                      onDeleteImage={() => deleteImage.mutate(item.id)}
                    />
                  ))}
                  {visibleItems.length === 0 ? (
                    <tr className="border-t border-border">
                      <td colSpan={6} className="px-4 py-8 text-center text-body text-muted-foreground">
                        ไม่มีเมนูที่ตรงกับ “{search.trim()}”
                      </td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
              <div className="flex items-center justify-between gap-3 border-t border-border px-4 py-[13px]">
                <span className="text-label font-normal text-muted-foreground">
                  แสดง {visibleItems.length} จาก {allItems.length} รายการ
                </span>
                <span className="text-label font-normal text-muted-foreground">เรียงตามหมวดหมู่ · ลำดับ</span>
              </div>
            </>
          )}
        </Card>
      </div>

      {drawer ? (
        <ItemDrawer
          item={editingItem}
          draft={drawer.draft}
          errors={errors}
          packages={packageRows}
          categories={categoryRows}
          saving={saveItem.isPending || uploadImage.isPending || deleteImage.isPending}
          uploading={uploadImage.isPending}
          onChange={(draft) => setDrawer({ id: drawer.id, draft })}
          onCancel={closeDrawer}
          onSave={() => void submit()}
          onUpload={(file) => {
            if (drawer.id === null) return;
            uploadImage.mutate({ id: drawer.id, file });
          }}
          onDeleteImage={() => {
            if (drawer.id === null) return;
            deleteImage.mutate(drawer.id);
          }}
        />
      ) : null}

      {isCategoriesOpen ? <CategoryManager onClose={() => setIsCategoriesOpen(false)} /> : null}
    </div>
  );
}

/** Frame `o09-menu`: green pill with a dot while the hub is up, the offline wording when it is down. */
function LiveBadge({ isOffline }: { isOffline: boolean }) {
  return (
    <span
      className={`inline-flex h-8 items-center gap-2 rounded-full px-3 text-label font-semibold ${
        isOffline ? "bg-sunken text-ink-2" : "bg-ready-soft text-ready-ink"
      }`}
    >
      <i className={`size-[7px] rounded-full ${isOffline ? "bg-served-dot" : "bg-ready-dot"}`} aria-hidden />
      {isOffline ? "ออฟไลน์ · ข้อมูลอาจไม่ใหม่" : "เชื่อมต่อแบบเรียลไทม์"}
    </span>
  );
}

function CategoryRow({
  label,
  count,
  selected,
  onSelect,
}: {
  label: string;
  count: number;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-current={selected ? "true" : undefined}
      className={`flex h-[38px] items-center justify-between gap-2 rounded-lg px-3 text-[14px] ${
        selected ? "bg-orange-soft font-semibold text-orange-ink" : "text-ink-2 hover:bg-sunken"
      }`}
    >
      {label}
      <span className={`text-[13px] tabular-nums ${selected ? "text-orange-ink" : "text-muted-foreground"}`}>
        {count}
      </span>
    </button>
  );
}

/**
 * Frame `o09-menu` draws a sort glyph in every header, so the glyph is a real control here: clicking
 * cycles none → ascending → descending → none. "None" is the server's own order, which the footer
 * names ("เรียงตามหมวดหมู่ · ลำดับ").
 */
export type SortKey = "name" | "kind" | "rank" | "price";
export type SortState = { key: SortKey; direction: "asc" | "desc" } | null;

function SortHeader({
  label,
  sortKey,
  sort,
  align = "start",
  onSort,
}: {
  label: string;
  sortKey: SortKey;
  sort: SortState;
  align?: "start" | "end";
  onSort: (key: SortKey) => void;
}) {
  const active = sort?.key === sortKey;
  return (
    <button
      type="button"
      onClick={() => onSort(sortKey)}
      aria-label={`เรียงตาม${label}`}
      className={`flex items-center gap-1.5 hover:text-ink ${align === "end" ? "ml-auto" : ""} ${
        active ? "text-ink" : ""
      }`}
    >
      {label}
      <ChevronsUpDownIcon className="size-3.5" strokeWidth={1.75} aria-hidden />
    </button>
  );
}

function Th({ className = "", children }: { className?: string; children: ReactNode }) {
  return (
    <th
      className={`px-4 py-2.5 text-left text-[12.5px] font-semibold whitespace-nowrap text-muted-foreground ${className}`}
    >
      {children}
    </th>
  );
}

/**
 * A stable sort for each sortable column. Buffet items carry no price, so they sort as 0 — which puts
 * them first ascending, next to the other "no price" rows the owner is looking for.
 */
function sortItems(items: readonly MenuItemDto[], sort: { key: SortKey; direction: "asc" | "desc" }) {
  const sign = sort.direction === "asc" ? 1 : -1;
  const value = (item: MenuItemDto): string | number => {
    switch (sort.key) {
      case "name":
        return item.name;
      case "kind":
        return item.kind;
      case "rank":
        return item.kind === "Buffet" ? (item.minPackageRank ?? 0) : 0;
      case "price":
        return item.kind === "Buffet" ? 0 : item.price;
    }
  };
  return [...items].sort((a, b) => {
    const left = value(a);
    const right = value(b);
    const order = typeof left === "string" ? left.localeCompare(right as string, "th") : left - (right as number);
    return order * sign;
  });
}

/** The no-photo fallback of the frames: a lucide category icon on a quiet tile. */
const TILE_ICON_COMPONENTS: Record<string, typeof SoupIcon> = {
  soup: SoupIcon,
  ham: HamIcon,
  beef: BeefIcon,
  fish: FishIcon,
  drumstick: DrumstickIcon,
  carrot: CarrotIcon,
  "ice-cream": IceCreamBowlIcon,
  "cup-soda": CupSodaIcon,
};

function Thumb({ item, size }: { item: MenuItemDto; size: "sm" | "lg" }) {
  const tileName = MENU_TILE_ICONS[item.categoryName.trim() as keyof typeof MENU_TILE_ICONS];
  const Icon = (tileName && TILE_ICON_COMPONENTS[tileName]) || UtensilsIcon;
  const box = size === "sm" ? "size-12 rounded-[10px]" : "size-[92px] rounded-xl";
  const glyph = size === "sm" ? "size-[22px]" : "size-[30px]";

  return (
    <span className={`flex flex-none items-center justify-center overflow-hidden bg-sunken text-ink-2 ${box}`}>
      {item.imagePath ? (
        // Owner uploads are served by the API at /api/uploads/..., so Next's optimizer has nothing to do.
        // eslint-disable-next-line @next/next/no-img-element
        <img src={item.imagePath} alt="" className="size-full object-cover" />
      ) : (
        <Icon className={glyph} strokeWidth={1.5} aria-hidden />
      )}
    </span>
  );
}

function ItemRow({
  item,
  packages,
  onEdit,
  onToggle,
  onDeleteImage,
}: {
  item: MenuItemDto;
  packages: readonly PackageDto[];
  onEdit: () => void;
  onToggle: (isAvailable: boolean) => void;
  onDeleteImage: () => void;
}) {
  const off = !item.isAvailable;
  const isBuffet = item.kind === "Buffet";

  return (
    <tr className="border-t border-border">
      <td className="h-14 px-4">
        <div className="flex items-center gap-3">
          <Thumb item={item} size="sm" />
          <b className={off ? "text-muted-foreground" : undefined}>{item.name}</b>
          {off ? <Badge variant="neutral">หมดชั่วคราว</Badge> : null}
          {!item.isActive ? <Badge variant="neutral">ปิดใช้งาน</Badge> : null}
        </div>
      </td>
      <td className="h-14 px-4">
        <Badge variant={isBuffet ? "neutral" : "accent"} className="before:hidden">
          {KIND_LABEL[item.kind]}
        </Badge>
      </td>
      <td className={`h-14 px-4 ${isBuffet ? "text-ink-2" : "text-muted-foreground"}`}>
        {minimumRankLabel(isBuffet ? item.minPackageRank : null, packages)}
      </td>
      <td className="h-14 px-4 text-right">
        {isBuffet ? (
          <span className={off ? "text-muted-foreground" : "text-ink-2"}>{BUFFET_PRICE_LABEL}</span>
        ) : (
          <span className={`font-semibold tabular-nums ${off ? "text-muted-foreground" : ""}`}>
            {formatBahtWhole(item.price)}
          </span>
        )}
      </td>
      <td className="h-14 px-4">
        <Switch
          checked={item.isAvailable}
          aria-label={`พร้อมขาย ${item.name}`}
          className={FRAME_SWITCH_CLASS}
          onCheckedChange={onToggle}
        />
      </td>
      <td className="h-14 px-4 text-right">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="ghost" size="icon-sm" aria-label={`ตัวเลือกของ ${item.name}`}>
              <EllipsisVerticalIcon aria-hidden />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={onEdit}>แก้ไขเมนู</DropdownMenuItem>
            {item.imagePath ? (
              <DropdownMenuItem variant="destructive" onSelect={onDeleteImage}>
                ลบรูป
              </DropdownMenuItem>
            ) : null}
          </DropdownMenuContent>
        </DropdownMenu>
      </td>
    </tr>
  );
}

/**
 * Frame o09/o10 draw a 44×26 switch with a 20px thumb inset 3px; the component's default track is
 * 32×18 with a 16px thumb. The size lives behind `data-[size=default]:` variants, which outrank a
 * plain utility, so these need `!`. The thumb's checked translate is `calc(100% - 2px)`, which for a
 * 20px thumb is the frame's 18px, so only the size needs overriding.
 */
const FRAME_SWITCH_CLASS = "h-[26px]! w-[44px]! p-[3px] [&_[data-slot=switch-thumb]]:size-5!";

function ItemDrawer({
  item,
  draft,
  errors,
  packages,
  categories,
  saving,
  uploading,
  onChange,
  onCancel,
  onSave,
  onUpload,
  onDeleteImage,
}: {
  item: MenuItemDto | null;
  draft: MenuItemDraft;
  errors: MenuFormErrors;
  packages: readonly PackageDto[];
  categories: readonly CategoryDto[];
  saving: boolean;
  uploading: boolean;
  onChange: (draft: MenuItemDraft) => void;
  onCancel: () => void;
  onSave: () => void;
  onUpload: (file: File) => void;
  onDeleteImage: () => void;
}) {
  const set = <K extends keyof MenuItemDraft>(key: K, value: MenuItemDraft[K]) =>
    onChange({ ...draft, [key]: value });

  const isBuffet = draft.kind === "Buffet";
  const descriptionLength = draft.description.trim().length;

  return (
    <Sheet open onOpenChange={(open) => (open ? undefined : onCancel())}>
      <SheetContent
        side="right"
        className="gap-0 p-0 data-[side=right]:w-full data-[side=right]:sm:max-w-[480px]"
        aria-describedby={undefined}
      >
        <form
          className="flex min-h-0 grow flex-col"
          onSubmit={(event) => {
            event.preventDefault();
            onSave();
          }}
        >
          <div className="flex items-start gap-3 border-b border-border px-[20px] pr-14 pt-[18px] pb-3">
            <div className="flex grow flex-col">
              <DialogTitle className="text-title">{item ? "แก้ไขเมนู" : "เพิ่มเมนู"}</DialogTitle>
              <span className="text-label font-normal text-muted-foreground">
                {item ? drawerSubtitle(item, packages) : "เมนูใหม่ · ยังไม่บันทึก"}
              </span>
            </div>
          </div>

          <div className="flex min-h-0 grow flex-col gap-2.5 overflow-y-auto px-[20px] pt-4 pb-3.5">
            <DrawerField id="menu-name" label="ชื่อเมนู" error={errors.name}>
              <Input
                id="menu-name"
                value={draft.name}
                autoComplete="off"
                aria-invalid={Boolean(errors.name)}
                onChange={(event) => set("name", event.target.value)}
              />
            </DrawerField>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="menu-category">หมวดหมู่</Label>
              <Select value={draft.categoryId} onValueChange={(value) => set("categoryId", value)}>
                <SelectTrigger
                  id="menu-category"
                  className="h-11 w-full rounded-md"
                  aria-invalid={Boolean(errors.categoryId)}
                >
                  <SelectValue placeholder="เลือกหมวดหมู่" />
                </SelectTrigger>
                <SelectContent>
                  {categories.map((category) => (
                    <SelectItem key={category.id} value={String(category.id)}>
                      {category.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FieldError error={errors.categoryId} />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>ชนิดเมนู</Label>
              <div className="inline-flex w-fit gap-0.5 rounded-lg bg-sunken p-[3px]" role="group" aria-label="ชนิดเมนู">
                {(["Buffet", "AlaCarte"] as const).map((kind) => (
                  <button
                    key={kind}
                    type="button"
                    aria-pressed={draft.kind === kind}
                    onClick={() =>
                      set("kind", kind)
                    }
                    className={`flex h-[34px] items-center rounded-[9px] px-4 text-[14px] ${
                      draft.kind === kind
                        ? "bg-surface font-semibold text-ink ring-1 ring-border"
                        : "font-medium text-ink-2"
                    }`}
                  >
                    {KIND_LABEL[kind]}
                  </button>
                ))}
              </div>
              <FieldError error={errors.kind} />
            </div>

            {isBuffet ? (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="menu-rank">แพ็กเกจขั้นต่ำ</Label>
                <Select value={draft.minPackageRank} onValueChange={(value) => set("minPackageRank", value)}>
                  <SelectTrigger
                    id="menu-rank"
                    className="h-11 w-full rounded-md"
                    aria-invalid={Boolean(errors.minPackageRank)}
                  >
                    <SelectValue placeholder="เลือกแพ็กเกจขั้นต่ำ" />
                  </SelectTrigger>
                  <SelectContent>
                    {[...packages]
                      .sort((a, b) => a.rank - b.rank)
                      .map((pkg) => (
                        <SelectItem key={pkg.id} value={String(pkg.rank)}>
                          {pkg.name} (rank {pkg.rank})
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
                <FieldError error={errors.minPackageRank} />
              </div>
            ) : null}

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="menu-price">ราคา</Label>
              {isBuffet ? (
                // Buffet items have no price of their own; the well says so and there is no box to type in.
                <div className="rounded-lg bg-sunken px-3.5 py-3 text-body text-ink-2">{BUFFET_PRICE_WELL}</div>
              ) : (
                <div className="relative">
                  <span
                    aria-hidden
                    className="pointer-events-none absolute inset-y-0 left-3.5 flex items-center text-ink"
                  >
                    ฿
                  </span>
                  <Input
                    id="menu-price"
                    className="tabular-nums pl-7"
                    inputMode="decimal"
                    value={draft.price}
                    aria-invalid={Boolean(errors.price)}
                    onChange={(event) => set("price", event.target.value)}
                  />
                </div>
              )}
              <FieldError error={errors.price} />
            </div>

            <div className="flex flex-col gap-1.5">
              <div className="flex items-center justify-between gap-2">
                <Label htmlFor="menu-description">คำอธิบาย</Label>
                <span
                  className={`font-mono text-[12px] tabular-nums ${
                    errors.description ? "text-cancel-ink" : "text-muted-foreground"
                  }`}
                >
                  {descriptionLength} / {MENU_DESCRIPTION_MAX}
                </span>
              </div>
              <Textarea
                id="menu-description"
                className="min-h-[72px]"
                value={draft.description}
                aria-invalid={Boolean(errors.description)}
                onChange={(event) => set("description", event.target.value)}
              />
              <FieldError error={errors.description} />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>รูปภาพ</Label>
              <div className="flex items-center gap-4">
                {item ? (
                  <Thumb item={item} size="lg" />
                ) : (
                  <span className="flex size-[92px] flex-none items-center justify-center rounded-xl bg-sunken text-ink-2">
                    <ImagePlusIcon className="size-[30px]" strokeWidth={1.5} aria-hidden />
                  </span>
                )}
                <div className="flex flex-col gap-2">
                  <div className="flex items-center gap-2">
                    <ImagePicker
                      disabled={item === null || uploading || saving}
                      onPick={onUpload}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={!item?.imagePath || saving}
                      onClick={onDeleteImage}
                    >
                      <Trash2Icon aria-hidden />
                      ลบรูป
                    </Button>
                  </div>
                  <span className="text-[12.5px] leading-[1.5] text-muted-foreground">
                    jpeg / png / webp ไม่เกิน 2MB · แสดงเป็นภาพสี่เหลี่ยมจัตุรัสในเมนูของลูกค้า
                  </span>
                  {item === null ? (
                    <span className="text-[12.5px] leading-[1.5] text-muted-foreground">
                      บันทึกเมนูก่อน จึงจะเปลี่ยนรูปได้
                    </span>
                  ) : null}
                </div>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-2.5">
              <div className="flex items-center justify-between gap-2.5 rounded-lg bg-sunken px-3 py-2">
                <div className="flex flex-col">
                  <span className="text-label">พร้อมขาย</span>
                  <span className="text-[12px] leading-[1.45] text-muted-foreground">ปิดชั่วคราวเมื่อของหมด</span>
                </div>
                <Switch
                  checked={draft.isAvailable}
                  aria-label="พร้อมขาย"
                  className={FRAME_SWITCH_CLASS}
                  onCheckedChange={(checked) => set("isAvailable", checked)}
                />
              </div>
              <div className="flex items-center justify-between gap-2.5 rounded-lg bg-sunken px-3 py-2">
                <div className="flex flex-col">
                  <span className="text-label">เปิดใช้งาน</span>
                  <span className="text-[12px] leading-[1.45] text-muted-foreground">ซ่อนจากเมนูทั้งหมด</span>
                </div>
                <Switch
                  checked={draft.isActive}
                  aria-label="เปิดใช้งาน"
                  className={FRAME_SWITCH_CLASS}
                  onCheckedChange={(checked) => set("isActive", checked)}
                />
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between gap-3 border-t border-border px-[20px] py-3">
            <span className="text-[12px] text-muted-foreground">บันทึกแล้วมีผลกับลูกค้าที่กำลังนั่งอยู่ทันที</span>
            <div className="flex items-center gap-2">
              <Button type="button" variant="outline" onClick={onCancel} disabled={saving}>
                ยกเลิก
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? <Loader2Icon className="animate-spin" aria-hidden /> : null}
                บันทึก
              </Button>
            </div>
          </div>

          {errors.form ? (
            <p className="border-t border-border px-[20px] py-2 text-[12.5px] text-cancel-ink">{errors.form}</p>
          ) : null}
        </form>
      </SheetContent>
    </Sheet>
  );
}

function ImagePicker({
  disabled,
  onPick,
}: {
  disabled: boolean;
  onPick: (file: File) => void;
}) {
  const [inputKey, setInputKey] = useState(0);
  return (
    <>
      <input
        key={inputKey}
        id="menu-image"
        type="file"
        accept="image/jpeg,image/png,image/webp"
        className="sr-only"
        onChange={(event) => {
          const file = event.target.files?.[0];
          // Reset so picking the same file again after a rejection still fires.
          setInputKey((key) => key + 1);
          if (!file) return;
          // The browser already filters by type; the size rule needs this check.
          const rejection = menuImageRejection(file);
          if (rejection) {
            toast.error(rejection);
            return;
          }
          onPick(file);
        }}
      />
      <Button type="button" variant="outline" size="sm" disabled={disabled} asChild>
        <label htmlFor="menu-image" className={disabled ? "pointer-events-none" : "cursor-pointer"}>
          <ImagePlusIcon aria-hidden />
          เปลี่ยนรูป
        </label>
      </Button>
    </>
  );
}

function DrawerField({
  id,
  label,
  error,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
      <FieldError error={error} />
    </div>
  );
}

function FieldError({ error }: { error?: string }) {
  if (!error) return null;
  return <p className="text-[12.5px] leading-[1.5] text-cancel-ink">{error}</p>;
}

/** Frame `o09-menu`'s “จัดการหมวดหมู่”: rename rows, delete the empty ones, add new ones. */
function CategoryManager({ onClose }: { onClose: () => void }) {
  const categories = useCategories();
  const create = useCreateCategory();
  const update = useUpdateCategory();
  const remove = useDeleteCategory();

  const [newName, setNewName] = useState("");
  const [renames, setRenames] = useState<Record<number, string>>({});
  const [rowError, setRowError] = useState<{ id: number; message: string } | null>(null);
  const [newError, setNewError] = useState<string | null>(null);

  const rows = categories.data ?? [];
  const readError = (error: unknown, fallback: string) =>
    error instanceof ApiError ? error.message : fallback;

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent className="gap-4 sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>จัดการหมวดหมู่</DialogTitle>
          <DialogDescription>
            ลบได้เฉพาะหมวดที่ว่าง · เมนูในหมวดยังอยู่ครบเมื่อเปลี่ยนชื่อหมวด
          </DialogDescription>
        </DialogHeader>

        <div className="flex max-h-[50vh] flex-col gap-2 overflow-y-auto">
          {rows.map((category) => {
            const name = renames[category.id] ?? category.name;
            const dirty = name.trim() !== category.name;
            return (
              <div key={category.id} className="flex flex-col gap-1.5">
                <div className="flex items-center gap-2">
                  <Input
                    value={name}
                    aria-label={`ชื่อหมวด ${category.name}`}
                    aria-invalid={rowError?.id === category.id}
                    onChange={(event) => setRenames((current) => ({ ...current, [category.id]: event.target.value }))}
                  />
                  <span className="w-20 flex-none text-[13px] text-muted-foreground tabular-nums">
                    {category.itemCount} เมนู
                  </span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`บันทึกชื่อหมวด ${category.name}`}
                    disabled={!dirty || category.name.trim().length === 0 || update.isPending}
                    onClick={() =>
                      update.mutate(
                        { id: category.id, input: { name: name.trim() } },
                        {
                          onSuccess: () => {
                            setRowError(null);
                            setRenames((current) => {
                              const next = { ...current };
                              delete next[category.id];
                              return next;
                            });
                          },
                          onError: (error) =>
                            setRowError({ id: category.id, message: readError(error, "บันทึกไม่สำเร็จ ลองอีกครั้ง") }),
                        },
                      )
                    }
                  >
                    <CheckIcon />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`ลบหมวด ${category.name}`}
                    disabled={remove.isPending}
                    onClick={() =>
                      remove.mutate(category.id, {
                        onSuccess: () => setRowError(null),
                        onError: (error) =>
                          setRowError({ id: category.id, message: readError(error, "ลบไม่สำเร็จ ลองอีกครั้ง") }),
                      })
                    }
                  >
                    <Trash2Icon aria-hidden />
                  </Button>
                </div>
                {rowError?.id === category.id ? (
                  <p className="text-[12.5px] leading-[1.5] text-cancel-ink">{rowError.message}</p>
                ) : null}
              </div>
            );
          })}
        </div>

        <div className="flex flex-col gap-1.5 border-t border-border pt-4">
          <div className="flex items-center gap-2">
            <Input
              value={newName}
              aria-label="ชื่อหมวดใหม่"
              placeholder="ชื่อหมวดใหม่"
              aria-invalid={Boolean(newError)}
              onChange={(event) => setNewName(event.target.value)}
            />
            <Button
              type="button"
              disabled={newName.trim().length === 0 || create.isPending}
              onClick={() =>
                create.mutate(
                  { name: newName.trim() },
                  {
                    onSuccess: () => {
                      setNewName("");
                      setNewError(null);
                    },
                    onError: (error) => setNewError(readError(error, "เพิ่มหมวดไม่สำเร็จ ลองอีกครั้ง")),
                  },
                )
              }
            >
              <PlusIcon aria-hidden />
              เพิ่มหมวด
            </Button>
          </div>
          {newError ? <p className="text-[12.5px] leading-[1.5] text-cancel-ink">{newError}</p> : null}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            เสร็จสิ้น
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
