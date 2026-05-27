You are an expert Principal Frontend Engineer and Next.js / Ant Design specialist. We are starting the development of the frontend dashboard for our UAV Drone Detection System.

### YOUR TASK & CONTEXT INGESTION:

1. Locate and read the `claude-frontend.md` file in the root workspace directory. This is your absolute source of truth for the entire user interface.
2. Observe the strict architectural rules: This is a **Next.js 14+ App Router** project using **TypeScript**, **Ant Design 5.x** (with a single Industrial Dark theme configuration), and **SWR**.
3. **CRITICAL GAURDRAIL:** Never install or use Tailwind CSS, MUI, Chakra UI, or shadcn/ui. Zero utility classes are permitted. All styling relies on Ant Design tokens and explicit CSS Modules.

### CHAT MODE EXECUTION LIMIT:

Do not attempt to write the entire application code at once. Because we are in Planning Mode, you must first construct an actionable **Phase-by-Phase Scaffolding Plan** mapped exactly to Section 15 of `claude-frontend.md`.

For this initial execution step, generate the configuration and infrastructure code for **Phases 1 through 5**:

1. Provide the exact shell terminal commands to initialize the project using `create-next-app` and install the strict package list from Section 11 (including `jose`, `swr`, `antd`, and `socket.io-client`).
2. Generate the global theme provider: `src/providers/AntdProvider.tsx` using the exact design token specifications from Section 3.
3. Generate the global styles: `src/app/globals.css` containing the JetBrains Mono font imports, custom webkit scrollbars, and core status pulse animations.
4. Generate the route protection middleware: `src/middleware.ts` exactly as specified in Section 5.
5. Generate the server-side API Route proxy files: `src/app/api/auth/login/route.ts`, `src/app/api/auth/logout/route.ts`, and the generic proxy handler `src/app/api/proxy/[...path]/route.ts` ensuring the JWT cookie `uav_token` is seamlessly injected downstream as a Bearer token.

Present the step-by-step file generation breakdown for these five initial phases. Once I review the plan and click approve, you may execute the terminal setups and file writes directly in my workspace.
