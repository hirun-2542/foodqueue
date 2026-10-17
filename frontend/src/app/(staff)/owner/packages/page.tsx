import type { Metadata } from "next";
import { PackagesScreen } from "./packages-screen";

export const metadata: Metadata = {
  title: "แพ็กเกจ · FoodQueue",
};

export default function PackagesPage() {
  return <PackagesScreen />;
}
