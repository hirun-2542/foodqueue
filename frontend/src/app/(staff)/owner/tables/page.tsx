import type { Metadata } from "next";
import { FloorPlanEditor } from "./floor-plan-editor";

export const metadata: Metadata = {
  title: "โต๊ะ · FoodQueue",
};

/** `/owner/tables` — the floor-plan edit mode of frame `o22-tables-edit`. */
export default function OwnerTablesPage() {
  return <FloorPlanEditor />;
}
