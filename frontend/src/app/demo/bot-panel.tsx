"use client";

import { ArmchairIcon, BanknoteIcon, ChefHatIcon, ClipboardListIcon, LayoutGridIcon, SmartphoneIcon } from "lucide-react";
import { formatTime } from "@/lib/format";
import type { BotLine, BotLineIcon } from "./use-bot-feed";

const LINE_ICONS = {
  order: ClipboardListIcon,
  kitchen: ChefHatIcon,
  table: ArmchairIcon,
  paid: BanknoteIcon,
} as const satisfies Record<BotLineIcon, typeof ClipboardListIcon>;

/** Frame `d1`'s `.d1-guide`: what each pane is about to do, once, under the feed. */
const GUIDE = [
  { icon: SmartphoneIcon, who: "ลูกค้า", what: ": ส่งออเดอร์" },
  { icon: ChefHatIcon, who: "ครัว", what: ": การ์ดใหม่โผล่ทันที" },
  { icon: LayoutGridIcon, who: "เจ้าของร้าน", what: ": ยอดและโต๊ะอัปเดต" },
] as const;

/**
 * The right-hand panel of frame `d1-demo`: a live timeline of what the demo bot is doing, built from the
 * product's own hub events (see `use-bot-feed`), plus the frame's footer caption and guide.
 *
 * With nothing to report yet it says so and repeats the bot's cycle rather than inventing a line: the
 * feed is a report of events that arrived on this page, never a decoration.
 */
export function BotPanel({ lines }: { lines: BotLine[] }) {
  return (
    <aside className="flex min-w-0 flex-1 flex-col gap-4 rounded-2xl border border-hairline bg-surface px-4 py-5">
      <div className="flex flex-none items-center gap-2">
        <i className="size-[7px] flex-none rounded-full bg-ready-dot" aria-hidden />
        <h2 className="text-base leading-normal font-semibold">บอทกำลังทำงาน</h2>
      </div>

      {lines.length === 0 ? (
        <p className="flex flex-1 items-center text-[13px] leading-[1.55] text-muted-foreground">
          ยังไม่มีการเคลื่อนไหวในรอบนี้ · บอทจำลองลูกค้าสั่งอาหารทุก 20–40 วินาที
        </p>
      ) : (
        <ul
          aria-label="กิจกรรมล่าสุดจากบอท"
          className="relative flex flex-1 flex-col justify-evenly gap-2 border-s border-hairline ps-[30px]"
        >
          {lines.map((line) => {
            const Icon = LINE_ICONS[line.icon];
            return (
              <li key={line.key} className="relative flex max-h-[92px] items-center">
                <span
                  className="absolute -start-[30px] flex size-[22px] items-center justify-center rounded-full border border-hairline bg-surface text-ink-2"
                  aria-hidden
                >
                  <Icon className="size-3" strokeWidth={1.75} />
                </span>
                <span className="text-[13px] leading-[1.55]">
                  <span className="font-mono text-muted-foreground">{formatTime(line.at)}</span> · {line.text}
                </span>
              </li>
            );
          })}
        </ul>
      )}

      <div className="flex flex-none flex-col gap-2.5 border-t border-hairline pt-3.5">
        <p className="text-[12px] leading-[1.45] text-muted-foreground">บอทจำลองลูกค้าสั่งอาหารทุก 20–40 วินาที</p>
        {GUIDE.map(({ icon: Icon, who, what }) => (
          <div key={who} className="flex items-center gap-2">
            <Icon className="size-4 flex-none text-muted-foreground" strokeWidth={1.75} aria-hidden />
            <span className="text-[13px] leading-[1.55]">
              <b className="font-semibold">{who}</b>
              {what}
            </span>
          </div>
        ))}
      </div>
    </aside>
  );
}
