import type { Metadata } from "next";
import { HistoryScreen } from "./history-screen";

export const metadata: Metadata = {
  title: "ประวัติการชำระ · FoodQueue",
};

/** `/owner/history` — frame o15: today's paid bills, one row per receipt, with reprint. */
export default function OwnerHistoryPage() {
  return <HistoryScreen />;
}
