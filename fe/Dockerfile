# =============================================================================
# UAV Drone Detection System — Frontend Dockerfile
# Multi-stage build: deps → builder → runner
#
# Based on: Next.js 16 App Router, standalone output mode
# Target service: fe (dashboard)
# Kong gateway: http://kong:8000 (internal Docker bridge)
# =============================================================================

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM node:20-alpine AS builder

WORKDIR /app

# Install ALL deps (including devDependencies needed for build)
COPY package.json package-lock.json* ./
RUN npm ci --ignore-scripts --cache .npm

# Copy application source
COPY . .

# Environment variables baked in at build time.
# These are the DOCKER-INTERNAL values — not the host-facing .env.local values.
# BACKEND_URL points to Kong's internal service name on the Docker bridge network.
# NEXT_PUBLIC_WS_URL points to Kong's host-exposed port (accessible from the browser).
ENV NEXT_TELEMETRY_DISABLED=1
ENV NODE_ENV=production

# Build the application with standalone output (minimizes runtime image size)
RUN npm run build

# ── Stage 3: Production runner ────────────────────────────────────────────────
FROM node:20-alpine AS runner

WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

# Create non-root user for security
RUN addgroup --system --gid 1001 nodejs \
    && adduser --system --uid 1001 nextjs

# Copy only the standalone build output and static assets
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public

USER nextjs

EXPOSE 3000

ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# Next.js standalone server
CMD ["node", "server.js"]
