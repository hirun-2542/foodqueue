"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { formatBahtWhole, formatCount, roundTime } from "@/lib/format";
import { occupied, useTableOverview } from "@/lib/overview";
import { LiveProvider } from "@/lib/realtime";
import { useReportSummary } from "@/lib/reports";
import type { ReportSummaryDto, SessionDetailDto, TableOverviewDto } from "@/lib/types";
import { useSettings } from "./settings/settings-api";
import { OpenTableDialog, QrResultDialog } from "./open-table-flow";
import { KpiBand, KpiCell, KpiValue } from "./kpi-band";
import { TableCard } from "./table-card";

/**
 * `/owner` — the table grid of frame `o01-overview` (desktop) and `o16-owner-mobile` (phone), driven
 * by `GET /api/tables/overview`.
 *
 * Every figure on a card comes from the server: `openedAt`/`endsAt` are stored data and ใช้ไป N นาที
 * is measured against the server clock, so nothing is derived from the browser clock and no screen
 * can grow a countdown. The grid refetches when the hub reports `sessionChanged`/`orderCreated` (and
 * the other floor events, see `useTableOverview`), not on an interval.
 *
 * The KPI band above the grid is ticket 23's (`GET /api/reports/summary`): it is not faked here.
 */
export function OwnerOverviewScreen() {
  return (
    <LiveProvider>
      <OwnerOverviewBody />
    </LiveProvider>
  );
}

function OwnerOverviewBody() {
  const overview = useTableOverview();
  const settings = useSettings();
  // The KPI band is the reports endpoint (ticket 23), not a second sales calculation: the same answer
  // `/owner/reports` shows, so the two screens agree by construction.
  const report = useReportSummary("today");

  const [openTable, setOpenTable] = useState<{ id: number; name: string; previewStart: number } | null>(null);
  const [created, setCreated] = useState<SessionDetailDto | null>(null);

  const cards = overview.data ?? [];
  const inUse = cards.filter((card) => card.sessionId != null).length;
  const lastOrderWarningMinutes = settings.data?.lastOrderWarningMinutes ?? 0;
  // ต้องดูแล = rounds that are inside the Last-order window or already past their end time, both
  // derived from the server's own numbers.
  const needsAttention = cards.filter((card) => {
    const session = occupied(card);
    if (!session) return false;
    const time = roundTime(session.minutesUsed, session.openedAt, session.endsAt, lastOrderWarningMinutes);
    return time.isLastOrder || time.isOver;
  }).length;

  if (overview.isPending) {
    return (
      <div className="flex flex-col gap-4">
        <PageHeading />
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-44 rounded-xl" />
          ))}
        </div>
      </div>
    );
  }

  if (overview.isError) {
    return (
      <div className="flex flex-col gap-4">
        <PageHeading />
        <p className="text-body text-muted-foreground">
          {overview.error instanceof ApiError
            ? overview.error.message
            : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
        </p>
        <Button variant="outline" className="self-start" onClick={() => void overview.refetch()}>
          ลองอีกครั้ง
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4 lg:gap-5">
      <PageHeading />

      {report.data ? <OverviewKpiBand cards={cards} summary={report.data.summary} /> : <Skeleton className="h-[104px] rounded-xl" />}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-title">โต๊ะ</h2>
        <div className="flex flex-wrap gap-2">
          <span className="inline-flex h-8.5 items-center rounded-full border border-orange-line bg-orange-soft px-3.5 text-sm font-semibold text-orange-ink">
            ทั้งหมด {cards.length}
          </span>
          <span className="inline-flex h-8.5 items-center rounded-full border border-border bg-surface px-3.5 text-sm text-ink-2">
            กำลังใช้ {inUse}
          </span>
          <span className="inline-flex h-8.5 items-center rounded-full border border-border bg-surface px-3.5 text-sm text-ink-2">
            ว่าง {cards.length - inUse}
          </span>
          {needsAttention > 0 ? (
            <span className="inline-flex h-8.5 items-center rounded-full border border-border bg-surface px-3.5 text-sm text-ink-2">
              ต้องดูแล {needsAttention}
            </span>
          ) : null}
        </div>
      </div>

      {cards.length === 0 ? (
        <p className="rounded-xl border border-dashed border-line-strong p-6 text-body text-muted-foreground">
          ยังไม่มีโต๊ะที่ใช้งานอยู่ — เพิ่มโต๊ะในหน้า “โต๊ะ” ก่อน
        </p>
      ) : (
        // Two columns even at 390px (frame `o16-owner-mobile`); the cards reflow to four on desktop
        // (frame `o01-overview`).
        <ul className="grid grid-cols-2 gap-2.5 lg:gap-3 xl:grid-cols-4">
          {cards.map((card) => (
            <TableCard
              key={card.id}
              card={card}
              lastOrderWarningMinutes={lastOrderWarningMinutes}
              onOpenTable={(table) => setOpenTable({ ...table, previewStart: Date.now() })}
            />
          ))}
        </ul>
      )}

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

function PageHeading() {
  return (
    <div className="flex min-h-12 flex-col justify-center gap-0.5 lg:min-h-20">
      <h1 className="text-headline">ภาพรวม</h1>
      <span className="text-label font-normal text-muted-foreground">เจ้าของร้าน</span>
    </div>
  );
}

/**
 * The KPI band of frame `o01-overview`: today's sales figures from `GET /api/reports/summary` plus the
 * one cell that is about the floor rather than the till (`โต๊ะที่เปิดอยู่` and the kitchen backlog),
 * which comes from the same `GET /api/tables/overview` answer the grid below is drawn from.
 *
 * The comparison with yesterday is the server's `previousRevenue` ("yesterday up to this time"), not a
 * number this screen derives: with nothing to compare against the line is omitted instead of faked.
 */
function OverviewKpiBand({ cards, summary }: { cards: TableOverviewDto[]; summary: ReportSummaryDto }) {
  const inUse = cards.filter((card) => card.sessionId != null).length;
  const waiting = cards.reduce((sum, card) => sum + (card.ordersWaiting ?? 0), 0);
  const perBill = summary.billCount === 0 ? 0 : summary.revenue / summary.billCount;
  const comparison = comparisonWithYesterday(summary);

  return (
    <KpiBand>
      <KpiCell label="ยอดขายวันนี้">
        <KpiValue>{formatBahtWhole(Math.round(summary.revenue))}</KpiValue>
        {comparison == null ? (
          <span className="text-label text-muted-foreground">นับเฉพาะบิลที่ชำระแล้ว</span>
        ) : (
          <span className={`text-label ${comparison.tone}`}>{comparison.text}</span>
        )}
      </KpiCell>

      <KpiCell label="บิลที่ชำระแล้ว">
        <KpiValue>{formatCount(summary.billCount)}</KpiValue>
        <span className="text-label text-muted-foreground">เฉลี่ย {formatBahtWhole(Math.round(perBill))} ต่อบิล</span>
      </KpiCell>

      <KpiCell label="ลูกค้า (หัว)">
        <KpiValue>{formatCount(summary.guestCount)}</KpiValue>
        <span className="text-label text-muted-foreground">เฉลี่ย {formatBahtWhole(Math.round(summary.avgPerGuest))} ต่อหัว</span>
      </KpiCell>

      <KpiCell label="โต๊ะที่เปิดอยู่">
        <KpiValue>
          {inUse} <span className="text-[18px] font-medium text-muted-foreground">/ {cards.length}</span>
        </KpiValue>
        <span className="text-label text-muted-foreground">ค้างครัว {formatCount(waiting)} รอบ</span>
      </KpiCell>
    </KpiBand>
  );
}

/** `+8% เทียบเมื่อวานเวลาเดียวกัน`, `−12% …` or null when there is no comparable base. */
function comparisonWithYesterday(summary: ReportSummaryDto): { text: string; tone: string } | null {
  const previous = summary.previousRevenue;
  if (previous == null || previous <= 0) return null;
  const change = Math.round(((summary.revenue - previous) / previous) * 100);
  const sign = change > 0 ? "+" : change < 0 ? "−" : "±";
  return {
    text: `${sign}${Math.abs(change)}% เทียบเมื่อวานเวลาเดียวกัน`,
    // Frame `o01` draws the rise green; a fall must not borrow the same colour.
    tone: change < 0 ? "text-cancel-ink" : "text-ready-ink",
  };
}
