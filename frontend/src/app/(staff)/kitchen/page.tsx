import type { Metadata } from "next";
import { KitchenBoardScreen } from "./kitchen-board";

export const metadata: Metadata = {
  title: "จอครัว · FoodQueue",
};

export default function KitchenPage() {
  return <KitchenBoardScreen />;
}
