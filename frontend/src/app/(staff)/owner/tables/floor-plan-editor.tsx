"use client";

import {
  ArmchairIcon,
  ArrowLeftIcon,
  ChefHatIcon,
  DoorOpenIcon,
  Loader2Icon,
  MinusIcon,
  PlusIcon,
  Trash2Icon,
  WalletIcon,
  XIcon,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api";
import { useSaveFloorPlan, useTables } from "@/lib/tables";
import type { TableDto } from "@/lib/types";

/** Same bounds the API enforces; the stepper cannot leave them, so the screen cannot build an invalid seat count. */
const SEAT_MIN = 1;
const SEAT_MAX = 20;
/** Seat pips stop at 8 so a 20-seat table does not turn its tile into a dot matrix. */
const PIP_MAX = 8;

/** One editable table. `id === null` means "added on this screen, not saved yet"; `key` identifies
 * the row for React and for selection, which `id` cannot do for rows that do not exist yet. */
interface DraftTable {
  key: string;
  id: number | null;
  name: string;
  seats: number;
  zone: string | null;
  sortOrder: number;
  isActive: boolean;
}

/** Row identity for tables that do not exist on the server yet. A counter beats `crypto.randomUUID`,
 * which needs a secure context — the demo may be opened over plain HTTP on a LAN address. */
let nextAddedKey = 0;

const toDraft = (t: TableDto): DraftTable => ({
  key: `t${t.id}`,
  id: t.id,
  name: t.name,
  seats: t.seats,
  zone: t.zone,
  sortOrder: t.sortOrder,
  isActive: t.isActive,
});

const clampSeats = (seats: number) => Math.min(SEAT_MAX, Math.max(SEAT_MIN, seats));

export function FloorPlanEditor({
  onClose,
  addPending = false,
  occupiedTableIds,
}: {
  onClose: () => void;
  /** Arrives from the floor's เพิ่มโต๊ะ: put a new tile on the plan straight away. */
  addPending?: boolean;
  /** Table ids that hold an open round, from `GET /api/tables/overview` — the floor's only source. */
  occupiedTableIds: ReadonlySet<number>;
}) {
  const { data, isPending, isError, error } = useTables();
  const save = useSaveFloorPlan();

  // `draft === null` means "show exactly what the server says". The first edit materialises a copy;
  // a successful save (or ยกเลิก) drops it again, so the screen always re-reads the server's truth.
  const [draft, setDraft] = useState<DraftTable[] | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const consumed = useRef(false);

  const serverTables = data ?? [];
  const rows = draft ?? serverTables.map(toDraft);
  const selected = rows.find((row) => row.key === selectedKey) ?? rows[0] ?? null;

  const edit = (row: DraftTable, changes: Partial<DraftTable>) => {
    setDraft((current) => {
      const base = current ?? serverTables.map(toDraft);
      return base.map((r) => (r.key === row.key ? { ...r, ...changes } : r));
    });
  };

  /** A new row appended last, where the dashed "เพิ่มโต๊ะ" slot sits in the frame. */
  const appendRow = (base: DraftTable[], key: string): DraftTable[] => {
    const nextOrder = base.reduce((max, r) => Math.max(max, r.sortOrder), 0) + 1;
    const taken = new Set(base.map((r) => r.name.trim().toLowerCase()));
    let suffix = base.length + 1;
    while (taken.has(`ใหม่${suffix}`.toLowerCase())) suffix += 1;
    return [
      ...base,
      { key, id: null, name: `ใหม่${suffix}`, seats: 4, zone: null, sortOrder: nextOrder, isActive: true },
    ];
  };

  const addTable = () => {
    const key = `new-${++nextAddedKey}`;
    setDraft((current) => appendRow(current ?? serverTables.map(toDraft), key));
    setSelectedKey(key);
  };

  // The floor's เพิ่มโต๊ะ button opens this screen; `consumed` makes it a one-shot so it never fights
  // the owner's later edits.
  useEffect(() => {
    if (!addPending || isPending || consumed.current) return;
    consumed.current = true;
    const key = `new-${++nextAddedKey}`;
    setDraft((current) => appendRow(current ?? serverTables.map(toDraft), key));
    setSelectedKey(key);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one-shot on arrival from the floor
  }, [addPending, isPending]);

  const dirty = draft !== null;
  const nameCounts = new Map<string, number>();
  for (const row of rows) {
    const key = row.name.trim().toLowerCase();
    nameCounts.set(key, (nameCounts.get(key) ?? 0) + 1);
  }
  const nameTaken = (name: string) => (nameCounts.get(name.trim().toLowerCase()) ?? 0) > 1;
  const nameUsable = (name: string) => name.trim().length > 0 && !nameTaken(name);
  const canSave = dirty && rows.every((row) => nameUsable(row.name)) && !save.isPending;

  const active = rows.filter((row) => row.isActive);
  const totalSeats = active.reduce((sum, row) => sum + row.seats, 0);
  // Which tables hold guests is known only to the overview endpoint (this ticket); the × on a tile
  // and the delete button in the panel are for free tables only. The API is still the authority that
  // refuses to disable an occupied table (TABLE_OCCUPIED) — the screen just stops offering it.
  const isOccupied = (row: DraftTable) => row.id !== null && occupiedTableIds.has(row.id);
  const inUseCount = active.filter(isOccupied).length;

  // Zones keep first-appearance order in the floor, the way the frame reads left to right.
  const zoneGroups: { zone: string | null; rows: DraftTable[] }[] = [];
  for (const row of [...rows].sort((a, b) => a.sortOrder - b.sortOrder)) {
    const group = zoneGroups.find((g) => g.zone === row.zone);
    if (group) group.rows.push(row);
    else zoneGroups.push({ zone: row.zone, rows: [row] });
  }

  const reset = () => {
    setDraft(null);
    setSelectedKey(null);
  };

  const submit = () => {
    const byId = new Map(serverTables.map((t) => [t.id, t]));
    const create = rows
      .filter((row) => row.id === null)
      .map((row) => ({ name: row.name.trim(), seats: row.seats, zone: row.zone }));
    const update = rows
      .filter((row): row is DraftTable & { id: number } => row.id !== null)
      .filter((row) => {
        const before = byId.get(row.id)!;
        return (
          row.name.trim() !== before.name ||
          row.seats !== before.seats ||
          row.zone !== before.zone ||
          row.isActive !== before.isActive
        );
      })
      .map((row) => ({ id: row.id, name: row.name.trim(), seats: row.seats, zone: row.zone, isActive: row.isActive }));

    save.mutate(
      { create, update },
      {
        onSuccess: () => {
          reset();
          toast.success("บันทึกผังเรียบร้อย");
          onClose();
        },
        // The draft stays put on failure: a rejected save must not throw away what the owner typed.
        onError: (cause) =>
          toast.error(cause instanceof ApiError ? cause.message : "ทำรายการไม่สำเร็จ ลองอีกครั้ง"),
      },
    );
  };

  if (isPending) {
    return (
      <div className="flex min-h-40 items-center justify-center gap-2 text-label text-muted-foreground">
        <Loader2Icon className="size-4 animate-spin" aria-hidden />
        กำลังโหลดผังโต๊ะ
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex flex-col items-start gap-3">
        <h1 className="text-headline">โต๊ะ</h1>
        <p className="text-body text-muted-foreground">
          {error instanceof ApiError ? error.message : "โหลดผังโต๊ะไม่ได้ ลองอีกครั้ง"}
        </p>
        <Button variant="outline" onClick={() => window.location.reload()}>
          ลองอีกครั้ง
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      <div className="flex min-h-20 flex-wrap items-center justify-between gap-3 py-4 lg:py-5">
        <div className="flex flex-col gap-0.5">
          <h1 className="text-headline">โต๊ะ</h1>
          <span className="text-label font-normal text-muted-foreground">
            โหมดแก้ไขผัง · เพิ่ม ลบ โต๊ะ และตั้งจำนวนที่นั่ง
          </span>
        </div>
        <div className="flex items-center gap-3">
          <Button variant="ghost" disabled={save.isPending} onClick={onClose}>
            <ArrowLeftIcon aria-hidden />
            กลับไปผัง
          </Button>
          <Button variant="outline" disabled={!dirty || save.isPending} onClick={reset}>
            ยกเลิก
          </Button>
          <Button disabled={!canSave} onClick={submit}>
            {save.isPending ? (
              <Loader2Icon className="size-4 animate-spin" aria-hidden />
            ) : (
              <ArmchairIcon className="size-4" aria-hidden />
            )}
            บันทึกผัง
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2.5">
        <LegendChip label="ใช้งานอยู่" count={inUseCount} tone="use" />
        <LegendChip label="ว่าง" count={active.length - inUseCount} tone="free" />
        <span className="ms-auto text-label font-normal text-muted-foreground">
          รวม <b className="tabular-nums">{active.length}</b> โต๊ะ · <b className="tabular-nums">{totalSeats}</b> ที่นั่ง
        </span>
      </div>

      <div className="mt-4 flex flex-col gap-6 xl:grid xl:grid-cols-[minmax(0,800px)_312px] xl:items-start">
        <div className="min-w-0">
          <div className="flex flex-col gap-5 overflow-x-auto rounded-2xl border border-border bg-surface p-5 xl:min-h-[690px]">
            <div className="flex items-center gap-2 rounded-sm border-b border-border bg-sunken px-3.5 py-2 text-ink-2">
              <ChefHatIcon className="size-4" strokeWidth={1.75} aria-hidden />
              <b className="text-label">ครัว</b>
            </div>

            <div className="flex flex-wrap items-start gap-5">
              {zoneGroups.map((group) => (
                <div key={group.zone ?? "__none"} className="flex flex-col gap-2.5">
                  <div className="text-label text-ink-2">
                    {group.zone ? `โซน ${group.zone}` : "ไม่ระบุโซน"}
                  </div>
                  <div className="grid grid-flow-col grid-rows-3 gap-5">
                    {group.rows.map((row) => (
                      <TableTile
                        key={row.key}
                        row={row}
                        selected={selected !== null && row.key === selected.key}
                        nameTaken={nameTaken(row.name)}
                        isOccupied={isOccupied(row)}
                        onSelect={() => setSelectedKey(row.key)}
                        onSeats={(seats) => edit(row, { seats })}
                        onDisable={() => edit(row, { isActive: false })}
                      />
                    ))}
                  </div>
                </div>
              ))}

              <div className="grid grid-flow-col grid-rows-3 gap-5">
                <button
                  type="button"
                  onClick={addTable}
                  className="flex h-[130px] w-32 flex-col items-center justify-center gap-1 rounded-lg border-[1.5px] border-dashed border-control-border text-ink-2 hover:bg-sunken"
                >
                  <PlusIcon className="size-6" strokeWidth={1.75} aria-hidden />
                  <b className="text-label">เพิ่มโต๊ะ</b>
                  <span className="text-xs text-muted-foreground">วางในผัง</span>
                </button>
              </div>
            </div>

            <div className="mt-auto flex flex-wrap items-end justify-between gap-4 pt-6">
              <span className="flex flex-col gap-1 text-xs text-muted-foreground">
                <span className="h-4 border-s border-line-strong ps-2">หน้าต่าง</span>
              </span>
              <span className="flex items-center gap-1.5 text-label font-normal text-muted-foreground">
                <DoorOpenIcon className="size-4" strokeWidth={1.75} aria-hidden />
                ทางเข้า
              </span>
              <div className="flex flex-col justify-center gap-0.5 rounded-sm border border-line-strong px-3.5 py-3 text-ink-2">
                <span className="flex items-center gap-2">
                  <WalletIcon className="size-4" strokeWidth={1.75} aria-hidden />
                  <b className="text-label">แคชเชอร์</b>
                </span>
                <span className="text-xs text-muted-foreground">รับชำระเงิน · ออกใบเสร็จ</span>
              </div>
            </div>
          </div>

          <div className="mt-2.5 text-label font-normal text-muted-foreground">
            ลบได้เฉพาะโต๊ะที่ว่าง
          </div>
        </div>

        <aside className="flex w-full flex-col rounded-2xl border border-border bg-surface p-4 xl:w-[312px]">
          {selected === null ? (
            <>
              <h2 className="text-title">ยังไม่มีโต๊ะ</h2>
              <span className="text-xs text-muted-foreground">กดเพิ่มโต๊ะเพื่อเริ่มวางผังร้าน</span>
            </>
          ) : (
            <>
              <div className="flex items-center justify-between gap-3">
                <h2 className="text-title">ตั้งค่าโต๊ะ {selected.name || "ใหม่"}</h2>
                <span
                  className={`inline-flex h-6 items-center gap-1.5 rounded-full px-2.5 text-[12.5px] font-semibold ${
                    !selected.isActive
                      ? "bg-cancel-soft text-cancel-ink"
                      : isOccupied(selected)
                        ? "bg-orange-soft text-orange-ink"
                        : "bg-sunken text-ink-2"
                  }`}
                >
                  <i
                    className={`size-1.5 rounded-full ${
                      !selected.isActive
                        ? "bg-cancel-dot"
                        : isOccupied(selected)
                          ? "bg-orange"
                          : "bg-control-border"
                    }`}
                  />
                  {!selected.isActive ? "ปิดใช้งาน" : isOccupied(selected) ? "ใช้งานอยู่" : "ว่าง"}
                </span>
              </div>

              <div className="mt-4 flex flex-col gap-4">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="table-name">ชื่อโต๊ะ</Label>
                  <Input
                    id="table-name"
                    value={selected.name}
                    maxLength={20}
                    aria-invalid={!nameUsable(selected.name)}
                    onChange={(event) => edit(selected, { name: event.target.value })}
                    className="font-mono"
                  />
                  <span
                    className={`text-xs ${nameTaken(selected.name) ? "text-cancel-ink" : "text-muted-foreground"}`}
                  >
                    ชื่อซ้ำกับโต๊ะอื่นไม่ได้
                  </span>
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label>จำนวนคนต่อโต๊ะ (ที่นั่ง)</Label>
                  <div className="flex items-center gap-3">
                    <Stepper
                      size="lg"
                      seats={selected.seats}
                      label={selected.name || "ใหม่"}
                      onSeats={(seats) => edit(selected, { seats })}
                    />
                    <span className="text-label font-normal text-muted-foreground">คน</span>
                  </div>
                  <span className="text-xs text-muted-foreground">
                    ตั้งได้ 1–20 ที่นั่ง · ลดต่ำกว่าจำนวนลูกค้าที่นั่งอยู่ไม่ได้
                  </span>
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="table-zone">โซน</Label>
                  <Input
                    id="table-zone"
                    list="fq-known-zones"
                    value={selected.zone ?? ""}
                    maxLength={20}
                    placeholder="โซน A"
                    onChange={(event) => edit(selected, { zone: event.target.value.trim() || null })}
                  />
                  <datalist id="fq-known-zones">
                    {Array.from(new Set(serverTables.map((t) => t.zone).filter((z): z is string => !!z))).map((zone) => (
                      <option key={zone} value={zone} />
                    ))}
                  </datalist>
                </div>
              </div>

              <hr className="my-4 border-border" />

              {selected.isActive ? (
                <>
                  <Button
                    variant="destructive"
                    className="w-full"
                    disabled={isOccupied(selected)}
                    onClick={() => edit(selected, { isActive: false })}
                  >
                    <Trash2Icon className="size-4" aria-hidden />
                    ลบโต๊ะ {selected.name || "ใหม่"}
                  </Button>
                  <span className="mt-2 text-xs text-muted-foreground">
                    {isOccupied(selected)
                      ? "โต๊ะนี้มีลูกค้านั่งอยู่ · ลบไม่ได้จนกว่าจะปิดบิล"
                      : "ลบได้เฉพาะโต๊ะที่ว่าง · โต๊ะที่มีลูกค้านั่งอยู่จะลบไม่ได้"}
                  </span>
                </>
              ) : (
                <>
                  <Button
                    variant="outline"
                    className="w-full"
                    onClick={() => edit(selected, { isActive: true })}
                  >
                    <PlusIcon className="size-4" aria-hidden />
                    เปิดใช้งาน
                  </Button>
                  <span className="mt-2 text-xs text-muted-foreground">
                    โต๊ะนี้ถูกปิดใช้งานอยู่ · เปิดใช้งานเพื่อให้กลับมาอยู่ในผัง
                  </span>
                </>
              )}
            </>
          )}
        </aside>
      </div>
    </div>
  );
}

function Stepper({
  seats,
  onSeats,
  size = "sm",
  label,
}: {
  seats: number;
  onSeats: (seats: number) => void;
  size?: "sm" | "lg";
  label: string;
}) {
  const circle = size === "lg" ? "size-11" : "size-7";
  const glyph = size === "lg" ? "size-4.5" : "size-3.5";
  return (
    <span className={size === "lg" ? "flex items-center gap-3" : "flex items-center gap-1.5"}>
      <button
        type="button"
        aria-label={`ลดจำนวนที่นั่งของโต๊ะ ${label}`}
        disabled={seats <= SEAT_MIN}
        onClick={() => onSeats(clampSeats(seats - 1))}
        className={`inline-flex items-center justify-center rounded-full border border-line-strong bg-surface text-ink disabled:border-border disabled:bg-sunken disabled:text-muted-foreground ${circle}`}
      >
        <MinusIcon className={glyph} strokeWidth={2} aria-hidden />
      </button>
      <b
        className={`text-center font-semibold tabular-nums ${
          size === "lg" ? "min-w-11 text-[28px] leading-none" : "min-w-4 text-base"
        }`}
      >
        {seats}
      </b>
      <button
        type="button"
        aria-label={`เพิ่มจำนวนที่นั่งของโต๊ะ ${label}`}
        disabled={seats >= SEAT_MAX}
        onClick={() => onSeats(clampSeats(seats + 1))}
        className={`inline-flex items-center justify-center rounded-full border border-line-strong bg-surface text-ink disabled:border-border disabled:bg-sunken disabled:text-muted-foreground ${circle}`}
      >
        <PlusIcon className={glyph} strokeWidth={2} aria-hidden />
      </button>
    </span>
  );
}

function LegendChip({ label, count, tone }: { label: string; count: number; tone: "use" | "free" }) {
  return (
    <span className="inline-flex h-11 items-center gap-2.5 rounded-lg border border-border bg-surface ps-2 pe-4">
      <span
        className={`inline-flex size-9 items-center justify-center rounded-sm ${
          tone === "use" ? "border border-orange-line bg-orange-soft" : "border border-border bg-sunken"
        }`}
      >
        <i className={`size-1.5 rounded-full ${tone === "use" ? "bg-orange" : "bg-control-border"}`} />
      </span>
      <span className={`text-sm font-semibold ${tone === "use" ? "text-orange-ink" : "text-ink-2"}`}>{label}</span>
      <b className="text-title tabular-nums">{count}</b>
    </span>
  );
}

function TableTile({
  row,
  selected,
  nameTaken,
  isOccupied,
  onSelect,
  onSeats,
  onDisable,
}: {
  row: DraftTable;
  selected: boolean;
  nameTaken: boolean;
  /** Holds an open round: it keeps its in-use tone, gets no ×, and cannot be deleted from the panel. */
  isOccupied: boolean;
  onSelect: () => void;
  onSeats: (seats: number) => void;
  onDisable: () => void;
}) {
  const shownPips = Math.min(row.seats, PIP_MAX);
  return (
    <div
      className={`relative flex w-32 flex-col justify-between rounded-xl border p-3.5 ${
        row.seats >= 6 ? "h-[136px] rounded-2xl" : "h-[130px]"
      } ${
        row.isActive
          ? isOccupied
            ? `border-orange-line bg-orange-soft text-orange-ink ${
                nameTaken ? "border-cancel-line bg-cancel-soft" : ""
              }`
            : `border-border bg-sunken text-ink-2 ${nameTaken ? "border-cancel-line bg-cancel-soft" : ""}`
          : "border-dashed border-control-border bg-surface text-muted-foreground"
      } ${selected ? "outline-2 outline-offset-[10px] outline-ink" : ""}`}
    >
      {row.isActive && !isOccupied && (
        <button
          type="button"
          aria-label={`ลบโต๊ะ ${row.name}`}
          onClick={onDisable}
          className="absolute -top-2.5 -left-2.5 flex size-6 items-center justify-center rounded-full border border-control-border bg-surface text-ink-2 hover:bg-sunken"
        >
          <XIcon className="size-3.5" aria-hidden />
        </button>
      )}

      <div className="flex items-start justify-between gap-2">
        <button
          type="button"
          onClick={onSelect}
          aria-current={selected ? "true" : undefined}
          className="text-[26px] leading-tight font-semibold tracking-tight"
        >
          {row.name}
        </button>
        <span className="flex items-center gap-1 pt-2" aria-hidden>
          {Array.from({ length: shownPips }, (_, i) => (
            <i
              key={i}
              className={`size-1.5 rounded-full ${
                isOccupied ? "border border-orange-line" : "border border-control-border"
              }`}
            />
          ))}
          {row.seats > PIP_MAX && <span className="text-xs">+{row.seats - PIP_MAX}</span>}
        </span>
      </div>

      <Stepper seats={row.seats} label={row.name} onSeats={onSeats} />

      <span className="inline-flex items-center gap-1.5 text-label">
        <i
          className={`size-1.5 rounded-full ${
            !row.isActive ? "bg-cancel-dot" : isOccupied ? "bg-orange" : "bg-control-border"
          }`}
        />
        {!row.isActive ? "ปิดใช้งาน" : isOccupied ? "ใช้งานอยู่" : "ว่าง"}
      </span>
    </div>
  );
}
