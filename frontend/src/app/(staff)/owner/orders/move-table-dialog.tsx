"use client";

import { ArrowLeftRightIcon, CheckIcon, CircleCheckIcon, QrCodeIcon, XIcon } from "lucide-react";
import { useId, useState } from "react";
import { TimeRail } from "@/components/time-rail";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { clockRange, formatBaht, formatTime, roundTime } from "@/lib/format";
import { type OccupiedTable, occupied, useTableOverview } from "@/lib/overview";
import { useMoveSession } from "@/lib/sessions";
import type { OrderDto, TableOverviewDto } from "@/lib/types";
import { useSettings } from "../settings/settings-api";

/**
 * ย้ายโต๊ะ (ticket 21) — the button of the queue card's secondary row (frame o08) and the dialog it opens.
 * The round is the card's `sessionId`; every round of a table moves together, so any card of it will do.
 */
export function MoveTableAction({ order }: { order: OrderDto }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button
        variant="ghost"
        className="h-11 flex-1 gap-2 px-2 text-[15px] text-ink-2"
        aria-haspopup="dialog"
        onClick={() => setOpen(true)}
      >
        <ArrowLeftRightIcon className="size-[18px]" strokeWidth={1.75} aria-hidden />
        ย้ายโต๊ะ
      </Button>
      {open ? <MoveTableDialog sessionId={order.sessionId} onClose={() => setOpen(false)} /> : null}
    </>
  );
}

/**
 * Frame o18: the round being moved (package, guests, start–end, ใช้ไป N นาที, running total — never a
 * countdown), a picker of every active table where only a free one can be chosen, and what the move
 * carries. Everything comes from the floor (`GET /api/tables/overview`), which the hub keeps current: a
 * table taken on another device greys out here and a chosen table that was taken is dropped.
 * The server is still the judge — a destination taken in the last moment answers `TABLE_OCCUPIED`.
 */
function MoveTableDialog({ sessionId, onClose }: { sessionId: number; onClose: () => void }) {
  const overview = useTableOverview();
  const settings = useSettings();
  const move = useMoveSession(sessionId);
  const ids = useId();
  const [picked, setPicked] = useState<number | null>(null);

  const cards = overview.data ?? [];
  const currentCard = cards.find((card) => card.sessionId === sessionId);
  const current = currentCard ? occupied(currentCard) : null;
  const target = cards.find((card) => card.id === picked && card.sessionId == null) ?? null;
  const freeCount = cards.filter((card) => card.sessionId == null).length;

  const submit = () => {
    if (!target || move.isPending) return;
    move.mutate(target.id, {
      onSuccess: onClose,
      onError: (error) => {
        // The round closed meanwhile: nothing left to move.
        if (error instanceof ApiError && error.code === "SESSION_CLOSED") onClose();
      },
    });
  };

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent showCloseButton={false} className="max-h-[calc(100dvh-2rem)] gap-0 overflow-y-auto p-0 sm:max-w-[640px]">
        <div className="flex items-start gap-3 px-6 pt-5 pb-4">
          <div className="flex grow flex-col gap-1">
            <DialogTitle className="text-[24px] leading-[1.35] font-semibold tracking-[-0.01em]">
              ย้ายโต๊ะ <span className="font-mono font-medium">{current?.name ?? ""}</span>
            </DialogTitle>
            <DialogDescription className="text-[13px] leading-[1.55]">
              ย้ายทั้งออเดอร์ บิล และเวลานั่งไปพร้อมกัน · เลือกได้เฉพาะโต๊ะว่าง
            </DialogDescription>
          </div>
          <DialogClose asChild>
            <Button variant="ghost" size="icon" aria-label="ปิด">
              <XIcon className="size-6" strokeWidth={1.75} aria-hidden />
            </Button>
          </DialogClose>
        </div>
        <hr className="border-border" />

        <div className="flex flex-col px-6 py-5">
          {overview.isPending ? (
            <div className="flex flex-col gap-4">
              <Skeleton className="h-28 rounded-xl" />
              <Skeleton className="h-36 rounded-xl" />
            </div>
          ) : !current ? (
            <p className="rounded-xl bg-sunken p-4 text-body text-ink-2">
              รอบนี้ปิดไปแล้ว (ชำระเงินหรือยกเลิกโต๊ะ) จึงย้ายโต๊ะไม่ได้
            </p>
          ) : (
            <>
              <CurrentRound card={current} lastOrderWarningMinutes={settings.data?.lastOrderWarningMinutes ?? 0} />

              <span id={`${ids}-label`} className="mt-5 mb-2.5 text-[13px] font-semibold text-ink-2">
                เลือกโต๊ะปลายทาง
              </span>
              <div
                role="radiogroup"
                aria-labelledby={`${ids}-label`}
                className="grid grid-cols-3 gap-2 sm:grid-cols-6"
              >
                {cards.map((card) => (
                  <TableTile
                    key={card.id}
                    card={card}
                    isCurrent={card.id === current.id}
                    isPicked={card.id === target?.id}
                    onPick={() => setPicked(card.id)}
                  />
                ))}
              </div>

              <div className="mt-3 flex flex-wrap gap-4 text-[12.5px] text-muted-foreground">
                <span className="inline-flex items-center gap-1.5">
                  <i className="size-2 flex-none rounded-full bg-ready-dot" aria-hidden />
                  ว่าง · ย้ายได้
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <i className="size-2 flex-none rounded-full bg-served-dot" aria-hidden />
                  มีลูกค้าอยู่ · ย้ายไม่ได้
                </span>
              </div>

              {freeCount === 0 ? (
                <p className="mt-4 rounded-xl bg-sunken px-4 py-3 text-sm text-ink-2">
                  ตอนนี้ไม่มีโต๊ะว่าง · ย้ายได้เมื่อมีโต๊ะชำระเงินหรือว่างลง
                </p>
              ) : (
                <ul className="mt-4 flex flex-col gap-1.5 rounded-xl bg-sunken px-4 py-3 text-sm text-ink-2">
                  {target ? (
                    <li className="flex items-center gap-2">
                      <ArrowLeftRightIcon className="size-4 flex-none text-muted-foreground" strokeWidth={1.75} aria-hidden />
                      <span>
                        ย้ายออเดอร์ บิล และเวลานั่งของ <span className="font-mono">{current.name}</span> ไปที่{" "}
                        <span className="font-mono">{target.name}</span>
                      </span>
                    </li>
                  ) : null}
                  <li className="flex items-center gap-2">
                    <QrCodeIcon className="size-4 flex-none text-muted-foreground" strokeWidth={1.75} aria-hidden />
                    QR เดิมยังใช้ได้ ไม่ต้องพิมพ์ใหม่
                  </li>
                  <li className="flex items-center gap-2">
                    <CircleCheckIcon className="size-4 flex-none text-muted-foreground" strokeWidth={1.75} aria-hidden />
                    <span>
                      โต๊ะ <span className="font-mono">{current.name}</span> จะว่างทันที
                    </span>
                  </li>
                </ul>
              )}
            </>
          )}
        </div>

        <hr className="border-border" />
        <div className="flex items-center justify-end gap-2 px-6 py-4">
          <Button variant="ghost" className="h-12 px-5" onClick={onClose}>
            ยกเลิก
          </Button>
          <Button className="h-12 px-5" disabled={!target || !current || move.isPending} onClick={submit}>
            ย้ายไปโต๊ะ{target ? <span className="font-mono font-medium">{` ${target.name}`}</span> : null}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** The round being moved, as the overview card shows it — start–end and minutes used, no countdown. */
function CurrentRound({
  card,
  lastOrderWarningMinutes,
}: {
  card: OccupiedTable;
  lastOrderWarningMinutes: number;
}) {
  const time = roundTime(card.minutesUsed, card.openedAt, card.endsAt, lastOrderWarningMinutes);
  const guests = `${card.adults} ผู้ใหญ่${card.children > 0 ? ` ${card.children} เด็ก` : ""}`;

  return (
    <div className="flex flex-col rounded-xl bg-sunken p-4">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-[26px] leading-[1.15] font-semibold tracking-[-0.01em]">{card.name}</span>
          <Badge variant="accent" className="before:hidden">
            {card.packageName}
          </Badge>
          <span className="text-sm text-ink-2">
            {guests} · <span className="font-mono">{clockRange(card.openedAt, card.endsAt)}</span>
          </span>
        </div>
        <b className="text-[18px] tabular-nums">{formatBaht(card.runningTotal)}</b>
      </div>
      <div className="mt-2.5 flex items-center gap-3">
        <div className="grow">
          <TimeRail {...time.rail} />
        </div>
        {time.isOver ? (
          <Badge variant="cancelled" className="before:hidden">
            ปิดการสั่งแล้ว
          </Badge>
        ) : time.isLastOrder ? (
          <Badge variant="accent" className="before:hidden">
            Last order
          </Badge>
        ) : null}
        <span className="text-sm whitespace-nowrap text-ink-2">
          ใช้ไป <b className="font-mono tabular-nums">{card.minutesUsed}</b> นาที
        </span>
      </div>
      <span className="mt-2 text-[12.5px] text-muted-foreground">
        ค้างครัว <span className="tabular-nums">{card.ordersWaiting}</span> รอบ ·{" "}
        {time.isOver ? (
          "ปิดการสั่งแล้ว"
        ) : (
          <>
            สั่งเพิ่มได้ถึง <span className="font-mono">{formatTime(card.endsAt)}</span>
          </>
        )}
      </span>
    </div>
  );
}

/**
 * One table of the picker: the round's own table (dashed, โต๊ะเดิม), a table with guests (greyed, its
 * head count — not choosable), a free table (ว่าง), or the chosen one (เลือกแล้ว).
 */
function TableTile({
  card,
  isCurrent,
  isPicked,
  onPick,
}: {
  card: TableOverviewDto;
  isCurrent: boolean;
  isPicked: boolean;
  onPick: () => void;
}) {
  const guests = (card.adults ?? 0) + (card.children ?? 0);
  const isFree = card.sessionId == null;
  const tone = isPicked
    ? "border-orange-line bg-orange-soft text-orange-ink"
    : isCurrent
      ? "border-dashed border-line-strong bg-sunken text-ink-2"
      : isFree
        ? "border-border bg-ready-soft text-ink hover:border-ready-dot"
        : "border-border bg-sunken text-muted-foreground";
  const note = isPicked ? "เลือกแล้ว" : isCurrent ? "โต๊ะเดิม" : isFree ? "ว่าง" : `${guests} คน`;
  const noteTone = isPicked
    ? "font-semibold text-orange-ink"
    : isCurrent
      ? "font-semibold text-ink-2"
      : isFree
        ? "font-semibold text-ready-ink"
        : "";

  return (
    <button
      type="button"
      role="radio"
      aria-checked={isPicked}
      disabled={!isFree}
      aria-label={`โต๊ะ ${card.name} · ${isCurrent ? "โต๊ะเดิม" : isFree ? "ว่าง ย้ายได้" : "มีลูกค้าอยู่ ย้ายไม่ได้"}`}
      onClick={onPick}
      className={`flex min-h-16 flex-col justify-between rounded-xl border px-2.5 py-2 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed ${tone}`}
    >
      <span className="flex items-center justify-between gap-1">
        <b className="truncate font-mono text-[17px] leading-[1.2] font-medium">{card.name}</b>
        {isPicked ? <CheckIcon className="size-4 flex-none" strokeWidth={2} aria-hidden /> : null}
      </span>
      <span className={`text-[12px] leading-[1.3] ${noteTone}`}>{note}</span>
    </button>
  );
}
