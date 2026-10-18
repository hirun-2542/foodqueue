"use client";

import { PlusIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { TimeRail } from "@/components/time-rail";
import { clockRange, formatBaht, roundTime, type TimeTone } from "@/lib/format";
import { occupied } from "@/lib/overview";
import type { TableOverviewDto } from "@/lib/types";

/** The minutes text is tinted by how much of the round is left: quiet, Last order, or over. */
const minutesTone: Record<TimeTone, string> = {
  elapsed: "text-muted-foreground",
  late: "text-orange-ink",
  over: "text-cancel-ink",
};

/**
 * One card of the overview grid (frame `o01-overview`): a free table offers เปิดโต๊ะ, an occupied one
 * shows its package, guests, start–end times, minutes used, kitchen backlog and running total.
 *
 * The server sends `minutesUsed` and both instants, so nothing here reads the clock: the same card
 * renders identical text a second later, and the rail length is the only thing that moves (on the
 * next poll). No countdown exists anywhere on this screen.
 */
export function TableCard({
  card,
  lastOrderWarningMinutes,
  onOpenTable,
}: {
  card: TableOverviewDto;
  lastOrderWarningMinutes: number;
  onOpenTable: (table: { id: number; name: string }) => void;
}) {
  const session = occupied(card);

  if (!session) {
    return (
      <li className="flex min-h-44 flex-col gap-2.5 rounded-xl border border-dashed border-line-strong p-4">
        <div className="flex items-center justify-between gap-2">
          <b className="text-[20px] leading-tight font-semibold text-ink-2 lg:text-[22px]">{card.name}</b>
          <Badge variant="neutral" className="before:hidden">
            ว่าง
          </Badge>
        </div>
        <span className="text-label font-normal text-muted-foreground">{card.seats} ที่นั่ง</span>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          className="mt-auto self-start"
          onClick={() => onOpenTable({ id: card.id, name: card.name })}
        >
          <PlusIcon aria-hidden />
          เปิดโต๊ะ
        </Button>
      </li>
    );
  }

  const time = roundTime(session.minutesUsed, session.openedAt, session.endsAt, lastOrderWarningMinutes);
  const guests = `${session.adults} ผู้ใหญ่${session.children > 0 ? ` ${session.children} เด็ก` : ""}`;

  return (
    <li
      className={`flex min-h-44 flex-col gap-2.5 rounded-xl border bg-surface p-4 ${
        time.isOver ? "border-orange-strong" : time.isLastOrder ? "border-orange-line" : "border-border"
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <b className="text-[20px] leading-tight font-semibold lg:text-[22px]">{card.name}</b>
        <span className="flex flex-wrap items-center justify-end gap-1.5">
          {time.isOver ? (
            <Badge variant="cancelled" className="before:hidden">
              ปิดการสั่งแล้ว
            </Badge>
          ) : time.isLastOrder ? (
            <Badge variant="accent" className="before:hidden">
              Last order
            </Badge>
          ) : null}
          {/* Package tier tone needs the rank, which TableOverviewDto deliberately does not carry. */}
          <Badge variant="neutral" className="before:hidden">
            {session.packageName}
          </Badge>
        </span>
      </div>

      <span className="text-label font-normal text-muted-foreground">{guests}</span>

      <div className="flex flex-col gap-1">
        <div className="flex flex-wrap items-baseline justify-between gap-x-3">
          <span className="font-mono text-[17px] leading-tight font-medium lg:text-[22px]">
            {clockRange(session.openedAt, session.endsAt)}
          </span>
          <span className={`text-label font-normal ${minutesTone[time.rail.tone]}`}>
            ใช้ไป{" "}
            <b className="font-mono tabular-nums">{session.minutesUsed}</b> นาที
          </span>
        </div>
        <TimeRail {...time.rail} />
      </div>

      <div className="mt-auto flex items-center justify-between gap-2 border-t border-border pt-2.5">
        {session.ordersWaiting > 0 ? (
          <Badge variant="pending">ค้างครัว {session.ordersWaiting}</Badge>
        ) : (
          <Badge variant="neutral" className="before:hidden">
            เสิร์ฟครบ
          </Badge>
        )}
        <b className="tabular-nums">{formatBaht(session.runningTotal)}</b>
      </div>
    </li>
  );
}
