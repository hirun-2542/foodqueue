---
name: FoodQueue
description: Modern Minimalism on a clean white ground, with orange reserved for the one action, the shop banner and the seating clock.
colors:
  ground: "#FAFAF9"
  surface: "#FFFFFF"
  sunken: "#F5F4F2"
  hairline: "#E7E5E4"
  control-border: "#8E8883"
  ink: "#1C1917"
  ink-2: "#44403C"
  muted: "#6B6560"
  orange: "#F97316"
  orange-strong: "#CC4A0A"
  orange-hover: "#B33F08"
  orange-soft: "#FFF3E8"
  orange-ink: "#A83A06"
  orange-line: "#FDBA74"
  pending-ink: "#8A4B00"
  pending-soft: "#FEF3D9"
  preparing-ink: "#1D4ED8"
  preparing-soft: "#EAF1FF"
  ready-ink: "#166534"
  ready-soft: "#E8F7EC"
  served-ink: "#57534E"
  served-soft: "#F0EEEC"
  cancel-ink: "#B91C1C"
  cancel-soft: "#FDECEC"
  cancel-line: "#F3C4C4"
  pending-dot: "#E8A100"
  preparing-dot: "#3B82F6"
  ready-dot: "#22A04B"
  served-dot: "#A8A29E"
  cancel-dot: "#E5484D"
  alert: "#E8A100"
  line-strong: "#D6D3D1"
  on-accent: "#FFFFFF"
  paper-ink: "#111111"
  paper-rule: "#8D8D8D"
typography:
  display:
    fontFamily: "Noto Sans Thai, Sarabun, system-ui, sans-serif"
    fontSize: "36px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  headline:
    fontFamily: "Noto Sans Thai, Sarabun, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 600
    lineHeight: 1.35
    letterSpacing: "-0.01em"
  title:
    fontFamily: "Noto Sans Thai, Sarabun, system-ui, sans-serif"
    fontSize: "18px"
    fontWeight: 600
    lineHeight: 1.45
  body:
    fontFamily: "Noto Sans Thai, Sarabun, system-ui, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Noto Sans Thai, Sarabun, system-ui, sans-serif"
    fontSize: "13px"
    fontWeight: 600
    lineHeight: 1.55
  clock:
    fontFamily: "DM Mono, ui-monospace, monospace"
    fontSize: "28px"
    fontWeight: 500
    lineHeight: 1.15
rounded:
  xs: "6px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  pill: "999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "24px"
  xl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.orange-strong}"
    textColor: "{colors.surface}"
    rounded: "{rounded.md}"
    height: "44px"
    padding: "0 18px"
  button-primary-hover:
    backgroundColor: "{colors.orange-hover}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    height: "44px"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: "16px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "10px"
    height: "44px"
  nav-item-active:
    backgroundColor: "{colors.orange-soft}"
    textColor: "{colors.orange-ink}"
    rounded: "10px"
    height: "44px"
  badge-status:
    rounded: "{rounded.pill}"
    height: "24px"
---

# Design System: FoodQueue

## Overview

**Creative North Star: "The Seating Clock"**

A buffet seating is a clock. Everything in FoodQueue either helps a person act inside that clock or shows how much of it is left. The surface stays quiet so the clock and the one action in front of the person can be read at a glance: white ground, hairline cards, ink text, and a single orange that does three jobs only.

The same product is used on three devices by three people, so density changes but the language does not. The customer phone is relaxed and thumb-sized, the kitchen tablet is big and high-contrast, the owner desktop is denser and scannable. Thai script is the primary language; the type scale gives it room (line-height 1.5 and up) and keeps numerals tabular.

**Key Characteristics:**
- White ground, hairline borders, no shadow on resting surfaces.
- Orange only for the primary action, the selected state, the shop banner and the time rail.
- One signature element, the time rail, shown to staff (owner table cards and session detail, kitchen order age); the customer never sees a countdown.
- Status always reads as dot plus Thai label, never colour alone.
- Real, scannable QR codes and real demo data; nothing is a placeholder shape.

## Colors

A white and warm-stone neutral ground with one orange, plus five desaturated status colours for order state.

### Primary
- **Seating Orange** (#CC4A0A): filled primary buttons and nothing else on a fill; white label = 4.6:1.
- **Ember** (#F97316): non-text only. The time rail fill, dots, icons, selected-state accents. Ink on it = 6.2:1.
- **Ember Ink** (#A83A06): every piece of orange text (5.9:1 on the soft tint, 6.1:1 on the ground).
- **Pressed Orange** (#B33F08): hover and pressed fill.
- **Warm Tint** (#FFF3E8) with **Tint Line** (#FDBA74): selected rows, active nav, accent badges.

### Neutral
- **Ground** (#FAFAF9), **Surface** (#FFFFFF), **Sunken** (#F5F4F2): page, cards and sheets, table headers and wells.
- **Hairline** (#E7E5E4): card borders and dividers; deliberately below 3:1 as decoration.
- **Control Border** (#8E8883): borders of inputs, checkboxes, switch tracks (3.5:1 on white).
- **Ink** (#1C1917), **Ink 2** (#44403C), **Muted** (#6B6560): 17.5 / 10.3 / 5.7:1 on white.

### Status (ink on its own soft fill, all at least 5.6:1)
รอรับ amber (#8A4B00 on #FEF3D9), กำลังทำ blue (#1D4ED8 on #EAF1FF), พร้อมเสิร์ฟ green (#166534 on #E8F7EC), เสิร์ฟแล้ว stone (#57534E on #F0EEEC), ยกเลิก red (#B91C1C on #FDECEC).

### Named Rules
**The Four Jobs Rule.** Orange appears for the primary action, the selected state, the time rail, and the customer's shop banner. If a fifth reason shows up, use ink or a status colour instead.
**The Alert Rule.** Amber `Alert` (#E8A100, ink on it 7.9:1) is a fill, never text on white (2.2:1), and it means exactly one thing: an order card on the queue or kitchen board that nobody has started. It does not appear on the table map and it never blinks on tables; on the cards it pulses (reduced-motion: a static halo).
**The Measured Pair Rule.** A colour is only valid as a measured pair with the surface it sits on. Orange text is `Ember Ink`, never `Ember` or `Seating Orange` on the page ground (4.4:1).

## Typography

**Display, Body and Label Font:** Noto Sans Thai (with Sarabun, system-ui fallback)
**Clock and code Font:** DM Mono (countdowns, table codes, receipt numbers only)

**Character:** Calm and even. One family carries Thai and Latin so a mixed line never changes colour; mono appears only where a figure must never be misread.

### Hierarchy
- **Display** (600, 36px, 1.2): the payment total and the login title.
- **Headline** (600, 24px, 1.35): page titles and the customer's table name.
- **Title** (600, 18px, 1.45): section and dialog titles.
- **Body** (400, 15px, 1.6): all running text; 12px is the minimum anywhere.
- **Label** (600, 13px, 1.55): field labels, KPI labels, table headers.
- **Clock** (DM Mono 500, 28px): the countdown. Never set Thai inside a mono run; split the unit into its own element (`เกิน` in Noto, `16:48` in mono).

### Named Rules
**The Split Run Rule.** Numbers that must not be misread get mono; the Thai word beside them does not. Money is Noto with tabular figures, never mono.

## Layout

Mobile-first. Customer is a single column at 390px: a shop banner (128px: shop name over Seating Orange, or the owner's uploaded image) carrying the table name as a small white chip at top left and the package as a small white chip at top right, sticky category tabs, a floating cart bar and a two-item tab bar (เมนู / ออเดอร์); no bill tab, no countdown, no separate table row; touch targets are 44px or more. The customer orders page is one merged list of everything the table ordered (quantities summed, notes kept, no rounds, no status, no time). Kitchen is a landscape tablet board of three equal columns (รอรับ / กำลังทำ / พร้อมเสิร์ฟ), actions 64px tall. Owner is a 240px sidebar plus a content area with 32px gutters; the overview is a four-column grid of table cards, detail pages are two columns with a sticky bill. Spacing is a 4px grid, with more space above a heading than below it.

## Elevation & Depth

Flat by default. Cards are white with a 1px hairline; depth comes from tone (Ground, Surface, Sunken), not shadow. Shadow exists only on things that float over the page.

### Shadow Vocabulary
- **Float** (`box-shadow: 0 1px 2px rgba(28,25,23,.06), 0 12px 32px -4px rgba(28,25,23,.14)`): dialogs, bottom sheets, drawers, toasts, the floating cart bar.
- **Scrim** (`rgba(28,25,23,.38)`): the dim layer behind any floating surface.

### Named Rules
**The Border-or-Shadow Rule.** A surface has a hairline or a shadow, never both.

## Shapes

Radii step 8 / 12 / 16: controls 12 (inputs 10), cards and dialogs 16, bottom sheets 20 on the top corners. Pills are for small elements only (status badges, filter pills, the connection chip). Menu photos are square crops with a 12px radius at three sizes: 72px on the customer menu, 48px in the cart and owner tables, 36px on the customer orders list; an item without a photo falls back to a Lucide category icon (1.75 stroke) on a quiet tile.

## Components

### Time rail (signature)
A 6px track in Hairline with an Ember fill for the elapsed share of the package duration and a 2px ink tick at the Last order point. After the tick the fill becomes Seating Orange (`late`); once time is up it becomes the cancel red (`over`). Staff screens only: owner table cards and session detail. Table cards show start–end times (`18:55–20:25`) and minutes used (`ใช้ไป 47 นาที`), never a countdown; at the end time ordering closes (badge "ปิดการสั่งแล้ว"). On the kitchen board the same rail measures an order's age against 10 minutes. Never replaced by a ring or a sparkline.

### Shop banner (customer)
128px band at the top of every customer screen: shop name at 28px / 600 and a one-line tag in white on Seating Orange (4.6:1), with a tone-on-tone Lucide icon cropped at the right edge. Two small chips sit on it, 26px tall, white fill with a hairline border, pill-rounded, 12.5px / 600 ink text: table name top left, package top right (with an Ember dot). The owner can replace the orange field with an uploaded image (settings); the shop name and the chips stay on top. A closed session shows no amounts.

### Buttons
- **Primary:** Seating Orange fill, white label, 12px radius, 44px (52px large, 64px kitchen). One per screen.
- **Secondary:** white with a 1px stone border. **Ghost:** ink-2 text, no border. **Danger:** white with a red-tinted border and red ink.

### Status badge
24px pill, 6px dot plus Thai label, soft fill with matching ink. The label is the meaning; the colour only reinforces it. Staff order flow is รอรับ → (เริ่มทำ) → กำลังทำ → (พร้อมเสิร์ฟ) → พร้อมเสิร์ฟ; a ready order has no further button.

### Table state (floor map)
Two colours only, each with a Thai label: ใช้งานอยู่ (Warm Tint with Tint Line) and ว่าง (Sunken grey). Nothing blinks. A tile shows the table code, guests over seats (`2/4`) and one pip per seat; a table with an unstarted order keeps its in-use colour and shows a small bell line "ออเดอร์รอรับ". The floor map draws the room (walls, door, cashier, kitchen, window side) so position is readable; the owner selects a tile to see its facts in a side panel. Edit mode (แก้ไขผัง) adds a seat stepper to every tile, a remove button on free tiles only, a dashed "เพิ่มโต๊ะ" tile, and a side form for name, seats (1–20) and zone.

### New-order modal
A centred dialog with a still bell on a Warm Tint circle, the table and round, a short summary, a collapsible "ดูว่าลูกค้าสั่งอะไร" list (photos, quantities, notes), a sound indicator with mute, and the actions ไว้ก่อน / ไปหน้าออเดอร์ (it navigates to the queue; starting the order happens there). It appears on every staff screen when an order arrives and plays an alert sound.

### Cards and tables
Cards are white, 16px radius, hairline border, 16px padding, never nested. Tables have a Sunken header row, 56px rows and hairline separators. A selected row uses Warm Tint.

### Inputs
White, 10px radius, Control Border, 44px. Focus swaps the border to Seating Orange and adds a 3px Warm Tint ring; errors use the cancel ink with a message that names the fix.

### Navigation
Owner sidebar items are 44px; the active item is Warm Tint with Ember Ink text and a 600 weight; a count chip is a filled Seating Orange pill. Customer uses a two-item tab bar and owner-mobile a five-item one, with the active item in Ember Ink.

## Do's and Don'ts

### Do:
- **Do** reserve orange for the primary action, the selection, the time rail and the customer shop banner.
- **Do** pair every status colour with its dot and Thai label.
- **Do** keep numbers tabular and put Thai text outside mono runs.
- **Do** keep resting surfaces flat; let only floating layers carry the Float shadow.
- **Do** label demo data (`DEMO DATA`) wherever it could be mistaken for real.

### Don't:
- **Don't** add gradients, glass, emoji or unicode symbols in place of icons.
- **Don't** nest cards or tile identical icon-heading-text cards as page structure.
- **Don't** use ring or pie charts; share is a stacked bar with a legend.
- **Don't** put a call-staff or request-bill button on the customer screen, or a cancel button on a customer order.
- **Don't** introduce a colour that is not a token.
