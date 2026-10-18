"use client";

import { type ReactNode, useState } from "react";
import { Bar, BarChart, CartesianGrid, Cell, LabelList, ResponsiveContainer, XAxis, YAxis } from "recharts";
import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import {
  bangkokDayStart,
  formatBahtWhole,
  formatCount,
  formatDayHeading,
  formatDayRange,
  formatDayTick,
} from "@/lib/format";
import { useMenuItems } from "@/lib/menu";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import { paymentMethodLabel, reportRangeLabel, useReportSummary } from "@/lib/reports";
import { KpiBand, KpiCell, KpiValue } from "../kpi-band";
import type {
  DailyRow,
  HourlyRow,
  PackageMixRow,
  PaymentMixRow,
  ReportRange,
  ReportSummaryDto,
} from "@/lib/types";

const RANGES: ReportRange[] = ["today", "7d", "30d"];

/**
 * Segment colours of the stacked "share" bars, as frame `o14` draws them: the package bar puts the
 * accent on the second tier and the payment bar on the first method, so each card passes its own
 * order and later rows fall back to the quieter tokens.
 */
const PACKAGE_MIX_COLORS = ["bg-control-border", "bg-orange", "bg-ink-2", "bg-orange-line", "bg-line-strong"];
const PAYMENT_MIX_COLORS = ["bg-orange", "bg-control-border", "bg-ink-2", "bg-orange-line", "bg-line-strong"];

/**
 * `/owner/reports` — frame `o14-reports`, fed by `GET /api/reports/summary`.
 *
 * The screen only presents what the endpoint computed: the day boundaries, the paid-only rule and the
 * merged-bill rule all live in the API, so the figures here cannot drift from the KPI band on `/owner`.
 * Share is a stacked bar with a legend (DESIGN: never a ring or pie chart) and the top list is a
 * ranked bar list. When the range has no sales at all the charts are replaced by the empty state
 * "ยังไม่มียอดขายในช่วงนี้" instead of drawing an empty chart that looks broken.
 */
export function ReportsScreen() {
  return (
    <LiveProvider>
      <ReportsBody />
    </LiveProvider>
  );
}

function ReportsBody() {
  const [range, setRange] = useState<ReportRange>("7d");
  const report = useReportSummary(range);
  const offline = useLiveOffline();

  return (
    <div className="flex flex-col gap-4 lg:gap-5">
      <div className="flex min-h-12 flex-wrap items-center justify-between gap-3 lg:min-h-20">
        <div className="flex flex-col justify-center gap-0.5">
          <h1 className="text-headline">รายงาน</h1>
          <span className="text-label font-normal text-muted-foreground">
            {report.data
              ? reportSubtitle(range, report.data.summary)
              : "นับเฉพาะบิลที่ชำระแล้ว · วันขายนับตามเวลาไทย"}
          </span>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {offline ? (
            <span className="text-label text-muted-foreground">ออฟไลน์ — ตัวเลขอาจไม่เป็นปัจจุบัน</span>
          ) : null}
          <div className="inline-flex w-fit gap-0.5 rounded-lg bg-sunken p-[3px]" role="group" aria-label="ช่วงรายงาน">
            {RANGES.map((option) => (
              <button
                key={option}
                type="button"
                aria-pressed={range === option}
                onClick={() => setRange(option)}
                className={`flex h-[34px] items-center rounded-[9px] px-4 text-[14px] ${
                  range === option
                    ? "bg-surface font-semibold text-ink ring-1 ring-border"
                    : "font-medium text-ink-2"
                }`}
              >
                {reportRangeLabel[option]}
              </button>
            ))}
          </div>
        </div>
      </div>

      {report.isPending ? <ReportsSkeleton /> : null}

      {report.isError ? (
        <div className="flex flex-col items-start gap-3">
          <p className="text-body text-muted-foreground">
            {report.error instanceof ApiError
              ? report.error.message
              : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
          </p>
          <Button variant="outline" onClick={() => void report.refetch()}>
            ลองอีกครั้ง
          </Button>
        </div>
      ) : null}

      {report.data ? <ReportContent range={range} summary={report.data.summary} /> : null}
    </div>
  );
}

/** `ศุกร์ 16 ต.ค. 2569 · ช่วง 7 วัน (10–16 ต.ค.) · นับเฉพาะบิลที่ชำระแล้ว` */
function reportSubtitle(range: ReportRange, summary: ReportSummaryDto): string {
  const days = summary.daily;
  if (days.length === 0) return "นับเฉพาะบิลที่ชำระแล้ว";
  const last = days[days.length - 1].date;
  const heading = formatDayHeading(bangkokDayStart(last));
  if (range === "today") return `${heading} · นับเฉพาะบิลที่ชำระแล้ว`;
  const span = formatDayRange(days[0].date, last, false);
  return `${heading} · ช่วง ${reportRangeLabel[range]} (${span}) · นับเฉพาะบิลที่ชำระแล้ว`;
}

function ReportsSkeleton() {
  return (
    <div className="flex flex-col gap-5">
      <Skeleton className="h-[104px] rounded-xl" />
      <div className="grid gap-5 xl:grid-cols-2">
        <Skeleton className="h-[380px] rounded-xl" />
        <Skeleton className="h-[380px] rounded-xl" />
      </div>
      <Skeleton className="h-[420px] rounded-xl" />
    </div>
  );
}

function ReportContent({ range, summary }: { range: ReportRange; summary: ReportSummaryDto }) {
  // ไม่มีบิลที่ชำระแล้วเลยในช่วงนี้ → บอกตรง ๆ ไม่ใช่กราฟว่าง (spec user story 99)
  if (summary.billCount === 0 && summary.revenue === 0) {
    return (
      <EmptyState
        icon={<BarChart3Icon />}
        title="ยังไม่มียอดขายในช่วงนี้"
        description="ช่วงที่เลือกยังไม่มีบิลที่ชำระแล้ว ลองเปลี่ยนช่วงหรือรอให้ปิดบิลรอบถัดไป"
      />
    );
  }

  return (
    <div className="flex flex-col gap-5">
      <ReportKpiCards range={range} summary={summary} />

      <div className="grid gap-5 xl:grid-cols-2">
        <DailyCard daily={summary.daily} />
        <HourlyCard range={range} hourly={summary.hourly} />
      </div>

      <div className="grid gap-5 xl:grid-cols-2">
        <PackageMixCard range={range} mix={summary.packageMix} totalBills={summary.billCount} />
        <PaymentMixCard range={range} mix={summary.paymentMix} totalBills={summary.billCount} />
      </div>

      <TopItemsCard range={range} items={summary.topItems} />
    </div>
  );
}

function BarChart3Icon() {
  return (
    <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.75" aria-hidden>
      <path d="M3 3v16a2 2 0 0 0 2 2h16" strokeLinecap="round" />
      <rect x="7" y="10" width="3" height="7" rx="1" />
      <rect x="12" y="5" width="3" height="12" rx="1" />
      <rect x="17" y="8" width="3" height="9" rx="1" />
    </svg>
  );
}

/** The four cards of frame `o14`, all from the same endpoint the overview band reads. */
function ReportKpiCards({ range, summary }: { range: ReportRange; summary: ReportSummaryDto }) {
  const perBill = summary.billCount === 0 ? 0 : summary.revenue / summary.billCount;
  const days = Math.max(1, summary.daily.length);

  return (
    <KpiBand>
      <KpiCell label={`ยอดขาย${range === "today" ? "วันนี้" : ` ${reportRangeLabel[range]}`}`}>
        <KpiValue>{formatBahtWhole(Math.round(summary.revenue))}</KpiValue>
        <span className="text-label text-muted-foreground">เฉลี่ย {formatBahtWhole(Math.round(perBill))} ต่อบิล</span>
      </KpiCell>

      <KpiCell label="บิลที่ชำระแล้ว">
        <KpiValue>{formatCount(summary.billCount)}</KpiValue>
        <span className="text-label text-muted-foreground">
          เฉลี่ย {formatCount(Math.round(summary.billCount / days))} บิลต่อวัน
        </span>
      </KpiCell>

      <KpiCell label="ลูกค้า (หัว)">
        <KpiValue>{formatCount(summary.guestCount)}</KpiValue>
        <span className="text-label text-muted-foreground">
          เฉลี่ย {formatCount(Math.round(summary.guestCount / days))} คนต่อวัน
        </span>
      </KpiCell>

      <KpiCell label="เฉลี่ยต่อหัว">
        <KpiValue>{formatBahtWhole(Math.round(summary.avgPerGuest))}</KpiValue>
        <span className="text-label text-muted-foreground">ยอดขาย ÷ จำนวนลูกค้า</span>
      </KpiCell>
    </KpiBand>
  );
}

function Card({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3 rounded-xl border border-border bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex flex-col gap-0.5">
          <h2 className="text-title">{title}</h2>
          <span className="text-xs text-muted-foreground">{subtitle}</span>
        </div>
        <DemoTag />
      </div>
      {children}
    </section>
  );
}

/** Frame `o14` marks every chart card this way; the KPI band of `o01` carries no tag. */
function DemoTag() {
  return (
    <span className="inline-flex h-[22px] flex-none items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
      DEMO DATA
    </span>
  );
}

/** `40,000`-style ticks; the bars themselves are the Ember palette of the frame (line + today dark). */
function valueTick(value: number): string {
  return formatCount(value);
}

function DailyCard({ daily }: { daily: DailyRow[] }) {
  const total = daily.reduce((sum, day) => sum + day.revenue, 0);
  const last = daily[daily.length - 1];
  const subtitle =
    daily.length === 0
      ? "ไม่มียอดในช่วงนี้"
      : `${formatDayRange(daily[0].date, last.date, true)} · รวม ${formatBahtWhole(total)} · แถบเข้มคือวันนี้`;
  // แท่งเยอะ (30 วัน) ให้แกน x ข้ามป้ายบ้าง ไม่ให้ทับกัน
  const tickInterval = daily.length > 10 ? Math.ceil(daily.length / 8) - 1 : 0;
  const withValues = daily.length <= 12;

  return (
    <Card title="ยอดขายรายวัน" subtitle={subtitle}>
      <div className="h-[300px] w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={daily} margin={{ top: 18, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke="var(--color-hairline)" />
            <XAxis
              dataKey="date"
              tickFormatter={formatDayTick}
              interval={tickInterval}
              tickLine={false}
              axisLine={{ stroke: "var(--color-hairline)" }}
              tick={{ fontSize: 12, fill: "var(--color-muted-foreground)" }}
            />
            <YAxis
              tickFormatter={valueTick}
              tickLine={false}
              axisLine={false}
              width={56}
              tick={{ fontSize: 12, fill: "var(--color-muted-foreground)" }}
            />
            <Bar dataKey="revenue" name="ยอดขาย" maxBarSize={42} radius={[4, 4, 0, 0]} isAnimationActive={false}>
              {daily.map((day, index) => (
                <Cell
                  key={day.date}
                  fill={index === daily.length - 1 ? "var(--color-orange)" : "var(--color-orange-line)"}
                />
              ))}
              {withValues ? (
                <LabelList
                  dataKey="revenue"
                  position="top"
                  offset={6}
                  formatter={(value) => formatCount(Number(value ?? 0))}
                  fill="var(--color-ink-2)"
                  fontSize={12}
                />
              ) : null}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </Card>
  );
}

function HourlyCard({ range, hourly }: { range: ReportRange; hourly: HourlyRow[] }) {
  // เฉพาะชั่วโมงที่มีจริง (เฟรม o14 แสดง 11:00–21:00 ไม่ได้วาด 24 แท่งที่ว่างเปล่า)
  const first = hourly.findIndex((row) => row.revenue > 0);
  const lastFromEnd = [...hourly].reverse().findIndex((row) => row.revenue > 0);
  const last = lastFromEnd === -1 ? -1 : hourly.length - 1 - lastFromEnd;
  const rows = first === -1 ? hourly : hourly.slice(first, last + 1);
  const total = rows.reduce((sum, row) => sum + row.revenue, 0);
  const peak = rows.reduce((best, row) => (row.revenue > best.revenue ? row : best), rows[0]);
  const subtitle = `${reportRangeLabel[range]} · ${hourLabel(rows[0]?.hour ?? 0)}–${hourLabel(
    rows[rows.length - 1]?.hour ?? 0,
  )} · รวม ${formatBahtWhole(total)}`;

  return (
    <Card title="ยอดขายรายชั่วโมง" subtitle={subtitle}>
      <div className="h-[300px] w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={rows} margin={{ top: 18, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke="var(--color-hairline)" />
            <XAxis
              dataKey="hour"
              tickFormatter={hourLabel}
              interval={rows.length <= 12 ? 0 : 1}
              tickLine={false}
              axisLine={{ stroke: "var(--color-hairline)" }}
              tick={{ fontSize: 12, fill: "var(--color-muted-foreground)" }}
            />
            <YAxis
              tickFormatter={valueTick}
              tickLine={false}
              axisLine={false}
              width={56}
              tick={{ fontSize: 12, fill: "var(--color-muted-foreground)" }}
            />
            <Bar dataKey="revenue" name="ยอดขาย" maxBarSize={40} radius={[4, 4, 0, 0]} isAnimationActive={false}>
              {rows.map((row) => (
                <Cell
                  key={row.hour}
                  fill={row.hour === peak.hour ? "var(--color-orange)" : "var(--color-orange-line)"}
                />
              ))}
              {rows.length <= 14 ? (
                <LabelList
                  dataKey="revenue"
                  position="top"
                  offset={6}
                  formatter={(value) => {
                    const amount = Number(value ?? 0);
                    return amount > 0 ? formatCount(amount) : "";
                  }}
                  fill="var(--color-ink-2)"
                  fontSize={12}
                />
              ) : null}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </Card>
  );
}

function hourLabel(hour: number): string {
  return `${String(hour).padStart(2, "0")}:00`;
}

/**
 * Share as one stacked bar plus its legend table — never a pie or a ring (DESIGN). `head` names the
 * measure columns of this card and `rows[].middle` is the optional second one (guests for packages);
 * the payment table has no such column, so its grid is one column narrower.
 */
function MixCard({
  title,
  subtitle,
  head,
  colors,
  rows,
  totalBills,
}: {
  title: string;
  subtitle: string;
  head: [string, string];
  colors: string[];
  /** `middle` is the second measure column (guests for packages); payment methods pass null. */
  rows: { key: string; label: string; count: number; middle: number | null; amount: number }[];
  totalBills: number;
}) {
  const hasMiddle = rows.some((row) => row.middle != null);
  const cols = hasMiddle
    ? "grid-cols-[12px_1fr_54px_54px_104px_62px]"
    : "grid-cols-[12px_1fr_54px_104px_62px]";
  const share = (count: number) => (totalBills === 0 ? 0 : Math.round((count / totalBills) * 100));

  return (
    <Card title={title} subtitle={subtitle}>
      <div className="my-1 flex h-8 overflow-hidden rounded-md bg-hairline">
        {rows.map((row, index) => (
          <span
            key={row.key}
            className={colors[index % colors.length]}
            style={{ width: `${share(row.count)}%` }}
          />
        ))}
      </div>

      <div className={`grid ${cols} items-center gap-2.5 text-xs font-semibold text-muted-foreground`}>
        <span />
        <span>{head[0]}</span>
        <span className="text-right">{head[1]}</span>
        {hasMiddle ? <span className="text-right">คน</span> : null}
        <span className="text-right">ยอดขาย</span>
        <span className="text-right">สัดส่วน</span>
      </div>

      {rows.map((row, index) => (
        <div
          key={row.key}
          className={`grid ${cols} items-center gap-2.5 border-t border-border py-1.5 text-sm`}
        >
          <span className={`size-3 rounded ${colors[index % colors.length]}`} />
          <span>{row.label}</span>
          <span className="text-right tabular-nums">{formatCount(row.count)}</span>
          {hasMiddle ? (
            <span className="text-right tabular-nums">{formatCount(row.middle ?? 0)}</span>
          ) : null}
          <span className="text-right tabular-nums">{formatBahtWhole(row.amount)}</span>
          <span className="text-right tabular-nums">{share(row.count)}%</span>
        </div>
      ))}

      {rows.length > 0 ? (
        <div className={`grid ${cols} items-center gap-2.5 border-t border-border py-1.5 text-sm font-semibold`}>
          <span />
          <span>รวม</span>
          <span className="text-right tabular-nums">{formatCount(rows.reduce((s, r) => s + r.count, 0))}</span>
          {hasMiddle ? (
            <span className="text-right tabular-nums">
              {formatCount(rows.reduce((s, r) => s + (r.middle ?? 0), 0))}
            </span>
          ) : null}
          <span className="text-right tabular-nums">{formatBahtWhole(rows.reduce((s, r) => s + r.amount, 0))}</span>
          <span className="text-right tabular-nums">100%</span>
        </div>
      ) : (
        <p className="border-t border-border pt-2 text-sm text-muted-foreground">ไม่มีบิลในช่วงนี้</p>
      )}
    </Card>
  );
}

function PackageMixCard({
  range,
  mix,
  totalBills,
}: {
  range: ReportRange;
  mix: PackageMixRow[];
  totalBills: number;
}) {
  return (
    <MixCard
      title="สัดส่วนแพ็กเกจ"
      subtitle={`${reportRangeLabel[range]} · แบ่งตามจำนวนบิล`}
      head={["แพ็กเกจ", "บิล"]}
      colors={PACKAGE_MIX_COLORS}
      totalBills={totalBills}
      rows={mix.map((row) => ({
        key: row.name,
        label: row.name,
        count: row.sessions,
        middle: row.guests,
        amount: row.revenue,
      }))}
    />
  );
}

function PaymentMixCard({
  range,
  mix,
  totalBills,
}: {
  range: ReportRange;
  mix: PaymentMixRow[];
  totalBills: number;
}) {
  return (
    <MixCard
      title="วิธีจ่ายเงิน"
      subtitle={`${reportRangeLabel[range]} · แบ่งตามจำนวนบิล`}
      head={["วิธีจ่าย", "บิล"]}
      colors={PAYMENT_MIX_COLORS}
      totalBills={totalBills}
      rows={mix.map((row) => ({
        key: row.method,
        label: paymentMethodLabel[row.method] ?? row.method,
        count: row.count,
        middle: null,
        amount: row.total,
      }))}
    />
  );
}

/**
 * Top 10 as two ranked columns with a bar per item (frame `o14`). The "หมดชั่วคราว" badge comes from
 * the menu list, not from the report: the report counts what was served in range and a since-disabled
 * item still sold then.
 */
function TopItemsCard({ range, items }: { range: ReportRange; items: { name: string; quantity: number }[] }) {
  const menu = useMenuItems();
  const unavailable = new Set((menu.data ?? []).filter((item) => !item.isAvailable).map((item) => item.name));
  const max = items.reduce((best, item) => Math.max(best, item.quantity), 0);
  const columns: [number, typeof items][] = [
    [0, items.slice(0, 5)],
    [5, items.slice(5, 10)],
  ];

  return (
    <Card
      title="เมนูขายดี 10 อันดับ"
      subtitle={`${reportRangeLabel[range]} · จำนวนที่เสิร์ฟ · ไม่นับออเดอร์ที่ถูกยกเลิก`}
    >
      <div className="grid gap-4 sm:grid-cols-2 sm:gap-10">
        {columns.map(([offset, column]) => (
          <ol key={offset} className="flex flex-col gap-2">
            {column.map((item, index) => (
              <li key={item.name} className="grid grid-cols-[26px_1fr_72px] items-center gap-2.5">
                <span className="font-mono text-[13px] text-muted-foreground">
                  {String(offset + index + 1).padStart(2, "0")}
                </span>
                <span className="flex min-w-0 flex-col">
                  <span className="flex items-center gap-1.5">
                    <span className="truncate font-semibold">{item.name}</span>
                    {unavailable.has(item.name) ? (
                      <span className="inline-flex h-5 flex-none items-center rounded-full bg-cancel-soft px-2 text-xs font-semibold text-cancel-ink">
                        หมดชั่วคราว
                      </span>
                    ) : null}
                  </span>
                  <span className="mt-1 h-1.5 rounded-full bg-hairline">
                    <span
                      className="block h-1.5 rounded-full bg-orange-line"
                      style={{ width: `${max === 0 ? 0 : (item.quantity / max) * 100}%` }}
                    />
                  </span>
                </span>
                <span className="text-right font-semibold tabular-nums">{formatCount(item.quantity)}</span>
              </li>
            ))}
          </ol>
        ))}
      </div>
    </Card>
  );
}
