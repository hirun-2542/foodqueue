import { createCn } from "cn/config";

/**
 * `clsx` + Tailwind class merging, taught FoodQueue's own type scale (`--text-*` in `globals.css`, from DESIGN
 * typography). Without it the merger reads `text-label`/`text-headline`/… as colours, so a component's default
 * `text-sm text-muted-foreground` plus a caller's `text-label` kept the wrong size and dropped the colour.
 * Every component (including `components/ui`) imports `cn` from here.
 */
export const cn = createCn({
  extend: { theme: { text: ["display", "headline", "title", "body", "label", "clock"] } },
});
