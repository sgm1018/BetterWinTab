'use client'

import React, {
  useRef,
  useEffect,
  useCallback,
  useState,
} from 'react'
import {
  motion,
  useScroll,
  useSpring,
  useTransform,
  MotionValue,
} from 'framer-motion'

// ─── Window card data ──────────────────────────────────────────────────────────
interface WindowCard {
  title: string
  process: string
  folder: 'Dev' | 'Research' | 'Comms'
  folderColor: string
  processColor: string
}

const WINDOW_DATA: WindowCard[] = [
  // Dev (18 windows)
  { title: 'App.xaml.cs — BetterWinTab', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'MainViewModel.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'WindowOrchestration.tsx', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'FuzzyMatcher.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'HotkeyService.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'localhost:3000 — Chrome', process: 'chrome', folder: 'Dev', folderColor: '#39FF14', processColor: '#4285F4' },
  { title: 'npm run dev — Terminal', process: 'wt', folder: 'Dev', folderColor: '#39FF14', processColor: '#39FF14' },
  { title: 'git log — Terminal', process: 'wt', folder: 'Dev', folderColor: '#39FF14', processColor: '#39FF14' },
  { title: 'NativeMethods.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'Postman — GET /api/windows', process: 'Postman', folder: 'Dev', folderColor: '#39FF14', processColor: '#FF6C37' },
  { title: 'ThumbnailService.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'dotnet build — Terminal', process: 'wt', folder: 'Dev', folderColor: '#39FF14', processColor: '#39FF14' },
  { title: 'GitHub — BetterWinTab', process: 'chrome', folder: 'Dev', folderColor: '#39FF14', processColor: '#4285F4' },
  { title: 'Rider — BetterWinTab.sln', process: 'rider64', folder: 'Dev', folderColor: '#39FF14', processColor: '#FF318C' },
  { title: 'SettingsService.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'package.json — BetterWinTab', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'VirtualDesktopService.cs', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },
  { title: 'DEBUG — BetterWinTab.exe', process: 'Code', folder: 'Dev', folderColor: '#39FF14', processColor: '#007ACC' },

  // Research (15 windows)
  { title: 'DwmRegisterThumbnail — MDN', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Stack Overflow — WH_KEYBOARD_LL', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Framer Motion Docs', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'WinUI 3 Gallery', process: 'WinUIGallery', folder: 'Research', folderColor: '#8BE9FD', processColor: '#8BE9FD' },
  { title: 'Awwwards — Inspiration', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Architecture notes — Notion', process: 'Notion', folder: 'Research', folderColor: '#8BE9FD', processColor: '#FFFFFF' },
  { title: 'Win32 HWND lifecycle — Docs', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'MSDN — AttachThreadInput', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'CommunityToolkit.Mvvm', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Tailwind v3 Config Ref', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Obsidian — BetterWinTab', process: 'Obsidian', folder: 'Research', folderColor: '#8BE9FD', processColor: '#7C3AED' },
  { title: 'IVirtualDesktopManager COM', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'fzf algorithm internals', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Next.js App Router', process: 'chrome', folder: 'Research', folderColor: '#8BE9FD', processColor: '#4285F4' },
  { title: 'Product roadmap — Notion', process: 'Notion', folder: 'Research', folderColor: '#8BE9FD', processColor: '#FFFFFF' },

  // Comms (15 windows)
  { title: '#dev-general — Slack', process: 'slack', folder: 'Comms', folderColor: '#FFB86C', processColor: '#611f69' },
  { title: 'Stand-up call — Google Meet', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#4285F4' },
  { title: 'PR Review #142 — GitHub', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#4285F4' },
  { title: 'Gmail — Inbox (3)', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#EA4335' },
  { title: 'Calendar — Today', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#4285F4' },
  { title: 'Slack DM — UX review', process: 'slack', folder: 'Comms', folderColor: '#FFB86C', processColor: '#611f69' },
  { title: 'Figma — Landing page', process: 'figma', folder: 'Comms', folderColor: '#FFB86C', processColor: '#A259FF' },
  { title: 'Issue #87 — GitHub', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#4285F4' },
  { title: '#design — Slack', process: 'slack', folder: 'Comms', folderColor: '#FFB86C', processColor: '#611f69' },
  { title: 'Loom — walkthrough rec.', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#625DF5' },
  { title: 'Linear — Sprint backlog', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#5E6AD2' },
  { title: 'Figma — Component lib', process: 'figma', folder: 'Comms', folderColor: '#FFB86C', processColor: '#A259FF' },
  { title: 'Meet — 1:1 with PM', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#4285F4' },
  { title: 'Release notes — Notion', process: 'Notion', folder: 'Comms', folderColor: '#FFB86C', processColor: '#FFFFFF' },
  { title: 'Twitter/X — mentions', process: 'chrome', folder: 'Comms', folderColor: '#FFB86C', processColor: '#1DA1F2' },
]

// ─── Seeded pseudo-random (deterministic layout) ──────────────────────────────
function seededRandom(seed: number): () => number {
  let s = seed
  return () => {
    s = (s * 16807 + 0) % 2147483647
    return (s - 1) / 2147483646
  }
}

// ─── Card dimensions ───────────────────────────────────────────────────────────
const CARD_W = 168
const CARD_H = 96
const TITLE_BAR_H = 22

// ─── Canvas draw functions ─────────────────────────────────────────────────────
type Vec2 = { x: number; y: number }

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t
}

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
}

function drawCard(
  ctx: CanvasRenderingContext2D,
  card: WindowCard,
  x: number,
  y: number,
  rotation: number,
  alpha: number,
  scale: number,
  highlightFolder: string | null
): void {
  const w = CARD_W * scale
  const h = CARD_H * scale
  const tb = TITLE_BAR_H * scale
  const isHighlighted = highlightFolder === null || card.folder === highlightFolder

  ctx.save()
  ctx.globalAlpha = alpha * (isHighlighted ? 1.0 : 0.18)
  ctx.translate(x, y)
  ctx.rotate(rotation)

  // Card shadow (subtle)
  if (isHighlighted) {
    ctx.shadowBlur = 16 * scale
    ctx.shadowColor = 'rgba(0,0,0,0.6)'
  }

  // Card body
  ctx.fillStyle = '#0D0D0D'
  ctx.fillRect(-w / 2, -h / 2, w, h)
  ctx.shadowBlur = 0

  // Card border
  ctx.strokeStyle = isHighlighted ? card.folderColor + '33' : '#1a1a1a'
  ctx.lineWidth = scale
  ctx.strokeRect(-w / 2, -h / 2, w, h)

  // Title bar
  ctx.fillStyle = '#111111'
  ctx.fillRect(-w / 2, -h / 2, w, tb)

  // Title bar accent line (top edge)
  if (isHighlighted) {
    ctx.fillStyle = card.folderColor
    ctx.fillRect(-w / 2, -h / 2, w, scale * 1.5)
  }

  // Process dot
  const dotR = 4 * scale
  ctx.beginPath()
  ctx.arc(-w / 2 + 10 * scale, -h / 2 + tb / 2, dotR, 0, Math.PI * 2)
  ctx.fillStyle = card.processColor
  ctx.fill()

  // Title text
  ctx.font = `${Math.max(9, 10 * scale)}px JetBrains Mono, monospace`
  ctx.fillStyle = isHighlighted ? 'rgba(255,255,255,0.85)' : 'rgba(255,255,255,0.3)'
  ctx.textBaseline = 'middle'

  const titleX = -w / 2 + 20 * scale
  const titleMaxW = w - 26 * scale
  const titleText = card.title.length > 22 ? card.title.slice(0, 22) + '…' : card.title
  ctx.fillText(titleText, titleX, -h / 2 + tb / 2, titleMaxW)

  // Content lines (simulated window content)
  const lineY1 = -h / 2 + tb + 12 * scale
  const lineY2 = lineY1 + 10 * scale
  const lineY3 = lineY2 + 10 * scale

  ctx.fillStyle = 'rgba(255,255,255,0.06)'
  ctx.fillRect(-w / 2 + 10 * scale, lineY1, w * 0.75, 4 * scale)
  ctx.fillRect(-w / 2 + 10 * scale, lineY2, w * 0.55, 4 * scale)
  ctx.fillRect(-w / 2 + 10 * scale, lineY3, w * 0.35, 4 * scale)

  ctx.restore()
}

function drawFolderLabel(
  ctx: CanvasRenderingContext2D,
  label: string,
  color: string,
  x: number,
  y: number,
  alpha: number,
  scale: number
): void {
  ctx.save()
  ctx.globalAlpha = alpha
  ctx.font = `${Math.ceil(11 * scale)}px JetBrains Mono, monospace`
  ctx.fillStyle = color
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(label, x, y)
  // underline
  const tw = ctx.measureText(label).width
  ctx.fillRect(x - tw / 2, y + 9 * scale, tw, scale)
  ctx.restore()
}

// ─── Main component ────────────────────────────────────────────────────────────
export default function WindowOrchestration() {
  const containerRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rafRef = useRef<number>(0)
  const imagesLoadedRef = useRef(false)

  // Scroll tracking
  const { scrollYProgress } = useScroll({
    target: containerRef,
    offset: ['start start', 'end end'],
  })

  const smoothProgress = useSpring(scrollYProgress, {
    stiffness: 100,
    damping: 30,
    restDelta: 0.0001,
  })

  // Pre-compute positions on mount
  const chaosPositions = useRef<Vec2[]>([])
  const chaosRotations = useRef<number[]>([])
  const organizedPositions = useRef<Vec2[]>([])

  const computePositions = useCallback((cw: number, ch: number) => {
    const rng = seededRandom(42)
    const total = WINDOW_DATA.length

    // --- Chaos positions: scattered across canvas ---
    chaosPositions.current = WINDOW_DATA.map((_, i) => {
      const angle = (i / total) * Math.PI * 2 + rng() * 0.8
      const rx = (rng() * 0.36 + 0.08) * cw
      const ry = (rng() * 0.36 + 0.08) * ch
      return {
        x: cw / 2 + Math.cos(angle) * rx * (rng() > 0.5 ? 1 : -1),
        y: ch / 2 + Math.sin(angle) * ry * (rng() > 0.5 ? 1 : -1),
      }
    })

    // --- Chaos rotations: ±25° ---
    chaosRotations.current = WINDOW_DATA.map(() => (rng() - 0.5) * 0.44)

    // --- Organized positions: 3 folder columns ---
    const folders = ['Dev', 'Research', 'Comms'] as const
    const folderCounts = { Dev: 0, Research: 0, Comms: 0 }
    WINDOW_DATA.forEach(w => folderCounts[w.folder]++)

    const colW = cw / 3
    const cardScale = Math.min(cw / 1200, 1)
    const cw_ = CARD_W * cardScale
    const ch_ = CARD_H * cardScale
    const gapX = Math.max(4, 8 * cardScale)
    const gapY = Math.max(4, 10 * cardScale)

    const counts: Record<string, number> = { Dev: 0, Research: 0, Comms: 0 }
    organizedPositions.current = WINDOW_DATA.map(card => {
      const colIdx = folders.indexOf(card.folder)
      const idx = counts[card.folder]++
      const perRow = 3
      const col2 = idx % perRow
      const row = Math.floor(idx / perRow)
      const colCenterX = colW * colIdx + colW / 2
      const gridW = perRow * (cw_ + gapX) - gapX
      const startX = colCenterX - gridW / 2 + cw_ / 2
      const totalRows = Math.ceil(folderCounts[card.folder] / perRow)
      const gridH = totalRows * (ch_ + gapY) - gapY
      const startY = ch / 2 - gridH / 2 + ch_ / 2 + 24 * cardScale
      return {
        x: startX + col2 * (cw_ + gapX),
        y: startY + row * (ch_ + gapY),
      }
    })
  }, [])

  // ─── Draw loop ───────────────────────────────────────────────────────────────
  const drawFrame = useCallback(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const cw = canvas.width
    const ch = canvas.height
    const progress = smoothProgress.get()
    const cardScale = Math.min(cw / 1200, 1)

    // Clear
    ctx.clearRect(0, 0, cw, ch)
    ctx.fillStyle = '#050505'
    ctx.fillRect(0, 0, cw, ch)

    // ── Determine animation state from scroll progress ──
    // 0.00-0.15 → pure chaos
    // 0.15-0.65 → lerp 0→1 (chaos → organized)
    // 0.65-1.00 → pure organized
    let t = 0
    if (progress < 0.15) {
      t = 0
    } else if (progress < 0.65) {
      t = easeInOutCubic((progress - 0.15) / 0.50)
    } else {
      t = 1
    }

    // Highlight folder based on scroll segment
    // 0.20-0.40 → highlight nothing (search beat)—show all dimmed except a few
    // 0.40-0.65 → highlight "Dev"
    // 0.65-0.80 → highlight all (organized)
    let highlightFolder: string | null = null
    if (progress > 0.30 && progress < 0.50) {
      highlightFolder = 'Dev'
    } else if (progress > 0.50 && progress < 0.65) {
      highlightFolder = null // show all during transition
    }

    // Draw folder labels (appear at t > 0.5)
    const labelAlpha = Math.max(0, Math.min(1, (t - 0.6) / 0.2))
    if (labelAlpha > 0) {
      const folders = [
        { name: '// DEV_ENVIRONMENT', color: '#39FF14', colIdx: 0 },
        { name: '// RESEARCH', color: '#8BE9FD', colIdx: 1 },
        { name: '// COMMS', color: '#FFB86C', colIdx: 2 },
      ]
      folders.forEach(f => {
        const x = (cw / 3) * f.colIdx + cw / 6
        drawFolderLabel(ctx, f.name, f.color, x, 28 * cardScale, labelAlpha, cardScale)
      })

      // Column separator lines
      ctx.save()
      ctx.globalAlpha = labelAlpha * 0.15
      ctx.strokeStyle = '#444444'
      ctx.lineWidth = 1
      ctx.setLineDash([4, 4])
      for (let c = 1; c < 3; c++) {
        const lx = (cw / 3) * c
        ctx.beginPath()
        ctx.moveTo(lx, 0)
        ctx.lineTo(lx, ch)
        ctx.stroke()
      }
      ctx.setLineDash([])
      ctx.restore()
    }

    // Draw each card interpolated between chaos and organized
    WINDOW_DATA.forEach((card, i) => {
      const chaos = chaosPositions.current[i]
      const org = organizedPositions.current[i]
      if (!chaos || !org) return

      const x = lerp(chaos.x, org.x, t)
      const y = lerp(chaos.y, org.y, t)
      const rot = lerp(chaosRotations.current[i] || 0, 0, t)

      // Alpha: cards always visible but some dim in search beat
      let alpha = 1
      if (progress > 0.20 && progress < 0.45 && highlightFolder === 'Dev') {
        alpha = card.folder === 'Dev' ? 1 : 0.12
      }

      drawCard(ctx, card, x, y, rot, alpha, cardScale, null)
    })

    // In the search beat (0.20-0.35), draw a simulated search overlay
    if (progress > 0.20 && progress < 0.38) {
      const searchAlpha = Math.min(1, Math.min(
        (progress - 0.20) / 0.06,
        (0.38 - progress) / 0.06
      ))
      drawSearchOverlay(ctx, cw, ch, progress, searchAlpha, cardScale)
    }
  }, [smoothProgress])

  function drawSearchOverlay(
    ctx: CanvasRenderingContext2D,
    cw: number,
    ch: number,
    progress: number,
    alpha: number,
    scale: number
  ) {
    const inputW = 320 * scale
    const inputH = 38 * scale
    const ix = cw / 2 - inputW / 2
    const iy = ch / 2 - inputH / 2

    ctx.save()
    ctx.globalAlpha = alpha

    // Input box
    ctx.fillStyle = '#0A0A0A'
    ctx.fillRect(ix, iy, inputW, inputH)
    ctx.strokeStyle = '#39FF14'
    ctx.lineWidth = scale
    ctx.strokeRect(ix, iy, inputW, inputH)

    // Typed text simulation
    const queries = ['g', 'go', 'goo', 'goo ', 'goo cr', 'goo cro', 'goo crom', 'goo crome']
    const qIdx = Math.min(
      queries.length - 1,
      Math.floor(((progress - 0.20) / 0.17) * queries.length)
    )
    const typed = queries[qIdx] || ''

    ctx.font = `${Math.ceil(13 * scale)}px JetBrains Mono, monospace`
    ctx.fillStyle = '#ffffff'
    ctx.textBaseline = 'middle'
    ctx.fillText(`> ${typed}█`, ix + 12 * scale, iy + inputH / 2)

    // Result hint
    if (qIdx >= 5) {
      ctx.font = `${Math.ceil(11 * scale)}px JetBrains Mono, monospace`
      ctx.fillStyle = '#39FF14'
      ctx.fillText('✓  Gmail - Google Chrome', ix, iy + inputH + 16 * scale)
    }

    ctx.restore()
  }

  // ─── RAF loop ────────────────────────────────────────────────────────────────
  useEffect(() => {
    const loop = () => {
      drawFrame()
      rafRef.current = requestAnimationFrame(loop)
    }
    rafRef.current = requestAnimationFrame(loop)
    return () => cancelAnimationFrame(rafRef.current)
  }, [drawFrame])

  // ─── Resize handler ──────────────────────────────────────────────────────────
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const resize = () => {
      const vw = window.innerWidth
      const vh = window.innerHeight
      const dpr = window.devicePixelRatio || 1
      canvas.width = vw * dpr
      canvas.height = vh * dpr
      canvas.style.width = `${vw}px`
      canvas.style.height = `${vh}px`
      const ctx = canvas.getContext('2d')
      if (ctx) ctx.scale(dpr, dpr)
      computePositions(vw, vh)
    }

    resize()
    window.addEventListener('resize', resize)
    imagesLoadedRef.current = true
    return () => window.removeEventListener('resize', resize)
  }, [computePositions])

  // ─── Text beat config ────────────────────────────────────────────────────────
  // Each beat: [scrollStart, scrollEnd, content]
  const beats = [
    {
      range: [0.02, 0.18] as [number, number],
      align: 'center' as const,
      label: '// DIAGNOSIS',
      title: 'Your Alt+Tab\nis O(n).',
      sub: '50 open windows. No structure. Just chaos\nyou\'ve accepted as normal.',
    },
    {
      range: [0.22, 0.42] as [number, number],
      align: 'left' as const,
      label: '// FEATURE_02',
      title: 'Type wrong.\nFind right.',
      sub: 'A custom fuzzy engine with consecutive bonuses,\nword-boundary rewards, and multi-token decomposition.',
    },
    {
      range: [0.48, 0.68] as [number, number],
      align: 'right' as const,
      label: '// FEATURE_01',
      title: 'The folder\nthat fills itself.',
      sub: 'Smart Folders watch your running processes in real time.\nDefine a rule once. Never touch it again.',
    },
    {
      range: [0.74, 0.96] as [number, number],
      align: 'center' as const,
      label: '// CTA',
      title: 'Take back\nyour screen.',
      sub: 'Free. Open source. Windows 10/11. Not Electron.',
      isCTA: true,
    },
  ]

  return (
    <div
      ref={containerRef}
      id="canvas-container"
      style={{ height: '400vh' }}
      className="relative w-full"
    >
      {/* Sticky canvas viewport */}
      <div className="sticky top-0 h-screen w-full overflow-hidden">
        <canvas
          ref={canvasRef}
          className="absolute inset-0"
          style={{ imageRendering: 'pixelated' }}
        />

        {/* Text beat overlays */}
        {beats.map((beat, i) => (
          <BeatOverlay
            key={i}
            scrollYProgress={scrollYProgress}
            range={beat.range}
            align={beat.align}
            label={beat.label}
            title={beat.title}
            sub={beat.sub}
            isCTA={(beat as { isCTA?: boolean }).isCTA}
          />
        ))}

        {/* Scroll indicator (fades out at 10%) */}
        <ScrollIndicator scrollYProgress={scrollYProgress} />
      </div>
    </div>
  )
}

// ─── Beat overlay ──────────────────────────────────────────────────────────────
interface BeatOverlayProps {
  scrollYProgress: MotionValue<number>
  range: [number, number]
  align: 'left' | 'right' | 'center'
  label: string
  title: string
  sub: string
  isCTA?: boolean
}

function BeatOverlay({
  scrollYProgress,
  range,
  align,
  label,
  title,
  sub,
  isCTA,
}: BeatOverlayProps) {
  const [start, end] = range
  const fadeIn = start + (end - start) * 0.1
  const fadeOut = end - (end - start) * 0.1

  const opacity = useTransform(
    scrollYProgress,
    [start, fadeIn, fadeOut, end],
    [0, 1, 1, 0]
  )
  const yEnter = useTransform(
    scrollYProgress,
    [start, fadeIn],
    [20, 0]
  )
  const yExit = useTransform(
    scrollYProgress,
    [fadeOut, end],
    [0, -20]
  )

  const posClass =
    align === 'left'
      ? 'items-start left-8 md:left-16 right-auto max-w-lg'
      : align === 'right'
      ? 'items-end right-8 md:right-16 left-auto max-w-lg'
      : 'items-center left-1/2 -translate-x-1/2 text-center max-w-2xl'

  return (
    <motion.div
      style={{ opacity, y: yEnter || yExit }}
      className={`absolute bottom-16 md:bottom-24 flex flex-col gap-3 pointer-events-none z-10 px-4 ${posClass}`}
    >
      <span
        className="section-label"
        style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, letterSpacing: '4px', color: '#39FF14', textTransform: 'uppercase' }}
      >
        {label}
      </span>

      <h2
        style={{
          fontFamily: 'JetBrains Mono, monospace',
          fontSize: 'clamp(2.4rem, 6vw, 5rem)',
          fontWeight: 700,
          lineHeight: 1.05,
          letterSpacing: '-0.03em',
          color: 'rgba(255,255,255,0.92)',
          whiteSpace: 'pre-line',
          textAlign: align === 'center' ? 'center' : align,
        }}
      >
        {title}
      </h2>

      <p
        style={{
          fontFamily: 'Inter, sans-serif',
          fontSize: 'clamp(0.875rem, 1.5vw, 1rem)',
          lineHeight: 1.65,
          color: 'rgba(255,255,255,0.55)',
          whiteSpace: 'pre-line',
          textAlign: align === 'center' ? 'center' : align,
        }}
      >
        {sub}
      </p>

      {isCTA && (
        <div
          className="flex flex-wrap gap-3 justify-center mt-4 pointer-events-auto"
        >
          <a
            href="https://github.com/"
            className="cta-primary px-6 py-3 text-sm font-bold tracking-tight"
            style={{ fontFamily: 'JetBrains Mono, monospace', cursor: 'crosshair' }}
          >
            ↓ Download Free — Windows 10/11
          </a>
          <a
            href="https://github.com/"
            className="cta-secondary px-6 py-3 text-sm"
            style={{ fontFamily: 'JetBrains Mono, monospace', cursor: 'crosshair' }}
          >
            View Source on GitHub
          </a>
        </div>
      )}
    </motion.div>
  )
}

// ─── Scroll indicator ──────────────────────────────────────────────────────────
function ScrollIndicator({ scrollYProgress }: { scrollYProgress: MotionValue<number> }) {
  const opacity = useTransform(scrollYProgress, [0, 0.08], [1, 0])

  return (
    <motion.div
      style={{ opacity }}
      className="absolute bottom-8 left-1/2 -translate-x-1/2 flex flex-col items-center gap-2 pointer-events-none z-20"
    >
      <span
        style={{
          fontFamily: 'JetBrains Mono, monospace',
          fontSize: 11,
          letterSpacing: '3px',
          color: 'rgba(255,255,255,0.35)',
          textTransform: 'uppercase',
        }}
      >
        Scroll to Explore
      </span>
      <motion.div
        animate={{ y: [0, 6, 0] }}
        transition={{ repeat: Infinity, duration: 1.5, ease: 'easeInOut' }}
        style={{ width: 1, height: 28, background: 'linear-gradient(to bottom, #39FF14, transparent)' }}
      />
    </motion.div>
  )
}
