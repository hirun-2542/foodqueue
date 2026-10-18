"use client";

import { type ReactNode, useEffect, useRef, useState } from "react";

/** The frame `d1` declares each pane's device size; the box around it is that size times a scale. */
export interface DeviceSize {
  width: number;
  height: number;
}

/**
 * One pane of the split screen: a caption row plus the scaled device box around an `<iframe>` of the
 * route (frame `d1`'s `.d1-pane` / `.d1-cap` / `.d1-box`).
 *
 * The iframe is a real document of the route at its designed viewport (390x844 phone, 1280x800 kitchen
 * board, 1440x900 owner desktop) and it is scaled with the same `transform: scale()` trick the frame
 * uses. That is the reason this page embeds documents instead of rendering the screens side by side in
 * one tree: those shells size themselves against the viewport (`h-dvh`), so a second copy in the same
 * document would have to be rewritten to accept an artificial height — and it would share one hub
 * connection with whatever else is on the page, which is exactly what a pane must not do (ticket 15's
 * `LiveProvider` reuses a connection only inside the same document).
 */
export function Pane({
  caption,
  icon,
  size,
  scale,
  boxClassName,
  children,
}: {
  caption: ReactNode;
  icon: ReactNode;
  size: DeviceSize;
  scale: number;
  boxClassName?: string;
  children: ReactNode;
}) {
  const width = Math.round(size.width * scale);
  const height = Math.round(size.height * scale);

  return (
    <section className="min-h-0 flex-none rounded-2xl border border-hairline bg-surface p-3">
      <div className="flex h-[34px] items-center gap-2 px-0.5 text-ink-2">
        {icon}
        <span className="text-[13px] leading-[1.55] font-semibold text-ink">{caption}</span>
      </div>
      <div className={`overflow-hidden rounded-sm bg-background ${boxClassName ?? ""}`} style={{ width, height }}>
        <div style={{ width: size.width, height: size.height, transform: `scale(${scale})`, transformOrigin: "0 0" }}>
          {children}
        </div>
      </div>
    </section>
  );
}

/**
 * A pane that fits whatever width it is given: the design width is scaled down until it fits, never up.
 * Only the tab layout needs it (the desktop board is the frame's fixed 1440px and uses its fixed scales);
 * the box's parent is measured with a `ResizeObserver`, so a rotating tablet re-fits without a reload.
 */
export function useFitScale(designWidth: number): [React.RefObject<HTMLDivElement | null>, number] {
  const ref = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0);

  useEffect(() => {
    const box = ref.current;
    if (!box) return;

    const measure = () => {
      if (box.clientWidth > 0) setScale(Math.min(1, box.clientWidth / designWidth));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(box);
    return () => observer.disconnect();
  }, [designWidth]);

  return [ref, scale];
}

/**
 * The desktop board, drawn at the frame's own 1440x1060 and scaled down uniformly when the window is
 * narrower (1280–1439): the same picture, smaller, exactly like the frame scales the devices inside it.
 * The wrapping box takes the scaled height so the page does not leave a band of empty space where the
 * unscaled board used to be.
 */
export function ScaledBoard({
  width,
  height,
  className,
  children,
}: {
  width: number;
  height: number;
  className?: string;
  children: ReactNode;
}) {
  const [ref, scale] = useFitScale(width);

  return (
    <div ref={ref} className={`overflow-hidden ${className ?? ""}`}>
      {/* The board is the frame's own rectangle, drawn at its exact size and then scaled as a whole, so
          every pane keeps the frame's proportions at any window size. The plate below reserves the scaled
          height, otherwise the page would leave a band of empty space where the unscaled board used to be. */}
      <div style={scale > 0 ? { height: height * scale } : undefined}>
        <div
          className="flex items-stretch gap-5 px-6 py-[34px]"
          style={{ width, height, transform: `scale(${scale})`, transformOrigin: "0 0" }}
        >
          {children}
        </div>
      </div>
    </div>
  );
}

/**
 * The scaled iframe of one route. It fills the device box it is placed in (the box's own size is the
 * design viewport scaled down, so the document inside believes it is 390x844 or 1280x800).
 *
 * `pane` is the stable handle for this pane (`customer`/`kitchen`/`owner`) — the end-to-end test finds a
 * pane by it and the caption text stays free to change. `title` is the frame's own wording, and is also
 * how the pane names itself to assistive technology: an iframe without a title reads as "frame" alone.
 */
export function ViewFrame({ src, title, pane }: { src: string; title: string; pane: "customer" | "kitchen" | "owner" }) {
  return <iframe src={src} title={title} data-pane={pane} scrolling="no" className="block size-full border-0" />;
}
