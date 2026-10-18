"use client";

import { ArrowLeftIcon, BotIcon, ChefHatIcon, LayoutGridIcon, SmartphoneIcon, SoupIcon } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { useCustomerSession } from "@/lib/customer";
import { useDemoSplitScreen, useDemoStatus } from "@/lib/demo";
import { LiveProvider, useLiveOffline } from "@/lib/realtime";
import { BotPanel } from "./bot-panel";
import { Pane, ScaledBoard, useFitScale, ViewFrame } from "./demo-panes";
import { useBotFeed, type BotLine } from "./use-bot-feed";

/** The three designed viewports of frame `d1`, and the scale each is drawn at (`.d1-left` and friends). */
const PHONE = { width: 390, height: 844 } as const;
const KITCHEN = { width: 1280, height: 800 } as const;
const OWNER = { width: 1440, height: 900 } as const;
const PHONE_SCALE = 1.0379;
const KITCHEN_SCALE = 0.5;
const OWNER_SCALE = 0.44444;
/** Frame `d1`'s board: 1440x1060 with its 56px header, and the header is drawn by this page too, so the
 *  board proper is 1440x1004 — the rectangle every pane's position and size is measured against. */
const BOARD_WIDTH = 1440;
const BOARD_HEIGHT = 1060 - 56;

/**
 * `/demo` — the split screen of frame `d1-demo` (ticket 26, spec story 108): the customer's phone, the
 * kitchen board and the owner's desk side by side with the bot's activity panel, all live at once.
 *
 * **Documents, not copies.** The three panes are `<iframe>`s of the real routes. Every one of those
 * screens sizes itself against its viewport (`h-dvh` on the kitchen board and the customer phone, a
 * `lg:grid-cols-[240px_1fr]` shell for the owner), so a second copy inside this document would have to
 * be taught an artificial height — and, worse, it would share this document's single hub connection
 * (ticket 15's `LiveProvider` only reuses a connection for a matching audience inside the same
 * document), so the kitchen pane would hear the owner's groups and the customer pane would hear staff
 * events. An iframe is the same origin through Caddy, keeps its own cookie jar and its own connection,
 * and is exactly what the mockup draws. Each document opens exactly one `/hubs/live` connection.
 *
 * **The switch.** `GET /api/demo/status` is the server's one switch. With Demo off it answers 404, which
 * `useDemoStatus` reads as disabled, and this page then offers nothing: no sign-in, no round, no pane,
 * no live connection — the same reading the login card makes. The demo routes are only ever reached from
 * that card's link, which is drawn under the same condition.
 */
export function DemoScreen() {
  const status = useDemoStatus();

  if (status.isPending) {
    return <DemoSkeleton />;
  }

  if (!status.data?.enabled) {
    return (
      <div className="flex min-h-dvh items-center justify-center p-6">
        <DemoOff />
      </div>
    );
  }

  return <DemoSplitScreen />;
}

function DemoSkeleton() {
  return (
    <div className="flex min-h-dvh flex-col gap-5 p-6">
      <Skeleton className="h-14 rounded-xl" />
      <div className="flex flex-1 gap-5">
        <Skeleton className="h-[500px] w-[431px] rounded-2xl" />
        <Skeleton className="h-[500px] w-[666px] rounded-2xl" />
        <Skeleton className="h-[500px] flex-1 rounded-2xl" />
      </div>
    </div>
  );
}

/**
 * Demo is off (or this server has no demo at all): the page says so and stops. It is the only correct
 * answer even if somebody typed the URL — nothing here is a "no data" state of a working screen.
 */
function DemoOff() {
  return (
    <EmptyState
      icon={<BotIcon className="size-5" strokeWidth={1.75} aria-hidden />}
      title="ไม่มีโหมดเดโมในเซิร์ฟเวอร์นี้"
      description="หน้านี้แสดงได้เฉพาะเมื่อเปิด Demo Mode · เปิดด้วยค่า DEMO_ENABLED=true แล้วลองอีกครั้ง"
      action={
        <Button variant="outline" asChild>
          <Link href="/login">
            <ArrowLeftIcon aria-hidden />
            กลับหน้าเข้าสู่ระบบ
          </Link>
        </Button>
      }
    />
  );
}

/**
 * The desktop board of frame `d1`: sign in as the demo Owner, take the customer round, then lay the three
 * panes out at the frame's own scales with the bot panel beside them.
 *
 * One sign-in covers both staff panes because an Owner may open the kitchen board (that is why the frame
 * can draw them together), and the customer pane needs no sign-in at all — its token is the identity.
 */
function DemoSplitScreen() {
  const bootstrap = useDemoSplitScreen();

  if (bootstrap.isPending) {
    return <DemoSkeleton />;
  }

  if (bootstrap.isError) {
    return (
      <div className="flex min-h-dvh items-center justify-center p-6">
        <EmptyState
          icon={<BotIcon className="size-5" strokeWidth={1.75} aria-hidden />}
          title="เปิดหน้าเดโมไม่สำเร็จ"
          description={
            bootstrap.error instanceof ApiError
              ? bootstrap.error.message
              : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"
          }
          action={
            <Button variant="outline" onClick={() => void bootstrap.refetch()}>
              ลองอีกครั้ง
            </Button>
          }
        />
      </div>
    );
  }

  return <DemoBoard token={bootstrap.data.token} />;
}

function DemoBoard({ token }: { token: string }) {
  const session = useCustomerSession(token);
  // Signing in above is what lets this document itself hold an Owner connection: the bot panel reports
  // the product's own events, and the three panes hold their own connections inside their own documents.
  return (
    <LiveProvider>
      <Board token={token} tableName={session.data?.tableName ?? null} shopName={session.data?.shopName ?? null} />
    </LiveProvider>
  );
}

function Board({
  token,
  tableName,
  shopName,
}: {
  token: string;
  tableName: string | null;
  shopName: string | null;
}) {
  const lines = useBotFeed();
  const wide = useWideScreen();

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="flex h-14 flex-none items-center gap-3 border-b border-hairline bg-surface px-6">
        <span className="flex size-9 flex-none items-center justify-center rounded-md bg-orange-strong text-on-accent">
          <SoupIcon className="size-5" strokeWidth={1.75} aria-hidden />
        </span>
        <div className="flex flex-col">
          <b className="text-base leading-tight font-semibold">FoodQueue Demo</b>
          <span className="text-xs text-muted-foreground">{shopName ?? "เดโม"}</span>
        </div>
        <div className="ms-4 flex items-center gap-2">
          <BotIcon className="size-4 text-muted-foreground" strokeWidth={1.75} aria-hidden />
          <span className="text-[13px] text-muted-foreground">บอทจำลองลูกค้าสั่งอาหารทุก 20–40 วินาที</span>
        </div>
        <div className="ms-auto flex items-center gap-3">
          <LiveTag />
          <span className="inline-flex h-[22px] items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
            DEMO DATA
          </span>
          <Button variant="ghost" size="sm" asChild>
            <Link href="/login">
              <ArrowLeftIcon aria-hidden />
              กลับ
            </Link>
          </Button>
        </div>
      </header>

      {wide === null ? null : wide ? (
        /* The frame's board is a desktop one (1440x1060), scaled down as a whole when the window is
           narrower (1280–1439). */
        <ScaledBoard width={BOARD_WIDTH} height={BOARD_HEIGHT} className="flex-1">
          <Pane
            caption={`ลูกค้า · ${tableName ? `โต๊ะ ${tableName}` : "โต๊ะเดโม"}`}
            icon={<SmartphoneIcon className="size-4" strokeWidth={1.75} aria-hidden />}
            size={PHONE}
            scale={PHONE_SCALE}
          >
            <ViewFrame
              src={`/s/${encodeURIComponent(token)}`}
              title={`มุมมองลูกค้า${tableName ? ` โต๊ะ ${tableName}` : ""}`}
              pane="customer"
            />
          </Pane>

          <div className="flex w-[666px] flex-none flex-col gap-4">
            <Pane
              caption="ครัว"
              icon={<ChefHatIcon className="size-4" strokeWidth={1.75} aria-hidden />}
              size={KITCHEN}
              scale={KITCHEN_SCALE}
            >
              <ViewFrame src="/kitchen" title="จอครัว" pane="kitchen" />
            </Pane>
            <Pane
              caption="เจ้าของร้าน"
              icon={<LayoutGridIcon className="size-4" strokeWidth={1.75} aria-hidden />}
              size={OWNER}
              scale={OWNER_SCALE}
            >
              <ViewFrame src="/owner" title="หน้าภาพรวมเจ้าของร้าน" pane="owner" />
            </Pane>
          </div>

          <BotPanel lines={lines} />
        </ScaledBoard>
      ) : (
        /* Below the frame's own width the same three documents become tabs of one column. Only one
           layout is mounted at a time, so no document is ever loaded twice (and no pane opens a second
           connection to the hub). */
        <DemoTabs token={token} tableName={tableName} lines={lines} />
      )}
    </div>
  );
}

/**
 * Whether the window has the width frame `d1` draws. The breakpoint is applied in JS rather than with
 * `hidden`/`min-[1280px]:flex`, because a layout that is merely visually hidden still loads its iframes:
 * that would hand every pane a second document and a second hub connection.
 */
function useWideScreen(): boolean | null {
  const [wide, setWide] = useState<boolean | null>(null);

  useEffect(() => {
    const query = window.matchMedia("(min-width: 1280px)");
    const update = () => setWide(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);

  return wide;
}

const TAB_LABELS = {
  customer: "ลูกค้า",
  kitchen: "ครัว",
  owner: "เจ้าของร้าน",
  bot: "บอทกำลังทำงาน",
} as const;

type DemoTab = keyof typeof TAB_LABELS;

/** The narrow layout: one pane at a time, fitted to the width, with the bot panel as its own tab. */
function DemoTabs({
  token,
  tableName,
  lines,
}: {
  token: string;
  tableName: string | null;
  lines: BotLine[];
}) {
  const [tab, setTab] = useState<DemoTab>("customer");
  // Each device is fitted against its own design width: the kitchen board and the owner desk are as wide
  // as their real viewports, so scaling the phone's 390px against a 1280px document would overflow.
  const designWidth = tab === "kitchen" ? KITCHEN.width : tab === "owner" ? OWNER.width : PHONE.width;
  const [boxRef, scale] = useFitScale(designWidth);

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3 p-4">
      <div role="group" aria-label="เลือกมุมมอง" className="flex gap-1 rounded-lg bg-sunken p-1">
        {(Object.keys(TAB_LABELS) as DemoTab[]).map((key) => (
          <button
            key={key}
            type="button"
            aria-pressed={tab === key}
            onClick={() => setTab(key)}
            className={`h-9 flex-1 rounded-md px-2 text-[13.5px] font-medium outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${
              tab === key ? "bg-surface font-semibold text-ink" : "text-ink-2"
            }`}
          >
            {TAB_LABELS[key]}
          </button>
        ))}
      </div>

      <div ref={boxRef} className="flex min-h-0 flex-1 flex-col">
        {tab === "bot" ? <BotPanel lines={lines} /> : null}
        {tab === "customer" ? (
          <Pane
            caption={`ลูกค้า · ${tableName ? `โต๊ะ ${tableName}` : "โต๊ะเดโม"}`}
            icon={<SmartphoneIcon className="size-4" strokeWidth={1.75} aria-hidden />}
            size={PHONE}
            scale={scale}
            boxClassName="mx-auto"
          >
            <ViewFrame
              src={`/s/${encodeURIComponent(token)}`}
              title={`มุมมองลูกค้า${tableName ? ` โต๊ะ ${tableName}` : ""}`}
              pane="customer"
            />
          </Pane>
        ) : null}
        {tab === "kitchen" ? (
          <Pane
            caption="ครัว"
            icon={<ChefHatIcon className="size-4" strokeWidth={1.75} aria-hidden />}
            size={KITCHEN}
            scale={scale}
            boxClassName="mx-auto"
          >
            <ViewFrame src="/kitchen" title="จอครัว" pane="kitchen" />
          </Pane>
        ) : null}
        {tab === "owner" ? (
          <Pane
            caption="เจ้าของร้าน"
            icon={<LayoutGridIcon className="size-4" strokeWidth={1.75} aria-hidden />}
            size={OWNER}
            scale={scale}
            boxClassName="mx-auto"
          >
            <ViewFrame src="/owner" title="หน้าภาพรวมเจ้าของร้าน" pane="owner" />
          </Pane>
        ) : null}
      </div>
    </div>
  );
}

/** Whether this document's own connection to `/hubs/live` is up — the panel's feed depends on it. */
function LiveTag() {
  const isOffline = useLiveOffline();

  return (
    <span
      role="status"
      className={`inline-flex h-8 items-center gap-2 rounded-full px-3 text-[13px] font-semibold whitespace-nowrap ${
        isOffline ? "bg-sunken text-ink-2" : "bg-ready-soft text-ready-ink"
      }`}
    >
      <i className={`size-[7px] rounded-full ${isOffline ? "bg-served-dot" : "bg-ready-dot"}`} aria-hidden />
      {isOffline ? "ออฟไลน์ · ข้อมูลอาจไม่ใหม่" : "เชื่อมต่อแบบเรียลไทม์"}
    </span>
  );
}
