import type { Metadata } from 'next';
import './globals.css';
import { AntdProvider } from '@/providers/AntdProvider';
import { SWRProvider } from '@/providers/SWRProvider';

export const metadata: Metadata = {
  title: 'UAV Drone Detection System',
  description:
    'Real-time AIoT dashboard for monitoring RF-based UAV detection alerts, ' +
    'radar device health, and historical telemetry logs.',
  keywords: ['UAV', 'drone detection', 'radar', 'RF', 'AIoT', 'monitoring'],
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>
        <AntdProvider>
          <SWRProvider>
            {children}
          </SWRProvider>
        </AntdProvider>
      </body>
    </html>
  );
}

