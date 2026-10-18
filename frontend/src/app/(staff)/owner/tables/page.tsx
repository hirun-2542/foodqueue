import type { Metadata } from "next";
import { FloorMapScreen } from "./floor-map-screen";

export const metadata: Metadata = {
  title: "โต๊ะ · FoodQueue",
};

/** `/owner/tables` — frame `o12-tables` (live floor) and frame `o22-tables-edit` (แก้ไขผัง). */
export default function OwnerTablesPage() {
  return <FloorMapScreen />;
}
