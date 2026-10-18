"use client";

import {
  BellIcon,
  ChefHatIcon,
  DoorOpenIcon,
  PencilIcon,
  PlusIcon,
  UsersIcon,
  WalletIcon,
} from "lucide-react";
import { useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { TimeRail } from "@/components/time-rail";
import { clockRange, formatBaht, roundTime } from "@/lib/format";
import { occupied, useTableOverview } from "@/lib/overview";
import type { SessionDetailDto, TableOverviewDto } from "@/lib/types";
import { useSettings } from "../settings/settings-api";
import { OpenTableDialog, QrResultDialog } from "../open-table-flow";
import { FloorPlanEditor } from "./floor-plan-editor";

/**
 * `/owner/tables` — frame `o12-tables` (the live floor) and frame `o22-tables-edit` (ticket 06's edit
 * mode, untouched and reachable through แก้ไขผัง).
 *
 * Two states only, exactly as the design demands: ใช้งานอยู่ (Warm Tint with the Tint Line) and ว่าง
 * (Sunken grey). Nothing blinks and nothing pulses — a table with an unstarted order keeps its in-use
 * colour and gains the bell line "ออเดอร์รอรับ". Selecting a tile fills the side panel; a free tile
 * offers เปิดโต๊ะ through the same dialog the overview uses.
 */
export function FloorMapScreen() {
  const [isEditing, setIsEditing] = useState(false);
  const [addPending, setAddPending] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [openTable, setOpenTable] = useState<{ id: number; name: string; previewStart: number } | null>(null);
  const [created, setCreated] = useState<SessionDetailDto | null>(null);

  const overview = useTableOverview();
  const settings = useSettings();
  const cards = overview.data ?? [];
  const lastOrderWarningMinutes = settings.data?.lastOrderWarningMinutes ?? 0;
  // One source for "which table holds guests": the same answer drives the live floor and the edit
  // mode's delete rules, so the two modes can never disagree.
  const occupiedIds = new Set(
    cards.filter((card) => card.sessionId != null).map((card) => card.id),
  );

  if (isEditing) {
    return (
      <FloorPlanEditor
        onClose={() => {
          setAddPending(false);
          setIsEditing(false);
        }}
        addPending={addPending}
        occupiedTableIds={occupiedIds}
      />
    );
  }

  const selected = cards.find((card) => card.id === selectedId) ?? cards[0] ?? null;
  const inUse = cards.filter((card) => card.sessionId != null).length;
  const totalSeats = cards.reduce((sum, card) => sum + card.seats, 0);

  return (
    <div className="flex flex-col">
      <div className="flex min-h-20 flex-wrap items-center justify-between gap-3 py-4 lg:py-5">
        <div className="flex flex-col gap-0.5">
          <h1 className="text-headline">โต๊ะ</h1>
          <span className="text-label font-normal text-muted-foreground">
            ผังร้านและสถานะโต๊ะ · อัปเดตเรียลไทม์
          </span>
        </div>
        <div className="flex items-center gap-3">
          <Button variant="secondary" onClick={() => setIsEditing(true)}>
            <PencilIcon aria-hidden />
            แก้ไขผัง
          </Button>
          {/* Adding a table is edit mode's job (frame `o22`), so this enters it with a new row ready
              to place rather than pretending a live floor can grow a tile. */}
          <Button
            disabled={cards.length === 0}
            onClick={() => {
              setAddPending(true);
              setIsEditing(true);
            }}
          >
            <PlusIcon aria-hidden />
            เพิ่มโต๊ะ
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2.5">
        <LegendChip label="ใช้งานอยู่" count={inUse} tone="use" />
        <LegendChip label="ว่าง" count={cards.length - inUse} tone="free" />
        <span className="ms-auto text-label font-normal text-muted-foreground">
          รวม <b className="tabular-nums">{cards.length}</b> โต๊ะ ·{" "}
          <b className="tabular-nums">{totalSeats}</b> ที่นั่ง
        </span>
      </div>

      <div className="mt-4 flex flex-col gap-6 xl:grid xl:grid-cols-[minmax(0,800px)_312px] xl:items-start">
        <div className="min-w-0">
          {overview.isPending ? (
            <div className="flex min-h-[400px] items-center justify-center rounded-2xl border border-border bg-surface text-label text-muted-foreground">
              กำลังโหลดผังโต๊ะ
            </div>
          ) : (
            <div className="flex flex-col gap-4 overflow-x-auto rounded-2xl border border-border bg-surface p-5 xl:min-h-[690px]">
              <div className="flex items-center gap-2 rounded-sm border-b border-border bg-sunken px-3.5 py-2 text-ink-2">
                <ChefHatIcon className="size-4" strokeWidth={1.75} aria-hidden />
                <b className="text-label">ครัว</b>
              </div>

              <ZoneGroups
                cards={cards}
                selectedId={selected?.id ?? null}
                onSelect={setSelectedId}
              />

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
          )}

          <div className="mt-2.5 text-label font-normal text-muted-foreground">
            เพิ่ม ลบ และตั้งจำนวนที่นั่งของโต๊ะได้ในโหมดแก้ไขผัง
          </div>
        </div>

        <aside className="flex w-full flex-col rounded-2xl border border-border bg-surface p-4 xl:w-[312px]">
          {selected === null ? (
            <>
              <h2 className="text-title">ยังไม่มีโต๊ะ</h2>
              <span className="text-xs text-muted-foreground">เพิ่มโต๊ะในโหมดแก้ไขผังก่อน</span>
            </>
          ) : (
            <TablePanel
              card={selected}
              lastOrderWarningMinutes={lastOrderWarningMinutes}
              onOpenTable={() =>
                setOpenTable({ id: selected.id, name: selected.name, previewStart: Date.now() })
              }
            />
          )}
        </aside>
      </div>

      {openTable ? (
        <OpenTableDialog
          tableId={openTable.id}
          tableName={openTable.name}
          previewStart={openTable.previewStart}
          onCancel={() => setOpenTable(null)}
          onDone={(session) => {
            setOpenTable(null);
            setCreated(session);
            void overview.refetch();
          }}
        />
      ) : null}

      {created ? <QrResultDialog session={created} onClose={() => setCreated(null)} /> : null}
    </div>
  );
}

/** Zones keep first-appearance order, which is the floor's own order from the API. */
function ZoneGroups({
  cards,
  selectedId,
  onSelect,
}: {
  cards: TableOverviewDto[];
  selectedId: number | null;
  onSelect: (id: number) => void;
}) {
  const groups: { zone: string | null; rows: TableOverviewDto[] }[] = [];
  for (const card of cards) {
    const group = groups.find((g) => g.zone === card.zone);
    if (group) group.rows.push(card);
    else groups.push({ zone: card.zone, rows: [card] });
  }

  return (
    <div className="flex flex-wrap items-start gap-5">
      {groups.map((group) => (
        <div key={group.zone ?? "__none"} className="flex flex-col gap-2.5">
          <div className="text-label text-ink-2">{group.zone ? `โซน ${group.zone}` : "ไม่ระบุโซน"}</div>
          <div className="grid grid-flow-col grid-rows-3 gap-5">
            {group.rows.map((card) => (
              <FloorTile
                key={card.id}
                card={card}
                selected={card.id === selectedId}
                onSelect={() => onSelect(card.id)}
              />
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * One tile. Two states, each with its Thai label and its own dot tone — the colour only reinforces the
 * label, and ใช้งานอยู่ is decided by the presence of an open round, never by order status.
 */
function FloorTile({
  card,
  selected,
  onSelect,
}: {
  card: TableOverviewDto;
  selected: boolean;
  onSelect: () => void;
}) {
  const session = occupied(card);
  const shownPips = Math.min(card.seats, 8);

  return (
    <button
      type="button"
      onClick={onSelect}
      aria-current={selected ? "true" : undefined}
      className={`flex h-[130px] w-32 flex-col justify-between rounded-xl border p-3.5 text-left ${
        card.seats >= 6 ? "rounded-2xl" : ""
      } ${
        session
          ? "border-orange-line bg-orange-soft text-orange-ink"
          : "border-border bg-sunken text-ink-2"
      } ${selected ? "outline-2 outline-offset-[10px] outline-ink" : ""}`}
    >
      <span className="flex items-start justify-between gap-2">
        <b className="text-[26px] leading-tight font-semibold tracking-tight">{card.name}</b>
        <span className="flex items-center gap-1 pt-2" aria-hidden>
          {Array.from({ length: shownPips }, (_, i) => (
            <i
              key={i}
              className={`size-1.5 rounded-full ${
                session
                  ? i < session.adults + session.children
                    ? "bg-orange"
                    : "border border-orange-line"
                  : "border border-control-border"
              }`}
            />
          ))}
        </span>
      </span>

      <span className="flex items-center gap-1.5 text-label font-medium">
        <UsersIcon className="size-4" strokeWidth={1.75} aria-hidden />
        <span className="tabular-nums">
          {session ? `${session.adults + session.children}/${card.seats}` : `${card.seats} ที่นั่ง`}
        </span>
      </span>

      <span className="flex items-center gap-1.5 text-label font-semibold">
        <i className={`size-1.5 flex-none rounded-full ${session ? "bg-orange" : "bg-control-border"}`} />
        {session ? "ใช้งานอยู่" : "ว่าง"}
      </span>

      {session && session.ordersWaiting > 0 ? (
        <span className="flex items-center gap-1.5 text-label font-semibold">
          <BellIcon className="size-4" strokeWidth={1.75} aria-hidden />
          ออเดอร์รอรับ
        </span>
      ) : null}
    </button>
  );
}

/** Frame `o12-tables` side panel: the facts of the selected table, or the เปิดโต๊ะ action when free. */
function TablePanel({
  card,
  lastOrderWarningMinutes,
  onOpenTable,
}: {
  card: TableOverviewDto;
  lastOrderWarningMinutes: number;
  onOpenTable: () => void;
}) {
  const session = occupied(card);

  if (!session) {
    return (
      <>
        <div className="flex items-center justify-between gap-3">
          <h2 className="text-title">โต๊ะ {card.name}</h2>
          <Badge variant="neutral" className="before:hidden">
            ว่าง
          </Badge>
        </div>
        <dl className="mt-4 flex flex-col gap-2.5 text-label">
          <Fact label="ตำแหน่ง" value={card.zone ? `โซน ${card.zone}` : "ไม่ระบุโซน"} />
          <Fact label="ที่นั่ง" value={`${card.seats} ที่นั่ง`} />
        </dl>
        <Button className="mt-4 w-full" onClick={onOpenTable}>
          <PlusIcon aria-hidden />
          เปิดโต๊ะ
        </Button>
      </>
    );
  }

  const time = roundTime(session.minutesUsed, session.openedAt, session.endsAt, lastOrderWarningMinutes);

  return (
    <>
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-title">โต๊ะ {card.name}</h2>
        {time.isOver ? (
          <Badge variant="cancelled" className="before:hidden">
            ปิดการสั่งแล้ว
          </Badge>
        ) : (
          <Badge variant="accent" className="before:hidden">
            ใช้งานอยู่
          </Badge>
        )}
      </div>

      <dl className="mt-4 flex flex-col gap-1 text-label">
        <Fact label="ตำแหน่ง" value={card.zone ? `โซน ${card.zone}` : "ไม่ระบุโซน"} />
        <Fact
          label="ผู้นั่ง"
          value={`${session.adults} ผู้ใหญ่${session.children > 0 ? ` ${session.children} เด็ก` : ""} (${session.adults + session.children}/${card.seats} ที่นั่ง)`}
        />
        <Fact label="แพ็กเกจ" value={session.packageName} strong />
        <Fact label="เวลาใช้โต๊ะ" value={clockRange(session.openedAt, session.endsAt)} mono />
        <Fact
          label="ใช้ไปแล้ว"
          value={
            <>
              <b className="font-mono tabular-nums">{session.minutesUsed}</b> นาที
            </>
          }
        />
        <TimeRail {...time.rail} />
        <Fact label="ยอดบิล" value={formatBaht(session.runningTotal)} strong />
      </dl>

      {session.ordersWaiting > 0 ? (
        <div className="mt-4 flex items-start gap-2 rounded-lg bg-sunken p-3 text-label text-ink-2">
          <BellIcon className="size-4 flex-none pt-0.5" strokeWidth={1.75} aria-hidden />
          มีออเดอร์รอรับ · {session.ordersWaiting} รอบ
        </div>
      ) : null}

      {/* หน้ารายละเอียดบิล/ย้ายโต๊ะ/รวมบิล เป็นของ ticket 16, 21, 22 — ปุ่มยังไม่ทำที่นี่ */}
      <div className="mt-4 flex flex-col gap-2">
        <Button variant="secondary" className="w-full" disabled>
          ดูรายละเอียด
        </Button>
        <div className="grid grid-cols-2 gap-2">
          <Button variant="secondary" size="sm" disabled>
            ย้ายโต๊ะ
          </Button>
          <Button variant="secondary" size="sm" disabled>
            รวมบิล
          </Button>
        </div>
      </div>
    </>
  );
}

function Fact({
  label,
  value,
  mono,
  strong,
}: {
  label: string;
  value: ReactNode;
  mono?: boolean;
  strong?: boolean;
}) {
  return (
    <div className="flex items-start justify-between gap-3 py-1.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={`text-right ${mono ? "font-mono" : ""} ${strong ? "font-semibold text-ink" : "text-ink-2"}`}>
        {value}
      </dd>
    </div>
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
      <span className={`text-sm font-semibold ${tone === "use" ? "text-orange-ink" : "text-ink-2"}`}>
        {label}
      </span>
      <b className="text-title tabular-nums">{count}</b>
    </span>
  );
}
