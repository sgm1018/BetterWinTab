import type { Metadata } from 'next'
import './globals.css'

export const metadata: Metadata = {
  title: 'BetterWinTab — Your Alt+Tab is O(n). Fix it.',
  description:
    'Replace Windows\' flat, context-free window switcher with a 2D semantic workspace. Smart Folders, fuzzy search, live DWM previews. Built on .NET 8. Not Electron.',
  openGraph: {
    title: 'BetterWinTab — Your Alt+Tab is O(n). Fix it.',
    description:
      'Smart Folders. Fuzzy Search. Live DWM Previews. 11.5 min/day recovered. Free, open-source, Windows 10/11.',
    type: 'website',
    images: [{ url: '/og-image.png', width: 1200, height: 630 }],
  },
  twitter: {
    card: 'summary_large_image',
    title: 'BetterWinTab — Your Alt+Tab is O(n). Fix it.',
    description:
      'Smart Folders. Fuzzy Search. Live DWM Previews. 11.5 min/day recovered.',
  },
  keywords: [
    'windows task switcher',
    'alt+tab replacement',
    'window manager',
    'windows productivity',
    'WinUI 3',
    '.NET 8',
    'open source',
    'power user',
    'smart folders',
    'fuzzy search',
  ],
  robots: { index: true, follow: true },
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html lang="en" className="scroll-auto">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link
          rel="preconnect"
          href="https://fonts.gstatic.com"
          crossOrigin="anonymous"
        />
        <link
          href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;600;700&family=Inter:wght@300;400;500;600;700&display=swap"
          rel="stylesheet"
        />
      </head>
      <body
        suppressHydrationWarning
        className="bg-bg text-white antialiased overflow-x-hidden"
        style={{ backgroundColor: '#050505' }}
      >
        {children}
      </body>
    </html>
  )
}
