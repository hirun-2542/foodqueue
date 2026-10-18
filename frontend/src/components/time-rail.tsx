import { type TimeTone } from "@/lib/format";

/**
 * The signature time rail (DESIGN): a 6px Hairline track, an Ember fill for the elapsed share, and a
 * 2px ink tick at the Last-order point. The fill turns Seating Orange after the tick and cancel red
 * once the round is over. It is a length, not a clock — no screen may grow a countdown from it.
 */
const fillTone: Record<TimeTone, string> = {
  elapsed: "bg-orange",
  late: "bg-orange-strong",
  over: "bg-cancel-dot",
};

export function TimeRail({ elapsed, mark, tone }: { elapsed: number; mark: number; tone: TimeTone }) {
  return (
    <span className="relative block py-[3px]" aria-hidden>
      <span className="block h-1.5 overflow-hidden rounded-full bg-hairline">
        <span
          className={`block h-full rounded-full ${fillTone[tone]}`}
          style={{ width: `${elapsed}%` }}
        />
      </span>
      <span
        className="absolute inset-y-0 w-0.5 rounded-full bg-ink-2"
        style={{ left: `${mark}%` }}
      />
    </span>
  );
}
