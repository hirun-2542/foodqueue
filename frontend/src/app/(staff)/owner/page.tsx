import type { Metadata } from "next";
import { OwnerOverviewScreen } from "./owner-overview-screen";

export const metadata: Metadata = {
  title: "ภาพรวม · FoodQueue",
};

/**
 * `/owner` — frame `o01-overview` on desktop and `o16-owner-mobile` on a phone.
 * The KPI band is ticket 23's reports endpoint; this screen is the table grid plus the open-table flow.
 */
export default function OwnerHomePage() {
  return <OwnerOverviewScreen />;
}
