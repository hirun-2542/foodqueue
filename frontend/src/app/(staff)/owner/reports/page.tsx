import type { Metadata } from "next";
import { ReportsScreen } from "./reports-screen";

export const metadata: Metadata = {
  title: "รายงาน · FoodQueue",
};

/**
 * `/owner/reports` — frame `o14-reports`. The range switcher, the KPI cards and the charts are all
 * drawn from `GET /api/reports/summary`; nothing here computes a sales figure of its own.
 */
export default function ReportsPage() {
  return <ReportsScreen />;
}
