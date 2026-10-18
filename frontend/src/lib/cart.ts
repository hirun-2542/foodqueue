"use client";

import { useMutation } from "@tanstack/react-query";
import {
  createContext,
  createElement,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from "react";
import { ApiError, apiFetch } from "@/lib/api";
import { toMs } from "@/lib/format";
import type { MenuItemKind } from "@/lib/types";

/**
 * ตะกร้าของลูกค้าหนึ่งเครื่อง (ticket 10) — สถานะทั้งหมดอยู่ที่นี่ที่เดียว
 *
 * กฎที่โมดูลนี้รับผิดชอบ:
 * - ตะกร้าเก็บใน `localStorage` ต่อ token (`fq-cart-{token}`) จึงอยู่ต่อหลังรีเฟรช และแต่ละมือถือ
 *   ที่นั่งโต๊ะเดียวกันมีตะกร้าของตัวเอง ไม่ทับกัน (spec: "ตะกร้าเก็บในเครื่องลูกค้า (ต่อ token)")
 * - ปุ่ม + หยุดที่ลิมิตต่อเมนู และหยุดเมื่อรวมครบลิมิตรอบนี้ (story 13/14)
 * - หมายเหตุต่อรายการ ≤ 100 ตัวอักษร ตามคอลัมน์ของฐานข้อมูล
 * - "หมดเวลาสั่งอาหาร"/"Last order" ตัดสินจากเวลาของเซิร์ฟเวอร์ (`serverNow` + offset) เท่านั้น
 *   ไม่มีตัวนับถอยหลังบนจอ และไม่มีการอ่านนาฬิกาเครื่องระหว่าง render
 */

/** ขอบเขตหมายเหตุต่อรายการ — ตรงกับ HasMaxLength(100) ของ OrderItem.Note */
export const CART_NOTE_MAX_LENGTH = 100;

/** ข้อความยืนยันหลังส่งสำเร็จ — ไม่มีเลขรอบโดยเจตนา (spec: ลูกค้าไม่เห็นเลขรอบ) */
export const CART_SENT_MESSAGE = "ส่งออเดอร์แล้ว";

/**
 * เมนูเท่าที่ตะกร้าต้องรู้ — รูปทรงนี้เป็น subset ของ `CustomerMenuItemDto` ของ ticket 09
 * จึงประกาศที่นี่แทนการรอ types.ts เพื่อไม่ให้มี DTO ชื่อเดียวกันสองที่
 */
export interface CartMenuItem {
  id: number;
  name: string;
  kind: MenuItemKind;
  price: number;
  /** The API omits null fields, so a menu row without a photo arrives as `undefined` rather than null. */
  imagePath?: string | null;
}

/** หนึ่งบรรทัดในตะกร้า: เก็บ snapshot ไว้ ตะกร้าจึงยังแสดงได้ก่อนเมนูโหลดเสร็จ */
export interface CartLine extends CartMenuItem {
  quantity: number;
  note: string;
}

/** คำตอบของ `POST /api/s/{token}/orders` — คำยืนยัน ไม่มีเลขรอบ/สถานะ */
export interface SubmitOrderResult {
  submittedAt: string;
  itemCount: number;
}

export interface OrderLimits {
  maxQtyPerItem: number;
  maxItems: number;
}

/** หน้าต่างเวลาสั่งอาหารของรอบนี้ ตัดสินจากเวลาของเซิร์ฟเวอร์ที่ปรับ offset แล้ว */
export interface OrderWindow {
  /** เลย `endsAt` แล้ว → สั่งไม่ได้ (เซิร์ฟเวอร์ตอบ `SESSION_EXPIRED`) */
  isClosed: boolean;
  /** เหลือเวลาไม่เกินค่าที่ร้านตั้ง → ประกาศ Last order (ข้อความเท่านั้น ไม่ปิดการสั่ง) */
  isLastOrder: boolean;
}

export function cartStorageKey(token: string): string {
  return `fq-cart-${token}`;
}

function readCart(token: string, limits: OrderLimits): CartLine[] {
  try {
    return parseCart(window.localStorage.getItem(cartStorageKey(token)), limits);
  } catch {
    return [];
  }
}

/** แปลงค่าที่เก็บไว้เป็นบรรทัดตะกร้า — ค่าที่เสียหาย/รูปทรงผิดถูกทิ้ง ไม่ให้ทั้งหน้าพัง */
function parseCart(raw: string | null, limits: OrderLimits): CartLine[] {
  if (!raw) return [];

  try {
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter((line): line is CartLine => typeof line === "object" && line !== null)
      .filter((line) => Number.isFinite(line.id) && Number.isFinite(line.quantity) && line.quantity > 0)
      .map((line) => ({
        ...line,
        note: line.note?.slice(0, CART_NOTE_MAX_LENGTH) ?? "",
        quantity: Math.max(1, Math.min(line.quantity, limits.maxQtyPerItem)),
      }));
  } catch {
    return [];
  }
}

/**
 * ตะกร้าอ่านจาก localStorage ผ่าน `useSyncExternalStore` — ได้ทั้งตอน SSR (`null` = ยังไม่รู้ ซึ่งตรงกับ
 * HTML แรกเสมอ จึงไม่มี hydration mismatch) และตอน hydrate โดยไม่ต้อง `setState` ใน effect
 * และรับการเปลี่ยนแปลงจากแท็บอื่นของเบราว์เซอร์เดียวกันด้วย
 */
export interface CartStore {
  subscribe: (listener: () => void) => () => void;
  getSnapshot: () => CartLine[] | null;
  getServerSnapshot: () => CartLine[] | null;
  /** บอก React ว่าค่าที่เก็บไว้เปลี่ยนแล้ว (หลังเขียน localStorage ด้วยมือ) */
  notify: () => void;
}

function createCartStore(token: string, limits: OrderLimits): CartStore {
  let cached: CartLine[] | null = null;
  let cachedRaw: string | null = null;
  const listeners = new Set<() => void>();

  const notify = () => {
    for (const listener of listeners) listener();
  };

  return {
    subscribe(listener: () => void): () => void {
      listeners.add(listener);
      window.addEventListener("storage", notify);
      return () => {
        listeners.delete(listener);
        window.removeEventListener("storage", notify);
      };
    },
    getSnapshot: (): CartLine[] | null => {
      let raw: string | null = null;
      try {
        raw = window.localStorage.getItem(cartStorageKey(token));
      } catch {
        return null;
      }

      // อ่านซ้ำเมื่อ "ค่าที่เก็บไว้" เปลี่ยนเท่านั้น: snapshot ต้องมี identity คงที่เมื่อข้อมูลเท่าเดิม
      // ไม่งั้น useSyncExternalStore จะเรนเดอร์วน
      if (raw !== cachedRaw) {
        cachedRaw = raw;
        cached = parseCart(raw, limits);
      }

      return cached;
    },
    /** ระหว่าง SSR ยังอ่าน localStorage ไม่ได้ — คืน null เพื่อให้ HTML แรกเหมือนกันทั้งสองฝั่ง */
    getServerSnapshot: (): CartLine[] | null => null,
    notify,
  };
}

/** เก็บหรือลบตะกร้าของ token นี้ — เรียกหลังการเปลี่ยนแปลงทุกครั้งที่ผู้ใช้ทำ */
function persistCart(token: string, lines: CartLine[]): void {
  try {
    if (lines.length === 0) {
      window.localStorage.removeItem(cartStorageKey(token));
      return;
    }

    window.localStorage.setItem(cartStorageKey(token), JSON.stringify(lines));
  } catch {
    // เขียนไม่ได้ (โหมดส่วนตัว/โควตาเต็ม) — ตะกร้ายังใช้ได้ในหน้านี้ แค่ไม่รอดรีเฟรช
  }
}

/**
 * เวลาปัจจุบันของเซิร์ฟเวอร์จากค่า `serverNow` ที่ API คืนมา — offset วัดใน effect (ไม่ใช่ระหว่าง render)
 * แล้วค่าใหม่ขยับทุก 30 วินาที เพื่อให้สถานะ "หมดเวลาสั่งอาหาร" พลิกเองได้ขณะหน้าเปิดค้างอยู่
 * นี่ไม่ใช่ตัวนับถอยหลัง: ไม่มีตัวเลขบนจอและไม่มีอะไรนับลง
 */
export function useServerTime(serverNow: string | number): number {
  const [now, setNow] = useState(() => toMs(serverNow));

  useEffect(() => {
    const offset = Date.now() - toMs(serverNow);
    const tick = () => setNow(Date.now() + offset);
    tick();
    const timer = window.setInterval(tick, 30_000);
    return () => window.clearInterval(timer);
  }, [serverNow]);

  return now;
}

/** สถานะหน้าต่างเวลาสั่งอาหารจากเวลาของเซิร์ฟเวอร์ — ฟังก์ชันบริสุทธิ์ ใช้ทั้งในตะกร้าและแบนเนอร์ */
export function orderWindowAt(now: number, endsAt: string | number, warningMinutes: number): OrderWindow {
  const end = toMs(endsAt);
  return {
    isClosed: now > end,
    isLastOrder: now <= end && now >= end - warningMinutes * 60_000,
  };
}

export interface Cart {
  /** token ของรอบนี้ — ชีตตะกร้าจึงไม่ต้องรับ token เป็น prop ซ้ำกับ provider */
  token: string;
  /** null = ยังอ่าน localStorage ไม่เสร็จ (ต่างจาก [] ที่แปลว่า "ตะกร้าว่าง") */
  lines: CartLine[] | null;
  itemCount: number;
  /** รวมครบลิมิตรอบนี้แล้ว — ปุ่ม + ทุกปุ่มหยุด และตะกร้าแสดงสถานะครบ */
  isFull: boolean;
  limits: OrderLimits;
  orderWindow: OrderWindow;
  quantityOf: (menuItemId: number) => number;
  /** เพิ่มหนึ่งที่ (ไม่เกินลิมิตต่อเมนู/ต่อรอบ) — คืน false เมื่อลิมิตหยุดไว้ */
  add: (item: CartMenuItem, quantity?: number) => boolean;
  setQuantity: (menuItemId: number, quantity: number) => void;
  setNote: (menuItemId: number, note: string) => void;
  clear: () => void;
}

const CartContext = createContext<Cart | null>(null);

export interface CartProviderProps extends OrderLimits {
  token: string;
  serverNow: string | number;
  endsAt: string | number;
  /** ค่าเตือน Last order ของร้าน (`lastOrderWarningMinutes`) */
  warningMinutes: number;
  children: ReactNode;
}

/**
 * ตะกร้า + หน้าต่างเวลาสั่งอาหารของหน้าลูกค้า — mount ครั้งเดียวรอบทั้งหน้าจอ
 * ลูกค้าที่นั่งโต๊ะเดียวกันมีคนละ provider เพราะคนละเครื่อง/คนละ token ไม่แชร์กัน
 */
export function CartProvider({
  token,
  maxQtyPerItem,
  maxItems,
  serverNow,
  endsAt,
  warningMinutes,
  children,
}: CartProviderProps) {
  const limits = useMemo<OrderLimits>(() => ({ maxQtyPerItem, maxItems }), [maxQtyPerItem, maxItems]);
  const store = useMemo(() => createCartStore(token, limits), [token, limits]);
  const lines = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getServerSnapshot);
  const now = useServerTime(serverNow);

  // ทุกการแก้ไขตะกร้าของผู้ใช้เขียนลง localStorage ทันที (ตะกร้าจึงรอดรีเฟรชและไม่ถูกแชร์ข้ามเครื่อง)
  const mutate = useCallback(
    (update: (current: CartLine[]) => CartLine[]) => {
      persistCart(token, update(readCart(token, limits)));
      store.notify();
    },
    [token, limits, store],
  );

  const quantityOf = useCallback(
    (menuItemId: number) => lines?.find((line) => line.id === menuItemId)?.quantity ?? 0,
    [lines],
  );

  const itemCount = lines?.reduce((sum, line) => sum + line.quantity, 0) ?? 0;
  const isFull = itemCount >= limits.maxItems;

  const add = useCallback(
    (item: CartMenuItem, quantity = 1) => {
      if (isFull) return false;

      let added = false;
      mutate((list) => {
        const existing = list.find((line) => line.id === item.id);
        const room = Math.min(quantity, limits.maxItems - list.reduce((sum, l) => sum + l.quantity, 0));
        if (room <= 0) return list;

        added = true;
        if (!existing) {
          return [...list, { ...item, quantity: Math.min(room, limits.maxQtyPerItem), note: "" }];
        }

        const next = Math.min(existing.quantity + room, limits.maxQtyPerItem);
        return list.map((line) => (line.id === item.id ? { ...line, quantity: next } : line));
      });

      return added;
    },
    [isFull, limits.maxItems, limits.maxQtyPerItem, mutate],
  );

  const setQuantity = useCallback(
    (menuItemId: number, quantity: number) => {
      mutate((list) => {
        if (quantity < 1) return list.filter((line) => line.id !== menuItemId);

        const used = list.reduce((sum, line) => sum + (line.id === menuItemId ? 0 : line.quantity), 0);
        const max = Math.min(limits.maxQtyPerItem, limits.maxItems - used);
        return list.map((line) =>
          line.id === menuItemId ? { ...line, quantity: Math.max(1, Math.min(quantity, max)) } : line,
        );
      });
    },
    [limits.maxItems, limits.maxQtyPerItem, mutate],
  );

  const setNote = useCallback(
    (menuItemId: number, note: string) => {
      const trimmed = note.slice(0, CART_NOTE_MAX_LENGTH);
      mutate((list) => list.map((line) => (line.id === menuItemId ? { ...line, note: trimmed } : line)));
    },
    [mutate],
  );

  const clear = useCallback(() => mutate(() => []), [mutate]);

  const orderWindow = useMemo(() => orderWindowAt(now, endsAt, warningMinutes), [now, endsAt, warningMinutes]);

  const value = useMemo<Cart>(
    () => ({
      token,
      lines,
      itemCount,
      isFull,
      limits,
      orderWindow,
      quantityOf,
      add,
      setQuantity,
      setNote,
      clear,
    }),
    [token, lines, itemCount, isFull, limits, orderWindow, quantityOf, add, setQuantity, setNote, clear],
  );

  return createElement(CartContext, { value }, children);
}

/** ตะกร้าของหน้าจอนี้ — ใช้ได้ทั้งแถบตะกร้า/ชีต และแถวเมนูที่ต้องมีปุ่ม +/− */
export function useCart(): Cart {
  const cart = useContext(CartContext);
  if (!cart) throw new Error("useCart must be used inside <CartProvider>");
  return cart;
}

/**
 * ส่งออเดอร์รอบนี้: เซิร์ฟเวอร์เป็นผู้ตัดสินลำดับการตรวจทั้งหมด (ลิมิต/หมดเวลา/เมนูหมด/ไม่อยู่ในแพ็กเกจ)
 * คำตอบเป็นคำยืนยันที่ไม่มีเลขรอบ และเมื่อสำเร็จตะกร้าถูกล้างเพื่อไม่ให้ส่งซ้ำโดยไม่ตั้งใจ (story 18/19)
 */
export function useSubmitOrder(token: string) {
  const { clear } = useCart();

  return useMutation({
    mutationFn: async (lines: CartLine[]): Promise<SubmitOrderResult> => {
      const result = await apiFetch<SubmitOrderResult>(`/api/s/${token}/orders`, {
        method: "POST",
        body: JSON.stringify({
          items: lines.map((line) => ({
            menuItemId: line.id,
            quantity: line.quantity,
            note: line.note.trim() === "" ? undefined : line.note.trim(),
          })),
        }),
      });
      clear();
      return result;
    },
  });
}

/** ข้อความไทยจาก ApiError (เซิร์ฟเวอร์ส่งข้อความที่แสดงต่อลูกค้าได้ตรง ๆ มาแล้ว) */
export function cartErrorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback;
}
