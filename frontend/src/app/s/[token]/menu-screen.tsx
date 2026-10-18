"use client";

import {
  BeefIcon,
  CarrotIcon,
  CheckIcon,
  ClipboardListIcon,
  CupSodaIcon,
  DrumstickIcon,
  FishIcon,
  HamIcon,
  IceCreamBowlIcon,
  PlusIcon,
  QrCodeIcon,
  SoupIcon,
  UtensilsIcon,
} from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api";
import { CartProvider } from "@/lib/cart";
import { useCustomerMenu, useCustomerSession } from "@/lib/customer";
import { LiveProvider } from "@/lib/realtime";
import { formatBaht } from "@/lib/format";
import type { CustomerCategoryDto, CustomerMenuItemDto, CustomerSessionDto } from "@/lib/types";
import { CartBar, OrderWindowBanner } from "./cart-bar";
import { CartSheet } from "./cart-sheet";
import { QuantityStepper } from "./quantity-stepper";
import { CustomerLiveSync, OfflineNotice, TableOrders } from "./orders-tab";

/**
 * Copy of the customer screens, verbatim from the approved frames (`notes/10-THAI-COPY.md` §c1-menu,
 * §c6-end), so a chip, a badge or a sold-out row reads exactly as it does in `design/out/`.
 * Nothing here counts down: this screen shows no clock at all, it only receives the server's.
 */
const INCLUDED_IN_BUFFET = "รวมในบุฟเฟ่ต์";
const SOLD_OUT = "หมด";
const MENU_TAB = "เมนู";
const ORDERS_TAB = "ออเดอร์";
const SECTION_ID = (categoryId: number) => `customer-category-${categoryId}`;

/**
 * `/s/{token}` — frames `c1-menu` (the menu) and `c6-end` (the QR does not exist).
 *
 * The customer scans a QR that identifies one round of one table and sees the menu their package may
 * order: buffet items up to the session's package rank, plus every active à la carte item with its
 * price. Sold-out rows stay listed, greyed, with the "หมด" label. There is no countdown and no bill
 * anywhere; the cart, the round number and the "หมดเวลาสั่งอาหาร" state come from ticket 10 mounted
 * beside this screen, and the orders tab from ticket 13.
 */
export function CustomerMenuScreen({ token }: { token: string }) {
  const session = useCustomerSession(token);
  const menu = useCustomerMenu(token);
  const [tab, setTab] = useState<"menu" | "orders">("menu");
  const [cartOpen, setCartOpen] = useState(false);

  if (session.isPending || menu.isPending) {
    return <MenuSkeleton />;
  }

  // A QR that does not exist (`SESSION_NOT_FOUND`) and one whose round is over (`SESSION_CLOSED`) are
  // both the end of this screen's job; the way back is the browser's, so no invented button.
  if (session.isError || menu.isError) {
    return <QrProblem error={session.error ?? menu.error} />;
  }

  // The cart is per device and lives in this browser (`fq-cart-{token}`); the limits and the ordering
  // window both come from the server's session payload, so no clock of ours decides anything.
  // The hub connects only for a round that loaded (the hub refuses a closed one); ticket 13 keeps the
  // table list and the sold-out flags live through it.
  return (
    <LiveProvider token={token}>
      <CustomerLiveSync token={token} />
      <CartProvider
        token={token}
        maxQtyPerItem={session.data.maxQtyPerItemPerOrder}
        maxItems={session.data.maxItemsPerOrder}
        serverNow={session.data.serverNow}
        endsAt={session.data.endsAt}
        warningMinutes={session.data.lastOrderWarningMinutes}
      >
        {tab === "orders" ? (
          <Shell session={session.data} tab={tab} onTab={setTab} onOpenCart={() => setCartOpen(true)} cartOpen={cartOpen} onCartOpenChange={setCartOpen}>
            <TableOrders token={token} menu={menu.data} />
          </Shell>
        ) : (
          <Shell session={session.data} tab={tab} onTab={setTab} onOpenCart={() => setCartOpen(true)} cartOpen={cartOpen} onCartOpenChange={setCartOpen}>
            <OrderWindowBanner />
            <MenuBody categories={menu.data.categories} maxQtyPerItemPerOrder={session.data.maxQtyPerItemPerOrder} />
          </Shell>
        )}
      </CartProvider>
    </LiveProvider>
  );
}

/**
 * The 390×844 customer shell: shop banner, body, tab bar. The cart owner mounts its floating bar
 * inside this column, which is why the body is the only part that scrolls.
 */
function Shell({
  session,
  tab,
  onTab,
  onOpenCart,
  cartOpen,
  onCartOpenChange,
  children,
}: {
  session: CustomerSessionDto;
  tab: "menu" | "orders";
  onTab: (tab: "menu" | "orders") => void;
  onOpenCart: () => void;
  cartOpen: boolean;
  onCartOpenChange: (open: boolean) => void;
  children: React.ReactNode;
}) {
  return (
    <div className="flex h-dvh flex-col bg-ground">
      <ShopBanner session={session} />
      <OfflineNotice />
      {children}
      {/* `c3-orders` has no cart bar: the table list is read-only, the cart lives on the menu tab. */}
      {tab === "menu" && (
        <div className="flex-none px-4 pb-2">
          <CartBar onOpen={onOpenCart} />
        </div>
      )}
      <TabBar tab={tab} onTab={onTab} />
      <CartSheet open={cartOpen} onOpenChange={onCartOpenChange} />
    </div>
  );
}

/** `DESIGN.md` Shop banner: 128px, shop name over the orange field, table and package as white chips. */
function ShopBanner({ session }: { session: CustomerSessionDto }) {
  return (
    <header className="relative h-32 flex-none overflow-hidden bg-orange-strong px-5 pt-14 text-on-accent">
      {session.bannerImagePath ? (
        // The owner's upload is served by the API at /api/uploads/..., so Next's optimizer has nothing to do.
        // eslint-disable-next-line @next/next/no-img-element
        <img src={session.bannerImagePath} alt="" className="absolute inset-0 size-full object-cover" />
      ) : (
        <SoupIcon
          className="absolute -right-3.5 -bottom-7.5 size-[150px] text-orange-hover"
          strokeWidth={1.25}
          aria-hidden
        />
      )}
      <div className="relative">
        <span className="absolute -top-11 left-0 inline-flex h-[26px] items-center rounded-full border border-hairline bg-surface px-2.5 text-[12.5px] leading-none font-semibold text-ink">
          โต๊ะ {session.tableName}
        </span>
        <span className="absolute -top-11 right-0 inline-flex h-[26px] items-center gap-1.5 rounded-full border border-hairline bg-surface px-2.5 text-[12.5px] leading-none font-semibold text-ink">
          <i className="size-1.5 flex-none rounded-full bg-orange" aria-hidden />
          {session.packageName} · {session.adults} ท่าน
          {session.children > 0 ? ` · ${session.children} เด็ก` : ""}
        </span>
        <h1 className="text-[28px] leading-tight font-semibold tracking-[-0.01em]">{session.shopName}</h1>
        <p className="mt-0.5 text-sm leading-normal">ชาบูบุฟเฟ่ต์ · สั่งอาหารผ่าน QR</p>
      </div>
    </header>
  );
}

/**
 * The sticky category rail plus the sections it navigates. The rail follows the list (the section under
 * it is the selected tab) and tapping a tab scrolls that section under the rail, which is what makes the
 * tabs useful on a 390px screen: the whole menu is one column of sections, never one filtered page.
 */
function MenuBody({
  categories,
  maxQtyPerItemPerOrder,
}: {
  categories: readonly CustomerCategoryDto[];
  maxQtyPerItemPerOrder: number;
}) {
  const [active, setActive] = useState(categories[0]?.id ?? 0);
  const rail = useRef<HTMLDivElement>(null);

  // The active tab can be changed by scrolling, so keep it inside the rail's own horizontal view.
  useEffect(() => {
    rail.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: "nearest", inline: "center" });
  }, [active]);

  const onScroll = (event: React.UIEvent<HTMLDivElement>) => {
    const container = event.currentTarget;
    const top = container.getBoundingClientRect().top;
    // The last section cannot always reach the top (the list runs out), so the bottom of the list
    // selects it — otherwise its tab would be unreachable by scrolling.
    if (container.scrollTop + container.clientHeight >= container.scrollHeight - 2) {
      setActive(categories[categories.length - 1]?.id ?? active);
      return;
    }

    let current = active;
    for (const category of categories) {
      const section = document.getElementById(SECTION_ID(category.id));
      // The rail is a sibling above the scroller, not an overlay, so a section is current from the
      // moment its heading reaches the top of the scrollport; the allowance absorbs sub-pixel scroll.
      if (section && section.getBoundingClientRect().top - top <= 1) current = category.id;
    }
    if (current !== active) setActive(current);
  };

  return (
    <>
      <CategoryTabs ref={rail} categories={categories} active={active} onSelect={setActive} />
      <div className="flex-1 overflow-y-auto overscroll-contain px-4 pb-24" onScroll={onScroll}>
        {categories.map((category, index) => (
          <CategorySection
            key={category.id}
            category={category}
            maxQtyPerItemPerOrder={maxQtyPerItemPerOrder}
            // `c1-menu` states the per-item limit once, in the first section's header.
            showLimit={index === 0}
          />
        ))}
      </div>
    </>
  );
}

function CategoryTabs({
  ref,
  categories,
  active,
  onSelect,
}: {
  ref: React.RefObject<HTMLDivElement | null>;
  categories: readonly CustomerCategoryDto[];
  active: number;
  onSelect: (id: number) => void;
}) {
  const jump = (id: number) => {
    onSelect(id);
    document.getElementById(SECTION_ID(id))?.scrollIntoView({ block: "start" });
  };

  return (
    <nav className="sticky top-0 z-10 flex-none overflow-x-auto border-b border-hairline bg-surface px-4">
      <div className="flex gap-6" ref={ref}>
        {categories.map((category) => (
          <button
            key={category.id}
            type="button"
            onClick={() => jump(category.id)}
            aria-current={category.id === active ? "true" : undefined}
            className={`relative -mb-px py-2.5 text-[15px] whitespace-nowrap outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
              category.id === active ? "font-semibold text-ink" : "font-medium text-muted-foreground"
            }`}
          >
            {category.name}
            {category.id === active && (
              <span className="absolute inset-x-0 bottom-0 h-0.5 rounded-[2px] bg-orange-strong" />
            )}
          </button>
        ))}
      </div>
    </nav>
  );
}

function CategorySection({
  category,
  maxQtyPerItemPerOrder,
  showLimit,
}: {
  category: CustomerCategoryDto;
  maxQtyPerItemPerOrder: number;
  showLimit: boolean;
}) {
  return (
    <section id={SECTION_ID(category.id)} className="pt-5 pb-1.5">
      <div className="flex items-baseline justify-between gap-3">
        <h2 className="text-title">{category.name}</h2>
        {showLimit && (
          <span className="text-[13px] font-medium text-muted-foreground">
            สั่งได้ {maxQtyPerItemPerOrder} ต่อเมนู
          </span>
        )}
      </div>
      <ul>
        {category.items.map((item) => (
          <MenuRow key={item.id} item={item} categoryName={category.name} />
        ))}
      </ul>
    </section>
  );
}

/**
 * One menu row of `c1-menu`: photo (or a category icon), name, the buffet badge or the à la carte
 * price, and the "+" affordance. A sold-out row is greyed and labelled; its control is inert, so a
 * pinkie-press on a dish the shop has run out of cannot look like it did something.
 */
function MenuRow({ item, categoryName }: { item: CustomerMenuItemDto; categoryName: string }) {
  const soldOut = !item.isAvailable;

  return (
    <li className="flex items-center gap-3.5 border-t border-hairline py-3 first:border-t-0">
      <MenuThumb item={item} categoryName={categoryName} soldOut={soldOut} />
      <div className="min-w-0 flex-1">
        <div className={`text-base leading-snug font-semibold ${soldOut ? "text-muted-foreground" : "text-ink"}`}>
          {item.name}
        </div>
        {soldOut ? (
          <span className="mt-0.5 inline-flex items-center rounded-full bg-sunken px-2.5 py-[3px] text-[12.5px] leading-none font-semibold text-muted-foreground">
            {SOLD_OUT}
          </span>
        ) : (
          <MenuRowMeta item={item} />
        )}
      </div>
      {/* An orderable row carries the +/− control; a sold-out row keeps the inert glyph, which is
          what says "สั่งไม่ได้" beyond colour alone. */}
      {soldOut ? <SoldOutControl /> : <QuantityStepper item={item} compact />}
    </li>
  );
}

/** Buffet rows are inside the package price; à la carte rows must say what they add to the bill. */
function MenuRowMeta({ item }: { item: CustomerMenuItemDto }) {
  if (item.kind === "Buffet") {
    return (
      <span className="mt-0.5 inline-flex items-center gap-1 text-[12.5px] leading-none font-medium text-orange-ink">
        <CheckIcon className="size-4 flex-none" strokeWidth={1.75} aria-hidden />
        {INCLUDED_IN_BUFFET}
      </span>
    );
  }

  return <span className="mt-0.5 block text-[12.5px] font-medium text-ink-2">{formatBaht(item.price)}</span>;
}

/**
 * The "+" affordance belongs to the cart owner (ticket 10), so the menu screen renders none: a button
 * that looked live but did nothing would be a lie, and the ticket says this screen shows the menu only.
 * The one exception is a sold-out row, where the muted glyph is what carries "สั่งไม่ได้" beyond colour.
 */
function SoldOutControl() {
  return (
    <span
      aria-hidden
      className="flex size-11 flex-none items-center justify-center rounded-full border border-hairline bg-sunken text-muted-foreground"
    >
      <PlusIcon className="size-5" strokeWidth={1.75} />
    </span>
  );
}

/** The no-photo fallback: a category icon on a quiet tile, the same rule as the owner's menu table. */
const TILE_ICON_COMPONENTS = {
  น้ำซุป: SoupIcon,
  หมู: HamIcon,
  เนื้อวัว: BeefIcon,
  ทะเล: FishIcon,
  "ลูกชิ้น/ของทอด": DrumstickIcon,
  ผัก: CarrotIcon,
  ของหวาน: IceCreamBowlIcon,
  เครื่องดื่ม: CupSodaIcon,
} as const;

function MenuThumb({
  item,
  categoryName,
  soldOut,
}: {
  item: CustomerMenuItemDto;
  categoryName: string;
  soldOut: boolean;
}) {
  const Icon = TILE_ICON_COMPONENTS[categoryName as keyof typeof TILE_ICON_COMPONENTS] ?? UtensilsIcon;

  return (
    <span
      className={`flex size-[72px] flex-none items-center justify-center overflow-hidden rounded-lg bg-sunken text-ink-2 ${
        soldOut ? "opacity-55" : ""
      }`}
    >
      {item.imagePath ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={item.imagePath} alt="" className="size-full object-cover" />
      ) : (
        <Icon className="size-[30px]" strokeWidth={1.5} aria-hidden />
      )}
    </span>
  );
}

/** `c1-menu`'s tab bar: เมนู / ออเดอร์ — no bill tab and no countdown anywhere. */
function TabBar({ tab, onTab }: { tab: "menu" | "orders"; onTab: (tab: "menu" | "orders") => void }) {
  return (
    <nav className="grid h-[68px] flex-none grid-cols-2 border-t border-hairline bg-surface pb-2">
      <TabButton
        icon={<UtensilsIcon className="size-6" strokeWidth={1.75} />}
        label={MENU_TAB}
        active={tab === "menu"}
        onClick={() => onTab("menu")}
      />
      <TabButton
        icon={<ClipboardListIcon className="size-6" strokeWidth={1.75} />}
        label={ORDERS_TAB}
        active={tab === "orders"}
        onClick={() => onTab("orders")}
      />
    </nav>
  );
}

function TabButton({
  icon,
  label,
  active,
  onClick,
}: {
  icon: React.ReactNode;
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? "page" : undefined}
      className={`flex flex-col items-center justify-center gap-0.5 text-xs outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
        active ? "font-semibold text-orange-ink" : "font-medium text-muted-foreground"
      }`}
    >
      {icon}
      {label}
    </button>
  );
}

/**
 * A QR that is not there, or a round that is closed and can be paid but not ordered from. The
 * "ขอบคุณที่ใช้บริการ" screen of `c6-end` belongs to the paid flow (ticket 10/18), not here, so this
 * state states the fact and offers a refetch — the guest may be holding a slip the staff just printed.
 */
function QrProblem({ error }: { error: unknown }) {
  const queryClient = useQueryClient();
  const unknownQr = error instanceof ApiError && error.code === "SESSION_NOT_FOUND";

  return (
    <div className="flex h-dvh flex-col items-center justify-center gap-3 bg-ground px-8 text-center">
      <span className="flex size-[72px] items-center justify-center rounded-full bg-sunken text-ink-2">
        <QrCodeIcon className="size-8" strokeWidth={1.5} aria-hidden />
      </span>
      <h1 className="text-headline">{unknownQr ? "QR ไม่ถูกต้อง" : "ปิดโต๊ะแล้ว"}</h1>
      <p className="text-body text-ink-2">
        {error instanceof ApiError ? error.message : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
      </p>
      <button
        type="button"
        onClick={() =>
          void queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] === "customer" })
        }
        className="mt-2 text-[15px] font-semibold text-orange-ink underline-offset-4 hover:underline"
      >
        ลองอีกครั้ง
      </button>
    </div>
  );
}

/** A shape of the real screen, not a blank page: the banner band, the rail and rows of the same height. */
function MenuSkeleton() {
  return (
    <div className="flex h-dvh flex-col bg-ground">
      <div className="h-32 flex-none animate-pulse bg-sunken" />
      <div className="h-11 flex-none border-b border-hairline bg-surface" />
      <div className="flex flex-1 flex-col gap-3 p-4">
        <div className="h-6 w-20 animate-pulse rounded-md bg-sunken" />
        {[0, 1, 2].map((row) => (
          <div key={row} className="flex items-center gap-3.5">
            <div className="size-[72px] flex-none animate-pulse rounded-lg bg-sunken" />
            <div className="flex flex-1 flex-col gap-2">
              <div className="h-4 w-40 animate-pulse rounded-md bg-sunken" />
              <div className="h-3 w-24 animate-pulse rounded-md bg-sunken" />
            </div>
          </div>
        ))}
      </div>
      <div className="h-[68px] flex-none border-t border-hairline bg-surface" />
    </div>
  );
}
