# FoodQueue web

Next.js 16 App Router + TypeScript + Tailwind v4 + shadcn/ui, Thai-only UI, mobile-first.

The app is **only ever reached through Caddy at `http://localhost:8080`**. Next does not proxy
`/api` or `/hubs`: cookie auth and the SignalR WebSocket need one origin, so never open this app
directly on `:3000` in a browser.

## Versions (pinned)

| Package | Version | Note |
|---|---|---|
| `next` | `16.4.2` | pinned exactly, as `qr-buffet-ordering/plan.md` requires |
| `react` / `react-dom` | `19.3.0` | from `create-next-app@16.4.2` |
| `tailwindcss` | `4.3.3` | devDependency; wired through `@tailwindcss/turbopack` |
| `@tanstack/react-query` | `5.104.3` | |
| `@microsoft/signalr` | `10.0.11` | |
| `recharts` | `3.10.1` | |
| `qrcode.react` | `4.2.0` | |
| `promptpay-qr` | `0.5.0` | installed; if it fails in Next 16/ESM use `src/lib/promptpay.ts` (plan.md contingency) |
| `zod` | `4.6.7` | |
| `react-hook-form` | `7.90.0` | |
| `@hookform/resolvers` | `5.9.1` | |
| `lucide-react` | `1.58.0` | |
| `@playwright/test` | `1.64.1` | devDependency |

Scaffolded with
`pnpm dlx create-next-app@16.4.2 frontend --ts --tailwind --eslint --app --src-dir --import-alias "@/*" --use-pnpm`,
then `pnpm dlx shadcn@4.22.0 init -t next -b radix -p nova`.

## shadcn/ui

**The CLI worked on Next 16 + Tailwind 4 — no fallback was needed.** `shadcn@4.22.0` ran
`init` (radix base, `nova` preset) and recognised the project as Next.js with Tailwind v4, wrote
`components.json`, `src/lib/utils.ts` and `src/components/ui/button.tsx`, and installed
`radix-ui`, `class-variance-authority`, `cn`, `sonner` and `tw-animate-css`.
`pnpm dlx shadcn@4.22.0 add card dialog sheet input label select tabs badge table sonner switch textarea dropdown-menu skeleton -y`
created the remaining 14 components. Nothing was hand-configured and Next was not downgraded.

Notes on the generated files, all inside `frontend/`:

- `src/lib/utils.ts` re-exports `cn` from the `cn` package (shadcn's `clsx`+`tailwind-merge`
  replacement); the components import `cn` from `"cn"` directly.
- `next-themes` (pulled in by the generated `sonner` wrapper) was **removed**: FoodQueue has no dark
  theme, so the toaster is pinned to `theme="light"` and the OS colour scheme is ignored.
- Tailwind is wired the way `create-next-app@16.4.2` scaffolds Next 16: the `@tailwindcss/turbopack`
  loader rule in `next.config.ts`, with `tailwindcss` as a devDependency. No `postcss.config.*` is
  needed.
- Login uses `button`, `input` and `label`. `card` is styled but unused until the demo-login card
  (ticket 24).

## Design tokens

`src/app/globals.css` is the only place colours, type, radii and shadows are defined, mapped from
`DESIGN.md` frontmatter into CSS variables and Tailwind v4 `@theme` tokens. The default Tailwind
palette is **removed** (`--color-*: initial`), so any colour class that is not a FoodQueue token
fails to compile. shadcn's semantic names (`--color-primary`, `--color-border`, `--color-input`,
`--color-ring`, …) are aliases of those tokens. Orange is only used for the primary action,
selection, the time rail and the customer shop banner; status colours keep WCAG AA contrast
(pending amber / preparing blue / ready green / cancelled red, each as soft fill + matching ink +
dot).

Fonts are `IBM Plex Sans Thai` (400/500/600) and `DM Mono` (500) via `next/font/google`
(build-time download, inlined — no runtime font CDN, no third-party runtime calls).

## Layout

```
src/app/layout.tsx        root layout: <html lang="th">, fonts, query client + sonner Toaster
src/app/page.tsx          / → redirect to /login
src/app/login/page.tsx    metadata wrapper
src/app/login/login-form.tsx  the credential form from design/out/l1,l2 login mockups
src/app/providers.tsx     TanStack QueryClientProvider + Toaster
src/lib/api.ts            apiFetch<T> (credentials: "include") + ApiError { code, status, message };
                          apiFetchWithServerTime<T> also returns the server clock from the `Date` header
src/lib/types.ts          API DTOs, error codes, live-event payloads
src/lib/format.ts         formatBaht / formatTime / formatDate (Asia/Bangkok) / minutesUsed / clockRange
src/lib/realtime.ts       useLiveConnection / LiveProvider / useLiveEvent / useLiveOffline
src/lib/kitchen.ts        kitchen board query (GET /api/orders) merged live from the hub events
src/app/(staff)/kitchen/  the k1 board: columns, order card (ticket 12 adds `actions`), chime
e2e/smoke.spec.ts         login form is visible at /login (Playwright)
```

`format.ts` and `realtime.ts` are pure/state-from-props: no `Date.now()` during render. Time reaches
the UI as a prop or state seeded from server time plus an offset.

`ApiError.code` is `null` when the response carried no FoodQueue error code (401/403, proxy or
network failure); `status` is `0` when the request never got an answer. `message` is always Thai and
safe to display.

After a successful login the form calls `GET /api/auth/me` and sends `Owner` to `/owner` and
`Kitchen` to `/kitchen` — those routes (and the guards that protect them) arrive in ticket 02, so
until then they 404. The brand block shows the product name rather than the shop name because
`/api/settings` is Owner-only and cannot be read before login.

## Commands

```bash
pnpm install
pnpm dev            # local dev only; reach the app through Caddy on :8080
pnpm build          # production build (output: "standalone")
pnpm exec tsc --noEmit
pnpm lint
pnpm exec playwright test   # needs the compose stack running: FQ_BASE_URL defaults to http://localhost:8080
```

## Docker

`Dockerfile` is a multi-stage pnpm build (`node:24-alpine`, `pnpm@12.10.1` installed globally,
`pnpm install --frozen-lockfile`) running `next build` with `output: "standalone"`, then copying
`.next/standalone` + `.next/static` into a runtime stage that listens on **3000** as the `node`
user. `docker compose` builds it with `build: { context: ./frontend }`. `.dockerignore` keeps
`node_modules`, `.next` and the Playwright files out of the build context.
