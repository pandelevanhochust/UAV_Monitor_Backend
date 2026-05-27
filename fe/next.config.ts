import type { NextConfig } from 'next';

/**
 * Next.js configuration for the UAV Drone Detection Dashboard.
 *
 * `output: 'standalone'` enables multi-stage Docker builds by packaging only
 * the files needed to run the server (no node_modules tree shipped).
 * The Dockerfile's runner stage copies .next/standalone + .next/static + public.
 */
const nextConfig: NextConfig = {
  output: 'standalone',
};

export default nextConfig;
