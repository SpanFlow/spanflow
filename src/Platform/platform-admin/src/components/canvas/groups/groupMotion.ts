import type { GroupStyle } from './nodeGroups'

/**
 * How a group opens and closes (E61), as data: a list of tracks, each moving one thing from one frame to
 * another after a delay. Pure, so every style is testable without a browser; `useGroupMotion` plays them.
 * Only transform and opacity ever change (D61.7).
 */

/** A drawn thing's state. `dx`/`dy` are offsets from where it rests, so a canvas never loses positions. */
export interface MotionFrame {
  dx: number
  dy: number
  scale: number
  opacity: number
  rotate: number
}

export type MotionTarget = { kind: 'node'; id: string } | { kind: 'card' } | { kind: 'boundary' } | { kind: 'ripple' }
export type MotionEasing = 'out' | 'back'

export interface MotionTrack {
  target: MotionTarget
  from: MotionFrame
  to: MotionFrame
  delay: number
  duration: number
  easing: MotionEasing
}

export interface MotionPlan {
  tracks: MotionTrack[]
  /** When the last track ends, in ms. */
  total: number
}

/** A member as an animation sees it: how far its centre is from the card's, and its step in the flow. */
export interface MotionMember {
  id: string
  toCard: { dx: number; dy: number }
  rank: number
}

export const REST: MotionFrame = { dx: 0, dy: 0, scale: 1, opacity: 1, rotate: 0 }
const HIDDEN: MotionFrame = { ...REST, opacity: 0 }

export const easings: Record<MotionEasing, (t: number) => number> = {
  out: (t) => 1 - Math.pow(1 - t, 3),
  // A small overshoot: the petal passes its place and settles back.
  back: (t) => {
    const c = 1.6
    return 1 + (c + 1) * Math.pow(t - 1, 3) + c * Math.pow(t - 1, 2)
  },
}

export function frameAt(track: MotionTrack, elapsed: number): MotionFrame {
  if (elapsed <= track.delay) return track.from
  const u = track.duration <= 0 ? 1 : Math.min(1, (elapsed - track.delay) / track.duration)
  const f = easings[track.easing](u)
  const lerp = (a: number, b: number) => a + (b - a) * f
  return {
    dx: lerp(track.from.dx, track.to.dx),
    dy: lerp(track.from.dy, track.to.dy),
    scale: lerp(track.from.scale, track.to.scale),
    opacity: lerp(track.from.opacity, track.to.opacity),
    rotate: lerp(track.from.rotate, track.to.rotate),
  }
}

export const targetKey = (t: MotionTarget) => (t.kind === 'node' ? `node:${t.id}` : t.kind)

/**
 * Every target's frame at `elapsed` ms. A target with several tracks (the card's pulse, then its exit) is
 * on the latest one that has started; before any has, it shows the first one's starting frame.
 */
export function framesAt(plan: MotionPlan, elapsed: number): Map<string, MotionFrame> {
  const byTarget = new Map<string, MotionTrack[]>()
  for (const t of plan.tracks) {
    const key = targetKey(t.target)
    byTarget.set(key, [...(byTarget.get(key) ?? []), t])
  }
  const out = new Map<string, MotionFrame>()
  for (const [key, list] of byTarget) {
    const sorted = [...list].sort((a, b) => a.delay - b.delay)
    const started = sorted.filter((t) => t.delay <= elapsed)
    const current = started.length ? started[started.length - 1] : sorted[0]
    out.set(key, frameAt(current, elapsed))
  }
  return out
}

/** Where a member sits while folded: on the card's centre, small and invisible, turned like a closed petal. */
function folded(m: MotionMember, index: number, open: GroupStyle['open']): MotionFrame {
  return { dx: m.toCard.dx, dy: m.toCard.dy, scale: 0.25, opacity: 0, rotate: open === 'spiral' ? -(index * 90 + 45) : 0 }
}

function finish(tracks: MotionTrack[], speed: number, reducedMotion: boolean): MotionPlan {
  const k = reducedMotion ? 0 : 1 / speed
  const scaled = tracks.map((t) => ({ ...t, delay: t.delay * k, duration: t.duration * k }))
  return { tracks: scaled, total: Math.max(0, ...scaled.map((t) => t.delay + t.duration)) }
}

const track = (target: MotionTarget, from: MotionFrame, to: MotionFrame, delay: number, duration: number, easing: MotionEasing = 'out'): MotionTrack =>
  ({ target, from, to, delay, duration, easing })

/** The card pulses and gives way, then the members bloom out in the group's open style. */
export function openPlan(style: GroupStyle, members: readonly MotionMember[], reducedMotion = false): MotionPlan {
  const t: MotionTrack[] = [
    track({ kind: 'card' }, REST, { ...REST, scale: 1.08 }, 0, 110),
    track({ kind: 'card' }, { ...REST, scale: 1.08 }, { ...REST, scale: 0.4, opacity: 0 }, 110, 240),
  ]
  if (style.open === 'unfold') {
    t.push(track({ kind: 'boundary' }, { ...HIDDEN, scale: 0.5 }, REST, 80, 480))
    members.forEach((m, i) => t.push(track({ kind: 'node', id: m.id }, { ...HIDDEN, scale: 0.85 }, REST, 420 + i * 80, 360)))
  } else {
    const burst = style.open === 'burst'
    t.push(track({ kind: 'ripple' }, { ...REST, opacity: 0.9 }, { ...REST, scale: burst ? 2.8 : 2.2, opacity: 0 }, 110, 600))
    members.forEach((m, i) => {
      const delay = style.open === 'spiral' ? 200 + i * 110 : burst ? 200 : 200 + m.rank * 260
      t.push(track({ kind: 'node', id: m.id }, folded(m, i, style.open), REST, delay, burst ? 520 : 640, style.open === 'fan' ? 'out' : 'back'))
    })
    t.push(track({ kind: 'boundary' }, { ...HIDDEN, scale: 0.7 }, REST, burst ? 300 : 420, 520, 'back'))
  }
  return finish(t, style.speed, reducedMotion)
}

/** The reverse: members gather into the card (or fade where they are, for "unfold"), then the card returns. */
export function closePlan(style: GroupStyle, members: readonly MotionMember[], reducedMotion = false): MotionPlan {
  const t: MotionTrack[] = []
  if (style.open === 'unfold') {
    members.forEach((m, i) => t.push(track({ kind: 'node', id: m.id }, REST, { ...HIDDEN, scale: 0.8 }, i * 40, 260)))
    t.push(track({ kind: 'boundary' }, REST, { ...HIDDEN, scale: 0.5 }, 200, 360))
    t.push(track({ kind: 'card' }, { ...HIDDEN, scale: 1 }, REST, 380, 320))
  } else {
    const last = Math.max(0, ...members.map((m) => m.rank))
    members.forEach((m, i) => {
      const delay = style.open === 'burst' ? 0 : style.open === 'fan' ? (last - m.rank) * 140 : i * 60
      t.push(track({ kind: 'node', id: m.id }, REST, folded(m, i, style.open), delay, 440))
    })
    t.push(track({ kind: 'boundary' }, REST, { ...HIDDEN, scale: 0.7 }, 0, 320))
    t.push(track({ kind: 'card' }, { ...HIDDEN, scale: 0 }, REST, 280, 420, 'back'))
  }
  return finish(t, style.speed, reducedMotion)
}

/** True when the person asked their system for less motion (D61.7). */
export function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
