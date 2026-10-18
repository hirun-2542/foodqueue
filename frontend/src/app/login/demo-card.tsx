"use client";

import { ArrowRightIcon, ChefHatIcon, LayoutGridIcon, Loader2Icon, SmartphoneIcon } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, apiFetch } from "@/lib/api";
import { staffHome } from "@/lib/auth";
import { useDemoCustomerSession, useDemoLogin, useDemoStatus } from "@/lib/demo";
import type { DemoRole, MeDto } from "@/lib/types";

/**
 * The three demo actions in the order frames l1/l2 draw them: owner (the one filled button), kitchen,
 * then the customer view. `hint` is the second line of the button; the variant decides its colour.
 */
const actions = [
  {
    role: "Owner",
    icon: LayoutGridIcon,
    title: "เข้าใช้แบบเจ้าของร้าน",
    hint: "ภาพรวม โต๊ะ รายงาน และการรับชำระเงิน",
    filled: true,
  },
  {
    role: "Kitchen",
    icon: ChefHatIcon,
    title: "เข้าใช้แบบครัว",
    hint: "รับออเดอร์และเลื่อนสถานะบนจอครัว",
    filled: false,
  },
] as const;

const customerAction = {
  icon: SmartphoneIcon,
  title: "ดูมุมมองลูกค้า",
  hint: "ลองสั่งอาหารเหมือนลูกค้าที่โต๊ะจริง",
} as const;

/**
 * The "ทดลองใช้งาน" card of `/login` (frames l1/l2, ticket 24): one tap signs in as the demo owner or
 * kitchen account, or opens the customer screen of a real round in this database — no password typed.
 *
 * It renders only when `GET /api/demo/status` answers `enabled: true`. With `Demo:Enabled=false` that
 * endpoint is itself 404 (the server's one switch), {@link useDemoStatus} reads the 404 as disabled and
 * the card is absent — there is no second client-side flag to keep in sync.
 *
 * Frame l1's bot activity feed is ticket 25's live output and is deliberately not faked here; the caption
 * describing the bot is shown because it describes the demo rather than reporting an event.
 */
export function DemoCard() {
  const status = useDemoStatus();
  const demoLogin = useDemoLogin();
  const customerSession = useDemoCustomerSession();
  const router = useRouter();
  const [pending, setPending] = useState<DemoRole | "Customer" | null>(null);

  if (!status.data?.enabled) {
    return null;
  }

  const enter = async (role: DemoRole) => {
    setPending(role);
    try {
      await demoLogin.mutateAsync(role);
      const me = await apiFetch<MeDto>("/api/auth/me");
      router.replace(staffHome(me.role));
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "เข้าใช้ไม่สำเร็จ ลองอีกครั้ง");
      setPending(null);
    }
  };

  const viewCustomer = async () => {
    setPending("Customer");
    try {
      const { token } = await customerSession.mutateAsync();
      router.push(`/s/${token}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "เปิดมุมมองลูกค้าไม่สำเร็จ ลองอีกครั้ง");
      setPending(null);
    }
  };

  /**
   * One demo action: a 58px-tall row on a phone, 64px on the desktop card (frames l2/l1).
   * Three call sites share these exact metrics and the busy swap, so the shape is written once.
   */
  const actionButton = (
    { icon: Icon, title, hint }: { icon: typeof LayoutGridIcon; title: string; hint: string },
    key: string,
    filled: boolean,
    busy: boolean,
    onClick: () => void,
  ) => (
    <Button
      key={key}
      variant={filled ? "default" : "secondary"}
      size="lg"
      className="h-auto min-h-[58px] w-full justify-start gap-3.5 px-4 py-2.5 text-left whitespace-normal lg:h-16 lg:py-0"
      disabled={pending !== null}
      onClick={onClick}
    >
      {busy ? (
        <Loader2Icon className="size-6 animate-spin" aria-hidden />
      ) : (
        <Icon className="size-6" strokeWidth={1.75} aria-hidden />
      )}
      <span className="flex flex-col">
        <span className="text-[15px] leading-[1.3] font-semibold">{title}</span>
        <span
          className={`text-[12.5px] leading-[1.45] font-normal ${
            filled ? "text-primary-foreground" : "text-muted-foreground"
          }`}
        >
          {hint}
        </span>
      </span>
    </Button>
  );

  return (
    <div className="flex flex-col gap-5.5 lg:flex-1">
      {/* l2 separates the two blocks with "หรือ"; l1 puts them side by side, so the rule disappears.
          It belongs to the card: with the demo off there is nothing to separate. */}
      <div className="flex items-center gap-3 lg:hidden">
        <span className="h-px flex-1 bg-hairline" />
        <span className="text-label font-normal text-muted-foreground">หรือ</span>
        <span className="h-px flex-1 bg-hairline" />
      </div>

      <section
        aria-labelledby="demo-heading"
        className="flex flex-col rounded-xl border border-hairline bg-surface p-6 sm:max-w-[620px] sm:p-10"
      >
        <div className="flex items-center gap-3">
          <h2 id="demo-heading" className="text-title lg:text-headline">
            ทดลองใช้งาน
          </h2>
          <span className="inline-flex h-[22px] items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
            DEMO MODE
          </span>
        </div>
        <p className="mt-2 text-label font-normal text-muted-foreground lg:mt-3 lg:text-body">
          ข้อมูลทั้งหมดเป็นข้อมูลตัวอย่าง ไม่ต้องใช้รหัสผ่าน
        </p>

        <div className="mt-3.5 flex flex-col gap-2.5 lg:mt-8 lg:gap-3">
          {actions.map((action) =>
            actionButton(
              action,
              action.role,
              action.filled,
              pending === action.role,
              () => void enter(action.role),
            ),
          )}
          {actionButton(customerAction, "Customer", false, pending === "Customer", () => void viewCustomer())}
        </div>

        <Link
          href="/demo"
          className="mt-3 inline-flex h-11 items-center gap-2 self-start font-semibold text-orange-ink hover:underline"
        >
          ดูทั้ง 3 มุมมองพร้อมกัน
          <ArrowRightIcon className="size-5" strokeWidth={1.75} aria-hidden />
        </Link>

        <p className="mt-5 text-[12px] text-muted-foreground lg:mt-10">
          บอทจำลองลูกค้าสั่งอาหารทุก 20–40 วินาที
        </p>
      </section>
    </div>
  );
}
