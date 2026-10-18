"use client";

import { PencilIcon, UtensilsIcon, WifiOffIcon } from "lucide-react";
import { useCustomerLive, useCustomerOrders } from "@/lib/customer";
import { formatBahtWhole } from "@/lib/format";
import { useLiveOffline } from "@/lib/realtime";
import type { CustomerMenuDto, CustomerOrderItemDto } from "@/lib/types";

/** Copy of `c3-orders`, verbatim from `notes/10-THAI-COPY.md`; the offline line follows the staff screens' wording. */
const TABLE_ORDERS = "ออเดอร์ของโต๊ะ";
const OFFLINE = "ออฟไลน์ · ข้อมูลอาจไม่ใหม่";

/**
 * Wires this token's caches to the hub (ticket 13): a friend's submit or the shop's edit/cancel refreshes the
 * table list, and a sold-out flip greys the menu row without a reload. Renders nothing; mount it once inside
 * `<LiveProvider token={token}>`.
 */
export function CustomerLiveSync({ token }: { token: string }) {
  useCustomerLive(token);
  return null;
}

/**
 * Spec 26: while the hub is down the screen says so, because what it shows may be stale. On reconnect the
 * live connection refetches every query, so the notice disappears together with the stale data.
 */
export function OfflineNotice() {
  const isOffline = useLiveOffline();
  if (!isOffline) return null;

  return (
    <div
      role="status"
      className="flex flex-none items-center justify-center gap-2 border-b border-hairline bg-sunken px-4 py-2 text-[13px] font-semibold text-ink-2"
    >
      <WifiOffIcon className="size-4" strokeWidth={1.75} aria-hidden />
      {OFFLINE}
    </div>
  );
}

/**
 * `c3-orders` — everything the table ordered (every phone on this token) as one list: quantity summed per
 * dish, the note kept on its own row, a small photo. No rounds, no status and no bill (spec 20); cancelled
 * orders and removed lines are gone because the server no longer returns them (spec 22).
 *
 * The à la carte price beside the name is the menu's, the same figure the guest saw when ordering —
 * it tells them the dish is charged separately, it is not a running total.
 */
export function TableOrders({ token, menu }: { token: string; menu: CustomerMenuDto }) {
  const orders = useCustomerOrders(token);
  const prices = new Map(menu.categories.flatMap((category) => category.items.map((item) => [item.id, item.price])));
  const total = orders.data?.items.reduce((sum, item) => sum + item.quantity, 0) ?? 0;

  return (
    <div className="flex-1 overflow-y-auto overscroll-contain px-4 pb-4">
      <div className="flex items-baseline justify-between gap-3 pt-3.5 pb-2">
        <h2 className="text-title">{TABLE_ORDERS}</h2>
        {orders.isSuccess && orders.data.items.length > 0 && (
          <span className="text-[13px] text-muted-foreground tabular-nums">รวม {total} รายการ</span>
        )}
      </div>

      {orders.isPending ? (
        <OrdersSkeleton />
      ) : orders.isError ? (
        <section className="rounded-xl border border-hairline bg-surface px-4 py-6 text-center">
          <p className="text-body text-ink-2">โหลดรายการของโต๊ะไม่สำเร็จ</p>
          <button
            type="button"
            onClick={() => void orders.refetch()}
            className="mt-2 text-[15px] font-semibold text-orange-ink underline-offset-4 hover:underline"
          >
            ลองอีกครั้ง
          </button>
        </section>
      ) : orders.data.items.length === 0 ? (
        <section className="flex flex-col items-center gap-1 rounded-xl border border-hairline bg-surface px-6 py-8 text-center">
          <span className="text-body font-semibold text-ink">ยังไม่มีรายการ</span>
          <span className="text-[13px] text-muted-foreground">เมนูที่โต๊ะส่งแล้วจะขึ้นที่นี่โดยไม่ต้องรีเฟรช</span>
        </section>
      ) : (
        <section className="rounded-xl border border-hairline bg-surface px-4 py-2">
          <ul>
            {orders.data.items.map((item) => (
              <OrderLine
                key={`${item.menuItemId}:${item.note ?? ""}`}
                item={item}
                price={item.kind === "AlaCarte" ? prices.get(item.menuItemId) : undefined}
              />
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function OrderLine({ item, price }: { item: CustomerOrderItemDto; price: number | undefined }) {
  return (
    <li className="flex items-center gap-3 border-t border-hairline py-[7px] text-[15px] leading-normal first:border-t-0">
      <span className="flex size-9 flex-none items-center justify-center overflow-hidden rounded-sm bg-sunken text-ink-2">
        {item.imagePath ? (
          // Served by the API at /api/uploads/..., so Next's optimizer has nothing to do.
          // eslint-disable-next-line @next/next/no-img-element
          <img src={item.imagePath} alt="" className="size-full object-cover" />
        ) : (
          <UtensilsIcon className="size-[18px]" strokeWidth={1.5} aria-hidden />
        )}
      </span>
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="text-ink">
          {item.name}
          {price !== undefined && (
            <span className="ml-1 text-xs text-muted-foreground tabular-nums">{formatBahtWhole(price)}</span>
          )}
        </span>
        {item.note && (
          <span className="flex items-center gap-1 text-[12.5px] leading-[1.45] text-ink-2">
            <PencilIcon className="size-3.5 flex-none" strokeWidth={1.75} aria-hidden />
            {item.note}
          </span>
        )}
      </div>
      <span className="flex-none text-muted-foreground tabular-nums">×{item.quantity}</span>
    </li>
  );
}

function OrdersSkeleton() {
  return (
    <section className="rounded-xl border border-hairline bg-surface px-4 py-2">
      {[0, 1, 2].map((row) => (
        <div key={row} className="flex items-center gap-3 py-[7px]">
          <div className="size-9 flex-none animate-pulse rounded-sm bg-sunken" />
          <div className="h-4 w-40 animate-pulse rounded-md bg-sunken" />
        </div>
      ))}
    </section>
  );
}
