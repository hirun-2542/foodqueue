"use client";

import {
  ArmchairIcon,
  BookOpenIcon,
  ChefHatIcon,
  ClipboardListIcon,
  LayoutGridIcon,
  Loader2Icon,
  LogOutIcon,
  PackageIcon,
  SettingsIcon,
  SoupIcon,
} from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { type ReactNode, useEffect } from "react";
import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { StaffRole } from "@/lib/types";
import { isUnauthenticated, staffRoutes, useLogout, useMe } from "@/lib/auth";
import { NewOrderAlertScope } from "./new-order-alert";

const roleLabel: Record<StaffRole, string> = { Owner: "เจ้าของร้าน", Kitchen: "ครัว" };

/**
 * The navigation is exactly the set of staff routes that exist today; each later ticket adds its own
 * entry together with its route. The full sidebar from `o01-overview` arrives with those routes.
 */
const navItems = [
  { href: staffRoutes.owner, label: "ภาพรวม", icon: LayoutGridIcon, ownerOnly: true },
  { href: staffRoutes.orders, label: "ออเดอร์", icon: ClipboardListIcon, ownerOnly: true },
  { href: staffRoutes.menu, label: "เมนูอาหาร", icon: BookOpenIcon, ownerOnly: true },
  { href: staffRoutes.packages, label: "แพ็กเกจ", icon: PackageIcon, ownerOnly: true },
  { href: staffRoutes.tables, label: "โต๊ะ", icon: ArmchairIcon, ownerOnly: true },
  { href: staffRoutes.settings, label: "ตั้งค่า", icon: SettingsIcon, ownerOnly: true },
  { href: staffRoutes.kitchen, label: "จอครัว", icon: ChefHatIcon, ownerOnly: false },
];

/**
 * `/owner` is a prefix of every owner sub-route, so a plain `startsWith` lights up ภาพรวม on
 * `/owner/tables` as well. Only the longest matching route counts as current.
 */
function isCurrentRoute(pathname: string, href: string): boolean {
  if (!pathname.startsWith(href)) return false;
  const longest = navItems
    .map((item) => item.href)
    .filter((candidate) => pathname.startsWith(candidate))
    .reduce((a, b) => (a.length >= b.length ? a : b));
  return href === longest;
}

function Brand() {
  return (
    <div className="flex items-center gap-3">
      <span className="flex size-9 flex-none items-center justify-center rounded-md bg-orange-strong text-on-accent">
        <SoupIcon className="size-5" strokeWidth={1.75} aria-hidden />
      </span>
      <div className="flex flex-col">
        <b className="text-body leading-tight font-semibold">FoodQueue</b>
        <span className="text-xs text-muted-foreground">ระบบสั่งอาหารผ่าน QR</span>
      </div>
    </div>
  );
}

/**
 * Auth guard for every staff screen: nobody signed in goes to `/login`, and a Kitchen account never
 * sees `/owner/*` (it is sent to `/kitchen`). The decision comes from `GET /api/auth/me`'s `role`.
 */
export default function StaffLayout({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const { data: me, error, isPending } = useMe();
  const logout = useLogout();

  const signedOut = isUnauthenticated(error);
  const wrongRole = me?.role === "Kitchen" && pathname.startsWith("/owner");

  useEffect(() => {
    if (isPending) return;
    if (signedOut) {
      router.replace("/login");
    } else if (wrongRole) {
      router.replace(staffRoutes.kitchen);
    }
  }, [isPending, signedOut, wrongRole, router]);

  // Nothing is rendered while the answer is unknown or a redirect is in flight, so a protected
  // screen never flashes before the guard has decided.
  if (isPending) {
    return (
      <div className="flex min-h-dvh items-center justify-center">
        <Skeleton className="h-10 w-52" />
      </div>
    );
  }

  if (signedOut || wrongRole) {
    return (
      <div className="flex min-h-dvh items-center justify-center gap-2 text-label text-muted-foreground">
        <Loader2Icon className="size-4 animate-spin" aria-hidden />
        กำลังพาไปหน้าที่เหมาะสม
      </div>
    );
  }

  if (!me) {
    return (
      <div className="p-6 lg:p-10">
        <EmptyState
          icon={<LogOutIcon className="size-5" strokeWidth={1.75} aria-hidden />}
          title="โหลดข้อมูลผู้ใช้ไม่ได้"
          description="เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ จึงยังยืนยันสิทธิ์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"
          action={
            <Button variant="outline" onClick={() => window.location.reload()}>
              ลองอีกครั้ง
            </Button>
          }
        />
      </div>
    );
  }

  // The kitchen display is a full-screen board with its own header bar (frame k1): a landscape
  // tablet needs all 1280px for the three columns, so no sidebar or tab bar there.
  if (pathname.startsWith(staffRoutes.kitchen)) return children;

  const visibleNav = navItems.filter((item) => !item.ownerOnly || me.role === "Owner");

  return (
    <div className="flex min-h-dvh flex-col lg:grid lg:grid-cols-[240px_1fr]">
      <aside className="hidden border-r border-border bg-surface p-5 lg:flex lg:flex-col lg:gap-1">
        <div className="px-2 pb-5">
          <Brand />
        </div>

        {visibleNav.map(({ href, label, icon: Icon }) => {
          const active = isCurrentRoute(pathname, href);
          return (
            <Link
              key={href}
              href={href}
              aria-current={active ? "page" : undefined}
              className={`flex h-11 items-center gap-3 rounded-lg px-3 font-medium ${
                active ? "bg-orange-soft font-semibold text-orange-ink" : "text-ink-2 hover:bg-sunken"
              }`}
            >
              <Icon className="size-5" strokeWidth={1.75} aria-hidden />
              {label}
            </Link>
          );
        })}

        <div className="mt-auto flex items-center gap-2.5 border-t border-border px-2 pt-3">
          <span className="flex size-8 flex-none items-center justify-center rounded-full bg-sunken text-label font-semibold text-ink-2">
            {me.displayName.charAt(0)}
          </span>
          <div className="flex min-w-0 flex-1 flex-col">
            <b className="truncate text-label leading-tight">{me.displayName}</b>
            <span className="text-xs text-muted-foreground">{roleLabel[me.role]}</span>
          </div>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="ออกจากระบบ"
            disabled={logout.isPending}
            onClick={() => logout.mutate()}
          >
            <LogOutIcon aria-hidden />
          </Button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-between gap-3 border-b border-border bg-surface px-4 py-3 lg:hidden">
          <Brand />
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="ออกจากระบบ"
            disabled={logout.isPending}
            onClick={() => logout.mutate()}
          >
            <LogOutIcon aria-hidden />
          </Button>
        </header>

        {/* Ticket 15: the owner-wide new-order popup (o20/o21) and the one live connection owner screens share. */}
        <main className="min-w-0 flex-1 px-4 pt-5 pb-24 lg:px-8 lg:pt-0 lg:pb-10">
          <NewOrderAlertScope enabled={me.role === "Owner"}>{children}</NewOrderAlertScope>
        </main>
      </div>

      <nav className="fixed inset-x-0 bottom-0 z-10 grid border-t border-border bg-surface pb-2 lg:hidden">
        <div className="grid" style={{ gridTemplateColumns: `repeat(${visibleNav.length}, 1fr)` }}>
          {visibleNav.map(({ href, label, icon: Icon }) => {
            const active = isCurrentRoute(pathname, href);
            return (
              <Link
                key={href}
                href={href}
                aria-current={active ? "page" : undefined}
                className={`flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-medium ${
                  active ? "text-orange-ink" : "text-muted-foreground"
                }`}
              >
                <Icon className="size-5" strokeWidth={1.75} aria-hidden />
                {label}
              </Link>
            );
          })}
        </div>
      </nav>
    </div>
  );
}
