import type { Metadata } from "next";
import { MenuScreen } from "./menu-screen";

export const metadata: Metadata = {
  title: "เมนูอาหาร · FoodQueue",
};

/**
 * `/owner/menu` — frames `o09-menu` (category rail + item table) and `o10-menu-edit` (the drawer).
 */
export default function OwnerMenuPage() {
  return <MenuScreen />;
}
