import type { Metadata } from "next";
import { OpenTableFlow } from "./open-table-flow";

export const metadata: Metadata = {
  title: "ภาพรวม · FoodQueue",
};

/**
 * `/owner` — today it carries the open-table flow (ticket 07: frames `o02-open` and `o03-qr`).
 * The KPI band, the occupancy cards with ใช้ไป N นาที and ยอดสะสม come from ticket 08's overview
 * endpoint and ticket 23's reports, so they are not faked here.
 */
export default function OwnerHomePage() {
  return (
    <div className="flex flex-col gap-5 lg:py-5">
      <div className="flex min-h-20 flex-col justify-center gap-0.5">
        <h1 className="text-headline">ภาพรวม</h1>
        <span className="text-label font-normal text-muted-foreground">เจ้าของร้าน</span>
      </div>

      <OpenTableFlow />
    </div>
  );
}
