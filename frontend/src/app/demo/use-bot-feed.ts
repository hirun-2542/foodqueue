"use client";

import { useCallback, useRef, useState } from "react";
import { apiFetch } from "@/lib/api";
import { useLiveEvent } from "@/lib/realtime";
import type { SessionDetailDto } from "@/lib/types";

/** Which line of the feed this is; it only chooses the icon on the timeline. */
export type BotLineIcon = "order" | "kitchen" | "table" | "paid";

export interface BotLine {
  /** Stable react key: the same instant can produce two lines and neither may collide. */
  key: number;
  /** **Server** instant of the event (order's `createdAt`, session's `openedAt`, payment's `paidAt`). */
  at: number;
  icon: BotLineIcon;
  text: string;
}

/** Frame `d1` lists six lines; the seventh pushes the oldest out. */
const MAX_LINES = 6;

/**
 * The bot panel's feed (frame `d1-demo`, ticket 26). It reports what the demo bot is doing **from the
 * product's own events** — no polling and no invented activity:
 *
 * - `sessionChanged` on a round whose id has not been seen yet → `B1 เปิดโต๊ะ Silver`.
 * - `orderCreated` → `A1 สั่งรอบที่ 3` (the round's number rides on `OrderDto`).
 * - `orderStatusChanged` → `ครัวเริ่มทำรอบที่ N` on Preparing, `ครัวทำเสร็จรอบที่ N` on Ready.
 * - `sessionClosed` → `ปิดบิล · รับเงินสด` with the receipt's `paidAt` when the round really was paid.
 *
 * The only marker the server offers for "this round is the bot's" is `TableSession.IsDemoBot`, which
 * appears solely in `SessionDetailDto` (`GET /api/sessions/{id}`, OwnerOnly — the demo page signs in as
 * the demo Owner before this panel exists). So every event is resolved against that one request and the
 * answer is memoised per round, because a round's `isDemoBot` can never change. A probe that fails is
 * *not* memoised: the round simply produces no line, and the next event tries again, rather than
 * caching a false forever.
 *
 * The wording is neutral about who ordered (`A1 สั่งรอบที่ 3`, the approved copy of frame `d1`): when a
 * portfolio viewer orders from the left pane of this same page the round is a bot round — the demo hands
 * the viewer a round the bot owns — and the line states exactly what happened. Nothing here claims the
 * bot pressed the button.
 *
 * A line is pushed to the top; the panel is the newest six lines, exactly like the frame's timeline.
 */
export function useBotFeed(): BotLine[] {
  const [lines, setLines] = useState<BotLine[]>([]);
  /** `sessionId → IsDemoBot`, memoised on success only. */
  const botFlags = useRef(new Map<number, boolean>());
  /** Rounds already reported as opened, so an adjustment (also `sessionChanged`) adds no second line. */
  const seenRounds = useRef(new Set<number>());
  const nextKey = useRef(0);

  const push = useCallback((icon: BotLineIcon, at: number, text: string) => {
    setLines((shown) => [{ key: nextKey.current++, at, icon, text }, ...shown].slice(0, MAX_LINES));
  }, []);

  const isBotRound = useCallback(async (sessionId: number): Promise<boolean> => {
    const known = botFlags.current.get(sessionId);
    if (known !== undefined) return known;

    try {
      const detail = await apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}`);
      botFlags.current.set(sessionId, detail.isDemoBot);
      return detail.isDemoBot;
    } catch {
      return false;
    }
  }, []);

  useLiveEvent("sessionChanged", (round) => {
    void (async () => {
      if (seenRounds.current.has(round.id)) return;
      seenRounds.current.add(round.id);
      if (!(await isBotRound(round.id))) return;
      push("table", Date.parse(round.openedAt), `${round.tableName} เปิดโต๊ะ ${round.packageName}`);
    })();
  });

  useLiveEvent("orderCreated", (order) => {
    void (async () => {
      if (!(await isBotRound(order.sessionId))) return;
      push("order", Date.parse(order.createdAt), `${order.tableName} สั่งรอบที่ ${order.number}`);
    })();
  });

  useLiveEvent("orderStatusChanged", (order) => {
    void (async () => {
      if (order.status !== "Preparing" && order.status !== "Ready") return;
      if (!(await isBotRound(order.sessionId))) return;
      const what = order.status === "Preparing" ? "ครัวเริ่มทำ" : "ครัวทำเสร็จ";
      push("kitchen", Date.parse(order.updatedAt), `${order.tableName} ${what}รอบที่ ${order.number}`);
    })();
  });

  useLiveEvent("sessionClosed", ({ sessionId }) => {
    void (async () => {
      // Always fresh: the memoised probe predates the close, and the close is what carries `paidAt`.
      let detail: SessionDetailDto;
      try {
        detail = await apiFetch<SessionDetailDto>(`/api/sessions/${sessionId}`);
      } catch {
        return;
      }
      botFlags.current.set(sessionId, detail.isDemoBot);
      if (!detail.isDemoBot || detail.payment == null) return;
      push("paid", Date.parse(detail.payment.paidAt), `${detail.tableName} ปิดบิล · รับเงินสด`);
    })();
  });

  return lines;
}
