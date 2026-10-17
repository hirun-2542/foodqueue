import type { Metadata } from "next";
import { EmptyState } from "@/components/empty-state";
import { LayoutGridIcon } from "lucide-react";

export const metadata: Metadata = {
  title: "ภาพรวม · FoodQueue",
};

export default function OwnerHomePage() {
  return (
    <div className="flex min-h-20 flex-col justify-center gap-0.5 lg:py-5">
      <h1 className="text-headline">ภาพรวม</h1>
      <span className="text-label font-normal text-muted-foreground">เจ้าของร้าน</span>

      <div className="pt-5">
        <EmptyState
          icon={<LayoutGridIcon className="size-5" strokeWidth={1.75} aria-hidden />}
          title="ยังไม่มีข้อมูลให้แสดง"
          description="หน้านี้จะแสดงภาพรวมโต๊ะและยอดขาย เมื่อส่วนจัดการโต๊ะและแพ็กเกจพร้อมใช้งาน"
        />
      </div>
    </div>
  );
}
