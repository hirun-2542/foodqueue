# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Next.js (App Router) + TypeScript + Tailwind + shadcn/ui on the front end; ASP.NET Core 10, EF Core, PostgreSQL, SignalR on the back end. Confirmed by the user in the original brief; runs locally with docker compose only.

## Users

- **Customer** at a shabu / buffet table: scans a QR slip on their own phone, no account, orders in rounds while eating, wants to see the shop, what is available and what has been ordered. The customer sees no countdown and no bill; the bill is settled with staff.
- **Kitchen staff**: a tablet in the kitchen, wet or busy hands, needs new orders to appear and status changes to be one big tap.
- **Owner**: runs the shop from a desktop or phone; opens tables, takes payment, prints receipts, reads sales.
- **Portfolio viewer** (secondary, confirmed): arrives at Demo Mode with no password and must understand the whole system in a few clicks.

## Product Purpose

QR ordering for a single small–medium shabu / buffet restaurant. Replaces paper orders and hand-timed 90-minute seatings with real-time ordering, kitchen status, billing and sales stats. Also a full-stack portfolio piece, so the demo has to carry the story by itself.

## Positioning

Built around the buffet model, not the à la carte restaurant model: one session QR per seating, package tiers that unlock menu ranks, a per-round order limit, and a visible seating clock.

## Operating Context

- One Table Session per seating, one bill per session, many order rounds, paid once at the end.
- Thai only, baht, Asia/Bangkok time, optional service charge and VAT (default 0%).
- Printing is the browser print dialog onto 80mm thermal paper: pre-bill, receipt, QR slip.
- Phones are the customer device; a landscape tablet is the kitchen device; desktop is the owner device.

## Capabilities and Constraints

Terms follow `qr-buffet-ordering/spec.md`: Table, Table Session, Package, Buffet Item, À la carte Item, Order (shown to users as "รอบที่ N"), Bill Adjustment, Payment, Pre-bill, Receipt, Last order, Demo Bot. Roles are Owner and Kitchen only. Customers cannot cancel; no call-staff or request-bill buttons; no option groups, only a free-text note per line.

## Brand Commitments

- Visual direction pinned by the user: **Modern Minimalism, Clean White + Orange**.
- UI language: Thai only.
- Mobile-first; must work on a phone.

## Evidence on Hand

No real restaurant data, logos or customers exist. Everything shown is synthetic demo data (restaurant "ชาบูเดโม่", tables A1–B6, Silver/Gold/Platinum packages) and must be labelled as demo wherever a viewer could mistake it for real. Menu photos are stand-ins from Wikimedia Commons (`design/src/assets/menu/`, licences in `CREDITS.md`) and must be replaced with the shop's own photography.

## Product Principles

1. The seating window is legible to staff: owner and kitchen see the same start, end and minutes used. The customer is only told when Last order starts and when time is up.
2. Ordering is hard to get wrong: limits, availability and package rank are visible before the send button, not only after it fails.
3. The kitchen sees an order the moment it exists and changes it in one tap.
4. Money is explainable: every baht on a bill traces to a labelled line.
5. The demo explains itself without a guide.

## Accessibility & Inclusion

Thai script must render correctly everywhere. Text contrast 4.5:1, touch targets ≥44px on customer and kitchen screens, status never conveyed by colour alone.
