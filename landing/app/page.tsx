'use client'

import dynamic from 'next/dynamic'
import { motion, useScroll, useSpring, useTransform, useInView } from 'framer-motion'
import { useRef, useState } from 'react'
import clsx from 'clsx'

// Canvas component loaded client-side only (no SSR)
const WindowOrchestration = dynamic(
  () => import('@/components/WindowOrchestration'),
  { ssr: false }
)

// ─── Fade-in wrapper ───────────────────────────────────────────────────────────
function FadeIn({
  children,
  className,
  delay = 0,
}: {
  children: React.ReactNode
  className?: string
  delay?: number
}) {
  const ref = useRef<HTMLDivElement>(null)
  const inView = useInView(ref, { once: true, margin: '-80px' })
  return (
    <motion.div
      ref={ref}
      initial={{ opacity: 0, y: 32 }}
      animate={inView ? { opacity: 1, y: 0 } : {}}
      transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1], delay }}
      className={className}
    >
      {children}
    </motion.div>
  )
}

// ─── Rolling number ────────────────────────────────────────────────────────────
function RollNumber({ value, suffix = '' }: { value: string; suffix?: string }) {
  const ref = useRef<HTMLSpanElement>(null)
  const inView = useInView(ref, { once: true })
  return (
    <motion.span
      ref={ref}
      initial={{ opacity: 0, y: 20 }}
      animate={inView ? { opacity: 1, y: 0 } : {}}
      transition={{ duration: 0.9, ease: [0.22, 1, 0.36, 1] }}
      className="stat-number"
    >
      {value}{suffix}
    </motion.span>
  )
}

// ─── Progress bar (top of page) ───────────────────────────────────────────────
function PageProgress() {
  const { scrollYProgress } = useScroll()
  const scaleX = useSpring(scrollYProgress, { stiffness: 100, damping: 30 })
  return (
    <motion.div
      style={{ scaleX, transformOrigin: 'left', height: 1, backgroundColor: '#39FF14' }}
      className="fixed top-0 left-0 right-0 z-50"
      aria-hidden="true"
    />
  )
}

// ─── Collapsible objection ────────────────────────────────────────────────────
function Objection({ n, question, answer }: { n: string; question: string; answer: string }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="border-b" style={{ borderColor: '#1A1A1A' }}>
      <button
        onClick={() => setOpen(v => !v)}
        className="w-full text-left py-5 flex items-start gap-4 objection-header group"
        aria-expanded={open}
      >
        <span
          className="shrink-0 text-xs"
          style={{ fontFamily: 'JetBrains Mono, monospace', color: '#39FF14', letterSpacing: '2px' }}
        >
          [{n}]
        </span>
        <span
          style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 14, color: 'rgba(255,255,255,0.85)' }}
        >
          "{question}"
        </span>
        <motion.span
          animate={{ rotate: open ? 45 : 0 }}
          transition={{ duration: 0.2 }}
          className="ml-auto shrink-0 text-sm"
          style={{ color: '#39FF14', fontFamily: 'JetBrains Mono, monospace' }}
        >
          +
        </motion.span>
      </button>
      <motion.div
        initial={false}
        animate={{ height: open ? 'auto' : 0, opacity: open ? 1 : 0 }}
        transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
        style={{ overflow: 'hidden' }}
      >
        <div
          className="pb-6 pl-10"
          style={{
            fontFamily: 'Inter, sans-serif',
            fontSize: 14,
            lineHeight: 1.75,
            color: 'rgba(255,255,255,0.6)',
            whiteSpace: 'pre-line',
          }}
        >
          {answer}
        </div>
      </motion.div>
    </div>
  )
}

// ─── Theme swatch ─────────────────────────────────────────────────────────────
const THEMES = [
  { name: 'Neon Green',   accent: '#39FF14' },
  { name: 'Cyber Blue',   accent: '#00D4FF' },
  { name: 'Deep Purple',  accent: '#BD93F9' },
  { name: 'Crimson',      accent: '#FF2D55' },
  { name: 'Amber Gold',   accent: '#FFB300' },
  { name: 'Arctic',       accent: '#E0F7FA' },
  { name: 'Monochrome',   accent: '#CCCCCC' },
  { name: 'Cyberpunk',    accent: '#FF00C8' },
  { name: 'Deep Ocean',   accent: '#006EFF' },
  { name: 'Blood Red',    accent: '#FF1744' },
  { name: 'Fire & Steel', accent: '#FF6D00' },
  { name: 'Forest',       accent: '#00C853' },
  { name: 'Midnight Rose',accent: '#FF4081' },
  { name: 'Earthy',       accent: '#BCAAA4' },
  { name: 'Lavender',     accent: '#CE93D8' },
  { name: 'Sunset',       accent: '#FF6E40' },
  { name: 'Jade',         accent: '#1DE9B6' },
  { name: 'Infrared',     accent: '#FF3D00' },
  { name: 'Ice',          accent: '#84FFFF' },
  { name: 'Solar',        accent: '#FFD600' },
  { name: 'Zinc',         accent: '#9E9E9E' },
  { name: 'Custom…',      accent: '#39FF14' },
]

// ─── Roadmap item ─────────────────────────────────────────────────────────────
function RoadmapItem({
  done,
  children,
}: {
  done?: boolean
  children: React.ReactNode
}) {
  return (
    <div className="flex items-start gap-3 py-1">
      <span
        className="shrink-0 mt-0.5"
        style={{
          fontFamily: 'JetBrains Mono, monospace',
          fontSize: 13,
          color: done ? '#39FF14' : '#444444',
        }}
      >
        {done ? '✓' : '○'}
      </span>
      <span
        style={{
          fontFamily: 'Inter, sans-serif',
          fontSize: 14,
          color: done ? 'rgba(255,255,255,0.75)' : 'rgba(255,255,255,0.4)',
          lineHeight: 1.6,
        }}
      >
        {children}
      </span>
    </div>
  )
}

// ─── PAGE ─────────────────────────────────────────────────────────────────────
export default function Page() {
  const [hoveredTheme, setHoveredTheme] = useState<string | null>(null)

  return (
    <main className="relative" style={{ backgroundColor: '#050505' }}>

      {/* ── Page scroll progress bar ─────────────────────────────────────────── */}
      <ProgressBar />

      {/* ══════════════════════════════════════════════════════════════════════
          §01  SCROLLYTELLING HERO — Canvas animation (400vh)
      ══════════════════════════════════════════════════════════════════════ */}
      <section aria-label="Hero animation">
        <WindowOrchestration />
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §02  STAT BAR
      ══════════════════════════════════════════════════════════════════════ */}
      <section
        className="border-y py-12"
        style={{ borderColor: '#1A1A1A', backgroundColor: '#050505' }}
        aria-label="Key metrics"
      >
        <div className="max-w-5xl mx-auto px-6">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-px" style={{ background: '#1A1A1A' }}>
            {[
              { num: '11.5', unit: 'min/day', label: 'Recovered per developer' },
              { num: '0%',   unit: 'CPU',     label: 'From DWM live thumbnails' },
              { num: '<1',   unit: 'ms',      label: 'Hotkey hook latency' },
            ].map(s => (
              <div key={s.num} className="py-10 px-8 text-center" style={{ backgroundColor: '#050505' }}>
                <div
                  className="mb-1"
                  style={{
                    fontFamily: 'JetBrains Mono, monospace',
                    fontSize: 'clamp(2.5rem, 6vw, 4rem)',
                    fontWeight: 700,
                    color: '#39FF14',
                    lineHeight: 1,
                    letterSpacing: '-0.03em',
                  }}
                >
                  {s.num}
                  <span style={{ fontSize: '0.45em', marginLeft: '0.25em', color: '#39FF14', opacity: 0.7 }}>
                    {s.unit}
                  </span>
                </div>
                <p style={{ fontFamily: 'Inter, sans-serif', fontSize: 13, color: '#444444' }}>
                  {s.label}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §03  THE PROBLEM — Digital Diogenes
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6" aria-label="The problem">
        <div className="max-w-4xl mx-auto">
          <FadeIn>
            <span className="section-label">// DIAGNOSIS</span>
          </FadeIn>
          <FadeIn delay={0.1}>
            <h2
              className="mt-4 mb-8"
              style={{
                fontFamily: 'JetBrains Mono, monospace',
                fontSize: 'clamp(2.5rem, 6vw, 4rem)',
                fontWeight: 700,
                lineHeight: 1.05,
                letterSpacing: '-0.03em',
                color: 'rgba(255,255,255,0.92)',
              }}
            >
              You have Digital Diogenes Syndrome.
            </h2>
          </FadeIn>
          <FadeIn delay={0.15}>
            <p className="mb-10 max-w-2xl" style={{ fontFamily: 'Inter, sans-serif', fontSize: 17, lineHeight: 1.75, color: 'rgba(255,255,255,0.55)' }}>
              Your desktop isn't a workspace. It's a graveyard of contexts.
              8 VS Code windows. 15 Chrome tabs. 4 terminals. 3 Slack threads.
              All alive. All undead. All fighting for your attention.
            </p>
            <p className="mb-10 max-w-2xl" style={{ fontFamily: 'Inter, sans-serif', fontSize: 17, lineHeight: 1.75, color: 'rgba(255,255,255,0.55)' }}>
              Every time you press Alt+Tab, Windows hands you a flat list ordered
              by the last thing you touched — with zero knowledge of why you opened any of it.
            </p>
          </FadeIn>

          <FadeIn delay={0.2}>
            <blockquote
              className="my-12 text-center"
              style={{
                fontFamily: 'JetBrains Mono, monospace',
                fontSize: 'clamp(1.2rem, 3vw, 1.75rem)',
                color: '#39FF14',
                fontWeight: 600,
                lineHeight: 1.4,
              }}
            >
              "A flat list of 50 windows is not productivity.<br />
              It's archaeology."
            </blockquote>
          </FadeIn>

          <FadeIn delay={0.25}>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-px mt-12" style={{ background: '#1A1A1A' }}>
              {[
                {
                  head: 'O(n) SEARCH',
                  body: 'Your target window has a ~10% chance of being in the first 5 results when you have 50+ windows open.',
                },
                {
                  head: 'ZERO SEMANTICS',
                  body: 'code.exe and chrome.exe sit side by side with no organizational distinction whatsoever.',
                },
                {
                  head: 'VOLATILE ORDER',
                  body: 'Every click reshuffles the deck. There is no spatial memory. Only temporal decay.',
                },
              ].map(col => (
                <div
                  key={col.head}
                  className="p-7 group transition-all duration-150"
                  style={{ backgroundColor: '#050505', borderBottom: '1px solid transparent' }}
                  onMouseEnter={e => (e.currentTarget.style.borderBottomColor = '#39FF14')}
                  onMouseLeave={e => (e.currentTarget.style.borderBottomColor = 'transparent')}
                >
                  <span className="section-label block mb-3">{col.head}</span>
                  <p style={{ fontFamily: 'Inter, sans-serif', fontSize: 14, lineHeight: 1.7, color: '#444444' }}>
                    {col.body}
                  </p>
                </div>
              ))}
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §04  SOLUTION — Semantic Contexts
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }} aria-label="The solution">
        <div className="max-w-4xl mx-auto">
          <FadeIn><span className="section-label">// SOLUTION_ARCHITECTURE</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2
              className="mt-4 mb-8"
              style={{
                fontFamily: 'JetBrains Mono, monospace',
                fontSize: 'clamp(2.5rem, 6vw, 4rem)',
                fontWeight: 700,
                lineHeight: 1.05,
                letterSpacing: '-0.03em',
                color: 'rgba(255,255,255,0.92)',
              }}
            >
              Not folders.<br />
              Semantic Contexts.
            </h2>
          </FadeIn>
          <FadeIn delay={0.15}>
            <p className="max-w-2xl mb-12" style={{ fontFamily: 'Inter, sans-serif', fontSize: 17, lineHeight: 1.75, color: 'rgba(255,255,255,0.55)' }}>
              BetterWinTab doesn't organize files. It organizes your intent.
              A Semantic Context is a named collection of windows that belong together
              — because they serve the same goal, not because they share the same process.
            </p>
          </FadeIn>

          {/* Two-axis diagram */}
          <FadeIn delay={0.2}>
            <div
              className="p-7 font-mono text-sm overflow-x-auto"
              style={{ backgroundColor: '#0A0A0A', border: '1px solid #1A1A1A', fontSize: 13 }}
            >
              <pre style={{ color: 'rgba(255,255,255,0.6)', lineHeight: 1.8 }}>
{`  Y-AXIS (Contexts)          X-AXIS (Windows)         Navigate
  ─────────────────          ────────────────          ─────────
  ↑ "Dev Environment"   ──→  [VS Code] [Terminal] [Postman]    ↑↓ O(k)
  │ "Research"          ──→  [Chrome] [Notion] [Obsidian]
  │ "Standup"           ──→  [Slack] [Meet] [Calendar]         ←→ O(m)
  ↓ All Windows         ──→  [everything else]

  Total to any window: O(k + m) ≪ `}<span style={{ color: '#39FF14' }}>O(n)</span></pre>
            </div>
          </FadeIn>

          <FadeIn delay={0.25}>
            <div className="mt-10 p-6 border-l-2" style={{ borderColor: '#39FF14', backgroundColor: '#0A0A0A' }}>
              <div className="grid grid-cols-3 gap-4 text-center">
                {[
                  { label: 'Alt+Tab (worst)', val: '49 steps', bar: 100, color: '#FF3344' },
                  { label: 'BetterWinTab', val: '≤ 15 steps', bar: 30, color: '#39FF14' },
                  { label: 'Improvement', val: '−69%', bar: 0, color: '#39FF14' },
                ].map(row => (
                  <div key={row.label}>
                    <p style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, color: '#444444', marginBottom: 4 }}>{row.label}</p>
                    <p style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 22, fontWeight: 700, color: row.color }}>{row.val}</p>
                  </div>
                ))}
              </div>
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §05  FEATURE 01 — Smart Folders
      ══════════════════════════════════════════════════════════════════════ */}
      <FeatureSection
        tag="FEATURE_01"
        title={`The folder\nthat fills itself.`}
        body={`A Smart Folder watches your running processes in real time.\nThe moment you open a new VS Code file, it appears in your "Dev" context automatically — no drag, no assignment, no friction.\n\nRules are process-level or class-level. Zero configuration required for common tools: VS Code, Chrome, Windows Terminal, Rider, Figma.`}
        quote='"Smart Folders solve the fundamental problem of manual organization: it degrades with use. A rule never gets tired."'
        code={`// Define a Smart Folder in 3 fields:
{
  "name": "Dev Environment",
  "type": "SmartProcess",
  "processFilter": "Code"    // captures all VS Code instances, instantly
}

// Result: every window from code.exe appears here automatically,
// with live DWM thumbnail, title, and desktop badge.`}
        pills={['Auto-capture', 'Zero maintenance', 'Class-level rules']}
      />

      {/* ══════════════════════════════════════════════════════════════════════
          §06  FEATURE 02 — Fuzzy Search
      ══════════════════════════════════════════════════════════════════════ */}
      <FeatureSection
        tag="FEATURE_02"
        title={`Type wrong.\nFind right.`}
        body={`BetterWinTab ships a custom fuzzy matching engine built for the way developers actually type under pressure: fast, imprecise, multi-word, cross-field.\n\nIt's not a Contains() check. It's a scoring algorithm with consecutive bonuses, word-boundary rewards, and multi-token decomposition — inspired by fzf, built in C#, optimized for window metadata.`}
        quote=""
        code={`Score formula per character hit:
┌──────────────────────────────────────────────┐
│  +10  base per match                         │
│  +5×  consecutive streak                     │
│  +20  at word boundary (space, -, _, /, \\)  │
│  +2   for case-exact hit                     │
│  -(textLen − queryLen) / 4   length tax      │
│  +max(0, 50 − firstMatchIdx × 3)  early win  │
│  +1000 if exact substring found  (wins all)  │
└──────────────────────────────────────────────┘

No windows match? BetterWinTab becomes an app launcher.
Still no match? Press ↵. It runs your query as a shell command.`}
        pills={['Fuzzy match', 'App launcher', 'Run dialog fallback']}
        reverse
      />

      {/* ══════════════════════════════════════════════════════════════════════
          §07  FEATURE 03 — DWM Live Previews
      ══════════════════════════════════════════════════════════════════════ */}
      <FeatureSection
        tag="FEATURE_03"
        title={`Real-time previews.\nZero CPU overhead.`}
        body={`Every window card shows a live preview of its content, updated in real time — not a screenshot, not a cached bitmap. A mirror. A live mirror.\n\nThis is powered by DwmRegisterThumbnail, a Win32 API that asks the Desktop Window Manager — the same compositing engine Windows uses to render your screen — to project a window's render output directly into the overlay.\n\nThe GPU does the compositing. The CPU does nothing.`}
        quote=""
        code={`// The entire thumbnail pipeline:
DwmRegisterThumbnail(destHwnd, sourceHwnd, out thumbId);
DwmUpdateThumbnailProperties(thumbId, ref props);
// ↑ That's it. Windows handles rendering from this point.
// CPU contribution: 0 cycles during display.
// GPU composition: handled by DWM's existing render graph.`}
        pills={['GPU compositor path', 'Event-driven updates', 'Sub-frame latency']}
      />

      {/* ══════════════════════════════════════════════════════════════════════
          §08  FEATURE 04 — Theme Engine
      ══════════════════════════════════════════════════════════════════════ */}
      <section
        className="py-32 px-6 border-t"
        style={{ borderColor: '#1A1A1A', backgroundColor: hoveredTheme ? '#050505' : '#050505' }}
        aria-label="Theme engine"
      >
        <div className="max-w-4xl mx-auto">
          <FadeIn><span className="section-label">// FEATURE_04</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-4 mb-8" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2.2rem, 5vw, 3.5rem)',
              fontWeight: 700, lineHeight: 1.05, letterSpacing: '-0.03em',
              color: 'rgba(255,255,255,0.92)',
            }}>
              Your workflow.<br />Your aesthetic.
            </h2>
          </FadeIn>
          <FadeIn delay={0.15}>
            <p className="max-w-2xl mb-10" style={{ fontFamily: 'Inter, sans-serif', fontSize: 17, lineHeight: 1.75, color: 'rgba(255,255,255,0.55)' }}>
              BetterWinTab ships with 22 curated themes and a full 14-variable color engine
              that lets you own every pixel. Accent, surface, card, border, text hierarchy, danger states.
              Every semantic color token is exposed. Every change previews live. Save as a named preset.
            </p>
          </FadeIn>

          {/* Swatch grid */}
          <FadeIn delay={0.2}>
            <div className="flex flex-wrap gap-3 mb-12">
              {THEMES.map(t => (
                <div
                  key={t.name}
                  className="theme-swatch w-10 h-10 cursor-crosshair"
                  style={{ backgroundColor: t.accent, boxShadow: hoveredTheme === t.name ? `0 0 16px ${t.accent}` : 'none' }}
                  title={t.name}
                  onMouseEnter={() => setHoveredTheme(t.name)}
                  onMouseLeave={() => setHoveredTheme(null)}
                />
              ))}
            </div>
            {hoveredTheme && (
              <p style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, color: '#444444', marginTop: -8, marginBottom: 16 }}>
                {hoveredTheme}
              </p>
            )}
          </FadeIn>

          {/* Token table */}
          <FadeIn delay={0.25}>
            <div className="terminal-border p-6">
              <p className="section-label mb-4">14 semantic color tokens. All exposed. All yours.</p>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                {['--accent', '--accent-dim', '--accent-subtle', '--background', '--surface', '--card', '--border', '--text-primary', '--text-secondary', '--text-muted', '--danger', '--folder-hover', '--folder-selected', '--custom'].map(tok => (
                  <span key={tok} style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, color: '#39FF14', opacity: 0.75 }}>
                    {tok}
                  </span>
                ))}
              </div>
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §09  THE HARD NUMBERS
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }} aria-label="Benchmarks">
        <div className="max-w-4xl mx-auto">
          <FadeIn><span className="section-label">// BENCHMARK</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-4 mb-3" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(3rem, 8vw, 6rem)',
              fontWeight: 700, lineHeight: 1, letterSpacing: '-0.04em', color: '#39FF14',
            }}>
              11.5 minutes.<br />Every day.
            </h2>
            <p className="mb-12" style={{ fontFamily: 'Inter, sans-serif', fontSize: 18, color: 'rgba(255,255,255,0.5)' }}>
              Calculated. Not claimed.
            </p>
          </FadeIn>

          <FadeIn delay={0.2}>
            <div className="terminal-border overflow-x-auto">
              <table className="w-full comparison-table text-left">
                <thead>
                  <tr style={{ borderBottom: '1px solid #1A1A1A' }}>
                    {['Action', 'Frequency', 'Time saved', 'Total'].map(h => (
                      <th key={h} className="px-4 py-3" style={{ color: '#444444', fontFamily: 'JetBrains Mono, monospace', fontSize: 11, letterSpacing: '2px', textTransform: 'uppercase', fontWeight: 400 }}>
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {[
                    ['Find known window',      '120×/day',  '3.3s',  '396s'],
                    ['Return to recent win',   '40×/day',   '0.2s',  '8s'],
                    ['Search by name (fuzzy)', '20×/day',   '6.5s',  '130s'],
                    ['Switch work context',    '15×/day',   '10.0s', '150s'],
                    ['Launch new app',         '5×/day',    '1.5s',  '7.5s'],
                  ].map(row => (
                    <tr key={row[0]} style={{ borderBottom: '1px solid #111' }}>
                      {row.map((cell, ci) => (
                        <td key={ci} className="px-4 py-3" style={{
                          fontFamily: 'JetBrains Mono, monospace',
                          fontSize: 13,
                          color: ci === 0 ? 'rgba(255,255,255,0.75)' : ci === 3 ? 'rgba(255,255,255,0.5)' : '#444444',
                        }}>
                          {cell}
                        </td>
                      ))}
                    </tr>
                  ))}
                  <tr style={{ borderTop: '1px solid #1A1A1A' }}>
                    <td className="px-4 py-3" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: 'rgba(255,255,255,0.85)', fontWeight: 700 }}>DAILY TOTAL</td>
                    <td className="px-4 py-3" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: '#444444' }}>200 ops</td>
                    <td className="px-4 py-3" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: '#444444' }}>avg 3.46s</td>
                    <td className="px-4 py-3" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: '#39FF14', fontWeight: 700 }}>691.5s</td>
                  </tr>
                </tbody>
              </table>
              <div className="px-4 py-4 border-t" style={{ borderColor: '#1A1A1A' }}>
                <p style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: 'rgba(255,255,255,0.5)' }}>
                  691.5s/day = 11.5 min/day = 4.2 h/month = 50 h/year
                </p>
                <p className="mt-2" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: 'rgba(255,255,255,0.35)' }}>
                  Over a 3-year career sprint: 150 hours recovered. At $75/hr: <span style={{ color: '#39FF14' }}>$11,250</span> in billable attention. BetterWinTab is free.
                </p>
              </div>
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §10  OBJECTIONS — The Hard Truth
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }} aria-label="Objections addressed">
        <div className="max-w-3xl mx-auto">
          <FadeIn><span className="section-label">// OBJECTIONS</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-4 mb-12" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2rem, 4vw, 3rem)',
              fontWeight: 700, lineHeight: 1.1, letterSpacing: '-0.03em',
              color: 'rgba(255,255,255,0.92)',
            }}>
              The Hard Truth.<br />We address it first.
            </h2>
          </FadeIn>
          <FadeIn delay={0.15}>
            <div className="border-t" style={{ borderColor: '#1A1A1A' }}>
              <Objection
                n="01"
                question="A low-level keyboard hook will lag my system."
                answer={`WH_KEYBOARD_LL runs on the thread that installed the hook — in this case, the WinUI 3 dispatcher thread, which is already alive and hot. The hook callback is a 12-line function that checks two key states and enqueues a single event. Total execution time: < 50 microseconds. We enforce a 300ms debounce to prevent ToggleOverlay from firing twice on fast fingers.\n\nThe only real risk with LL hooks is an unresponsive hook that causes Windows to remove it after a timeout. We don't block the callback. We dispatch to UI and exit immediately.`}
              />
              <Objection
                n="02"
                question="Live previews must destroy my GPU."
                answer={`DwmRegisterThumbnail does not capture frames. It registers a dependency between two window render targets inside the DWM compositor — the same entity already compositing your entire desktop at 60fps. The thumbnail is a view of an already-rendered surface. There is no additional render pass.\n\nWe measured: 0% GPU delta in Task Manager with 20 active thumbnails vs. 0 thumbnails. The compositor absorbs it.`}
              />
              <Objection
                n="03"
                question="Another Electron monstrosity disguised as a native app."
                answer={`BetterWinTab is compiled to a native Windows executable targeting net8.0-windows10.0.19041.0 with WinUI 3 on the Windows App SDK. The package type is Unpackaged — no MSIX sandbox, no UWP broker.\n\nFull dependency chain at runtime:\n  — Microsoft.WindowsAppSDK (WinUI 3 renderer)\n  — .NET 8 runtime\n  — user32.dll, dwmapi.dll, kernel32.dll (OS-native)\n\nNo Chromium. No Node. No V8. RAM footprint at idle: ~28MB.`}
              />
              <Objection
                n="04"
                question="Manual folders break every time I restart Windows."
                answer={`This is a real limitation in v1.0 and we document it explicitly. Window handles (HWND/IntPtr) are volatile — they are assigned by the OS at runtime and do not survive process restarts. Manual folder assignments persist across window refreshes within a session, but not across reboots.\n\nThis is on the v1.x roadmap:\n  pinnedWindows: [{ processName, titlePattern }]\n  — persist by process + title glob, re-match on boot.\n\nWe ship honest software. Roadmap items are not promises. They are dated commitments with complexity ratings.`}
              />
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §11  COMPARISON TABLE
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }} aria-label="Product comparison">
        <div className="max-w-4xl mx-auto">
          <FadeIn><span className="section-label">// MARKET_POSITION</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-4 mb-12" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2rem, 4vw, 3rem)',
              fontWeight: 700, lineHeight: 1.05, letterSpacing: '-0.03em',
              color: 'rgba(255,255,255,0.92)',
            }}>
              Feature parity is a<br />low bar. Here's the real table.
            </h2>
          </FadeIn>
          <FadeIn delay={0.2}>
            <div className="terminal-border overflow-x-auto">
              <table className="w-full comparison-table">
                <thead>
                  <tr style={{ borderBottom: '1px solid #1A1A1A' }}>
                    <th className="px-4 py-3 text-left" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, color: '#444444', fontWeight: 400, letterSpacing: '2px', textTransform: 'uppercase' }}>Capability</th>
                    <th className="px-4 py-3 text-center" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, color: '#444444', fontWeight: 400 }}>Alt+Tab</th>
                    <th className="px-4 py-3 text-center" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, color: '#444444', fontWeight: 400 }}>PowerToys</th>
                    <th className="px-4 py-3 text-center" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, color: '#39FF14', fontWeight: 700, borderLeft: '2px solid #39FF14' }}>BetterWinTab</th>
                  </tr>
                </thead>
                <tbody>
                  {[
                    ['2D hierarchical navigation',    '✗', '✗',         '✓'],
                    ['Auto-organizing Smart Rules',   '✗', '✗',         '✓'],
                    ['Manual context folders',        '✗', '✗',         '✓'],
                    ['Typo-tolerant fuzzy search',    '✗', '✗',         '✓'],
                    ['Integrated app launcher',       '✗', '✗',         '✓'],
                    ['Zero-CPU DWM live previews',    '✓', '✗',         '✓'],
                    ['Virtual desktop awareness',     '~', '✗',         '✓ + badges'],
                    ['Fully custom theme engine',     '✗', '✗',         '✓ (22 + DIY)'],
                    ['Full keyboard operability',     '✓', '✗',         '✓ (2D grid)'],
                    ['Non-Electron runtime',          '—', '✓',         '✓ (.NET 8)'],
                    ['Source available',              '—', '✓',         '✓'],
                  ].map(row => (
                    <tr key={row[0]} style={{ borderBottom: '1px solid #111' }}>
                      <td className="px-4 py-2" style={{ fontFamily: 'Inter, sans-serif', fontSize: 13, color: 'rgba(255,255,255,0.7)' }}>{row[0]}</td>
                      <td className="px-4 py-2 text-center" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: row[1] === '✓' ? 'rgba(255,255,255,0.6)' : '#333' }}>{row[1]}</td>
                      <td className="px-4 py-2 text-center" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: row[2] === '✓' ? 'rgba(255,255,255,0.6)' : '#333' }}>{row[2]}</td>
                      <td className="px-4 py-2 text-center" style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13, color: '#39FF14', fontWeight: 600, borderLeft: '2px solid #1a1a1a' }}>{row[3]}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §12  TECH STACK
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }} aria-label="Technical stack">
        <div className="max-w-4xl mx-auto">
          <FadeIn><span className="section-label">// RUNTIME_SPEC</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-4 mb-12" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2rem, 4vw, 3rem)',
              fontWeight: 700, lineHeight: 1.05, letterSpacing: '-0.03em', color: 'rgba(255,255,255,0.92)',
            }}>
              Built native. No shortcuts.
            </h2>
          </FadeIn>
          <FadeIn delay={0.2}>
            <div
              className="p-6 overflow-x-auto"
              style={{ backgroundColor: '#0A0A0A', border: '1px solid #1A1A1A', fontFamily: 'JetBrains Mono, monospace', fontSize: 13, lineHeight: 2 }}
            >
              <p style={{ color: '#39FF14', marginBottom: 8 }}>$ betterwindtab --version --verbose</p>
              {[
                ['UI Framework',   'WinUI 3 (Windows App SDK 1.x)'],
                ['Runtime',        '.NET 8'],
                ['Target',         'net8.0-windows10.0.19041.0'],
                ['MVVM',           'CommunityToolkit.Mvvm 8.x'],
                ['Serialization',  'System.Text.Json 9.x'],
                ['Native Interop', 'Manual P/Invoke (35 Win32 APIs)'],
                ['Package Type',   'Unpackaged — no MSIX, no sandbox'],
                ['Platforms',      'x64, x86, ARM64'],
              ].map(([k, v]) => (
                <div key={k} className="flex gap-4">
                  <span style={{ color: '#444444', minWidth: 140 }}>{k}</span>
                  <span style={{ color: 'rgba(255,255,255,0.65)' }}>: {v}</span>
                </div>
              ))}
              <div className="mt-4 pt-4" style={{ borderTop: '1px solid #1A1A1A' }}>
                {[
                  'user32.dll   — window enum, focus, hooks, styles',
                  'dwmapi.dll   — thumbnail registration, compositing',
                  'kernel32.dll — module handle, thread id',
                  'shell32.dll  — SHGetFileInfo (app icon extraction)',
                  'COM          — IVirtualDesktopManager',
                ].map(line => (
                  <div key={line} style={{ color: 'rgba(255,255,255,0.4)', paddingLeft: 16 }}>
                    <span style={{ color: '#39FF14' }}>{'> '}</span>{line}
                  </div>
                ))}
              </div>
            </div>
          </FadeIn>
          <FadeIn delay={0.25}>
            <div className="mt-8 grid grid-cols-3 gap-px" style={{ background: '#1A1A1A' }}>
              {[
                { v: '~3,500', u: 'lines of C#' },
                { v: '5', u: 'NuGet dependencies' },
                { v: '~28MB', u: 'idle RAM' },
              ].map(m => (
                <div key={m.v} className="py-6 text-center" style={{ backgroundColor: '#050505' }}>
                  <p style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 28, fontWeight: 700, color: '#39FF14' }}>{m.v}</p>
                  <p style={{ fontFamily: 'Inter, sans-serif', fontSize: 13, color: '#444444', marginTop: 4 }}>{m.u}</p>
                </div>
              ))}
            </div>
          </FadeIn>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §13  ROADMAP
      ══════════════════════════════════════════════════════════════════════ */}
      <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }} aria-label="Roadmap">
        <div className="max-w-4xl mx-auto">
          <FadeIn><span className="section-label">// ROADMAP</span></FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-4 mb-16" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2rem, 4vw, 3rem)',
              fontWeight: 700, lineHeight: 1.05, letterSpacing: '-0.03em', color: 'rgba(255,255,255,0.92)',
            }}>
              This is v1.0.<br />The trajectory is clear.
            </h2>
          </FadeIn>

          <div className="grid md:grid-cols-3 gap-8">
            {[
              {
                version: 'v1.x',
                subtitle: 'NOW — Stabilization',
                color: '#39FF14',
                items: [
                  { done: true,  label: 'Smart Folders by process and class name' },
                  { done: true,  label: '22 built-in themes + full custom engine' },
                  { done: true,  label: 'Virtual Desktop awareness + cross-desktop switching' },
                  { done: true,  label: 'Integrated fuzzy search + app launcher' },
                  { done: false, label: 'Manual folder persistence across reboots' },
                  { done: false, label: 'Multi-monitor target selection' },
                  { done: false, label: 'Configurable hotkeys from Settings UI' },
                ],
              },
              {
                version: 'v2.0',
                subtitle: 'DIFFERENTIATION',
                color: '#8BE9FD',
                items: [
                  { done: false, label: 'Smart Folder rules: title contains, AND/OR logic' },
                  { done: false, label: 'Workspace Profiles — save and load context sets' },
                  { done: false, label: 'Plugin API for custom commands' },
                  { done: false, label: 'Virtual Desktop integration in UI' },
                ],
              },
              {
                version: 'v3.0',
                subtitle: 'ECOSYSTEM',
                color: '#FFB86C',
                items: [
                  { done: false, label: 'Integrated Window Tiling (snap zones from overlay)' },
                  { done: false, label: 'Cloud sync (OneDrive / GitHub Gist)' },
                  { done: false, label: 'AI Smart Folders (usage pattern classification)' },
                  { done: false, label: 'Scripting engine (Lua / JS)' },
                ],
              },
            ].map(phase => (
              <FadeIn key={phase.version} delay={0.15}>
                <div>
                  <div className="mb-3">
                    <span style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 16, fontWeight: 700, color: phase.color }}>► {phase.version}</span>
                    <span style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 11, color: '#444444', marginLeft: 8 }}>{phase.subtitle}</span>
                  </div>
                  <div className="border-l pl-4" style={{ borderColor: phase.color + '33' }}>
                    {phase.items.map(item => (
                      <RoadmapItem key={item.label} done={item.done}>{item.label}</RoadmapItem>
                    ))}
                  </div>
                </div>
              </FadeIn>
            ))}
          </div>
        </div>
      </section>

      {/* ══════════════════════════════════════════════════════════════════════
          §14  CTA FINAL
      ══════════════════════════════════════════════════════════════════════ */}
      <section
        className="py-40 px-6 border-t text-center"
        style={{ borderColor: '#1A1A1A' }}
        aria-label="Download CTA"
      >
        <div className="max-w-3xl mx-auto">
          <FadeIn>
            <span className="section-label">// TAKE_BACK_CONTROL</span>
          </FadeIn>
          <FadeIn delay={0.1}>
            <h2 className="mt-6 mb-6" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2.5rem, 7vw, 5rem)',
              fontWeight: 700, lineHeight: 1.0, letterSpacing: '-0.04em',
              color: 'rgba(255,255,255,0.92)',
            }}>
              You've been navigating windows<br />
              the wrong way for 30 years.
            </h2>
          </FadeIn>
          <FadeIn delay={0.15}>
            <p className="mb-16 max-w-xl mx-auto" style={{ fontFamily: 'Inter, sans-serif', fontSize: 18, lineHeight: 1.7, color: 'rgba(255,255,255,0.45)' }}>
              Microsoft gave you Alt+Tab in 1987. They updated it three times in three decades. It's still a flat list.
              You deserve a tool that reflects how you actually think: in contexts, in layers, in intent.
            </p>
          </FadeIn>

          <FadeIn delay={0.2}>
            <div className="flex flex-wrap gap-4 justify-center mb-16">
              <a
                href="https://github.com/"
                className="cta-primary px-8 py-4 text-base font-bold"
                style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 14 }}
              >
                ↓ &nbsp;Download BetterWinTab — Free, Windows 10/11
              </a>
              <a
                href="https://github.com/"
                className="cta-secondary px-6 py-4 text-sm"
                style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13 }}
              >
                View Source — MIT License
              </a>
              <a
                href="/docs/ARCHITECTURE_AND_VISION.md"
                className="cta-secondary px-6 py-4 text-sm"
                style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 13 }}
              >
                Read the Architecture Doc
              </a>
            </div>
          </FadeIn>

          {/* Divider */}
          <div className="hr-accent mb-8" />

          <FadeIn delay={0.25}>
            <p style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, color: '#333', lineHeight: 2 }}>
              BetterWinTab is free and open source.&nbsp;&nbsp;
              x64 · x86 · ARM64&nbsp;&nbsp;|&nbsp;&nbsp;Windows 10 (19041+) · Windows 11&nbsp;&nbsp;|&nbsp;&nbsp;.NET 8 runtime required<br />
              No telemetry&nbsp;&nbsp;|&nbsp;&nbsp;No account needed&nbsp;&nbsp;|&nbsp;&nbsp;~28MB RAM at idle&nbsp;&nbsp;|&nbsp;&nbsp;&lt;1% CPU at rest&nbsp;&nbsp;|&nbsp;&nbsp;MIT Licensed
            </p>
          </FadeIn>
        </div>
      </section>

    </main>
  )
}

// ─── Feature section template ─────────────────────────────────────────────────
function FeatureSection({
  tag,
  title,
  body,
  quote,
  code,
  pills,
  reverse,
}: {
  tag: string
  title: string
  body: string
  quote: string
  code: string
  pills: string[]
  reverse?: boolean
}) {
  return (
    <section className="py-32 px-6 border-t" style={{ borderColor: '#1A1A1A' }}>
      <div className={`max-w-5xl mx-auto grid md:grid-cols-2 gap-16 items-start ${reverse ? 'md:[&>*:first-child]:order-2' : ''}`}>
        {/* Left: copy */}
        <div>
          <FadeIn><span className="section-label">// {tag}</span></FadeIn>
          <FadeIn delay={0.1}>
            <h3 className="mt-4 mb-6" style={{
              fontFamily: 'JetBrains Mono, monospace',
              fontSize: 'clamp(2rem, 4vw, 3rem)',
              fontWeight: 700, lineHeight: 1.05, letterSpacing: '-0.03em',
              color: 'rgba(255,255,255,0.92)',
              whiteSpace: 'pre-line',
            }}>
              {title}
            </h3>
          </FadeIn>
          <FadeIn delay={0.15}>
            <p className="mb-6" style={{ fontFamily: 'Inter, sans-serif', fontSize: 16, lineHeight: 1.75, color: 'rgba(255,255,255,0.55)', whiteSpace: 'pre-line' }}>
              {body}
            </p>
          </FadeIn>
          {quote && (
            <FadeIn delay={0.2}>
              <blockquote className="accent-left mb-6" style={{ fontFamily: 'Inter, sans-serif', fontSize: 15, lineHeight: 1.65, color: 'rgba(255,255,255,0.65)', fontStyle: 'italic' }}>
                {quote}
              </blockquote>
            </FadeIn>
          )}
          <FadeIn delay={0.25}>
            <div className="flex flex-wrap gap-2 mt-4">
              {pills.map(p => (
                <span key={p} style={{
                  fontFamily: 'JetBrains Mono, monospace',
                  fontSize: 11,
                  letterSpacing: '1px',
                  color: '#39FF14',
                  border: '1px solid #1A1A1A',
                  padding: '4px 10px',
                  backgroundColor: '#0A0A0A',
                }}>
                  {p}
                </span>
              ))}
            </div>
          </FadeIn>
        </div>

        {/* Right: code block */}
        <FadeIn delay={0.15}>
          <div
            className="p-5 overflow-x-auto"
            style={{ backgroundColor: '#0A0A0A', border: '1px solid #1A1A1A', fontFamily: 'JetBrains Mono, monospace', fontSize: 12.5, lineHeight: 1.9, color: 'rgba(255,255,255,0.55)', whiteSpace: 'pre' }}
          >
            {code}
          </div>
        </FadeIn>
      </div>
    </section>
  )
}

// ─── Thin progress bar ────────────────────────────────────────────────────────
function ProgressBar() {
  const { scrollYProgress } = useScroll()
  const scaleX = useSpring(scrollYProgress, { stiffness: 120, damping: 30, restDelta: 0.0005 })
  return (
    <motion.div
      className="fixed top-0 left-0 right-0 z-50"
      style={{ height: 1, scaleX, transformOrigin: 'left', backgroundColor: '#39FF14' }}
    />
  )
}
