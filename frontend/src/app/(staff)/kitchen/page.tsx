import type { Metadata } from "next";
import { ChefHatIcon } from "lucide-react";
import { EmptyState } from "@/components/empty-state";

export const metadata: Metadata = {
  title: "จอครัว · FoodQueue",
};

export default function KitchenPage() {
  return (
    <div className="flex flex-col gap-3">
      <EmptyState
        icon={<ChefHatIcon className="size-5" strokeWidth={1.75} aria-hidden />}
        title="ยังไม่มีออเดอร์"
        description="การ์ดออเดอร์จากลูกค้าจะขึ้นที่นี่แบบเรียลไทม์ เมื่อลูกค้าเริ่มสั่งอาหารที่โต๊ะ"
      />
    </div>
  );
}
