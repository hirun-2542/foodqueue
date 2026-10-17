// Display formatting. Every function here is pure: "now" is always passed in
// (server time plus the client's clock offset), never read with Date.now() during render.

const TIME_ZONE = "Asia/Bangkok";

const baht = new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" });

const time = new Intl.DateTimeFormat("th-TH", {
  timeZone: TIME_ZONE,
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

const date = new Intl.DateTimeFormat("th-TH", {
  timeZone: TIME_ZONE,
  day: "numeric",
  month: "short",
  year: "numeric",
});

type Instant = string | number | Date;

/**
 * Milliseconds since the epoch for any instant the API hands over (ISO-8601 string, epoch number or `Date`).
 * Exported so the cart compares `endsAt` against server time without a second parser.
 */
export function toMs(value: Instant): number {
  return value instanceof Date ? value.getTime() : typeof value === "number" ? value : Date.parse(value);
}

/** `1074.6` → `฿1,074.60` */
export function formatBaht(amount: number): string {
  return baht.format(amount);
}

/** Wall-clock time in Bangkok, 24-hour: `18:55`. */
export function formatTime(value: Instant): string {
  return time.format(toMs(value));
}

/** Calendar date in Bangkok, Thai Buddhist-era year: `18 ต.ค. 2569`. */
export function formatDate(value: Instant): string {
  return date.format(toMs(value));
}

/** Whole minutes elapsed since `openedAt`, never negative (clock skew). */
export function minutesUsed(openedAt: Instant, now: Instant): number {
  return Math.max(0, Math.floor((toMs(now) - toMs(openedAt)) / 60_000));
}

/** Start–end wall-clock range for staff screens: `18:55–20:25`. */
export function clockRange(start: Instant, end: Instant): string {
  return `${formatTime(start)}–${formatTime(end)}`;
}

const bahtWhole = new Intl.NumberFormat("th-TH", {
  style: "currency",
  currency: "THB",
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

/**
 * `299` → `฿299` — the design sets package tier prices in whole baht, where `formatBaht`'s
 * trailing `.00` is noise. Satang still show when a price has them.
 */
export function formatBahtWhole(amount: number): string {
  return bahtWhole.format(amount);
}
