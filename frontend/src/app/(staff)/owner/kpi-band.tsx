"use client";

import type { ReactNode } from "react";

/**
 * The KPI band of `o01-overview` and `o14-reports`: one row of four cells drawn from the same
 * `GET /api/reports/summary` answer, so the overview band and the report screen cannot show different
 * numbers for the same day.
 *
 * The grid and the cell are shared; the captions are not, because the two frames genuinely say
 * different things under the same figure ("เฉลี่ย ฿1,325 ต่อบิล" on the overview, "เฉลี่ย 24 บิลต่อวัน"
 * on the report, and the comparison with yesterday only makes sense for today).
 */
export function KpiBand({ children }: { children: ReactNode }) {
  return (
    <dl className="grid gap-px overflow-hidden rounded-xl border border-border bg-border sm:grid-cols-2 xl:grid-cols-4">
      {children}
    </dl>
  );
}

export function KpiCell({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1 bg-surface px-5 py-[18px]">
      <dt className="text-label text-muted-foreground">{label}</dt>
      <dd className="flex flex-col gap-0.5">{children}</dd>
    </div>
  );
}

/** The 28px figure of the frame; every number is tabular so cells line up. */
export function KpiValue({ children }: { children: ReactNode }) {
  return (
    <span className="text-[28px] leading-tight font-semibold tabular-nums tracking-[-0.01em]">{children}</span>
  );
}
