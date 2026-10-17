# FoodQueue web (Next.js 16, standalone). Built by compose as `build: { context: ./frontend }`.
# The container only ever answers Caddy on the compose network; nothing else reaches :3000.

FROM node:24-alpine AS pnpm

ENV PNPM_HOME="/pnpm" \
    PATH="/pnpm:$PATH" \
    NEXT_TELEMETRY_DISABLED=1
RUN npm install --global pnpm@12.10.1

FROM pnpm AS deps

WORKDIR /app
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

FROM pnpm AS build

WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# next/font/google fetches the fonts at build time and inlines them; the image makes no runtime font calls.
RUN pnpm build

FROM pnpm AS runner

WORKDIR /app
ENV NODE_ENV=production \
    PORT=3000 \
    HOSTNAME=0.0.0.0

COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static

USER node
EXPOSE 3000

CMD ["node", "server.js"]
