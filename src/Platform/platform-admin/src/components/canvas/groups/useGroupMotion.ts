import { useCallback, useEffect, useRef, useState } from 'react'
import { framesAt, type MotionFrame, type MotionPlan } from './groupMotion'

interface Playing {
  plan: MotionPlan
  start: number
  onDone?: () => void
}

/**
 * Plays group motion plans, one per group at a time; several groups animate at once (D61.7). While a group
 * plays, `frame(groupId, key)` gives the frame to draw — `key` is `node:<id>`, `card`, `boundary` or
 * `ripple`; when nothing is playing it is `undefined` and the canvas draws its resting state. Starting a
 * group again replaces what it was playing.
 */
export function useGroupMotion() {
  const playing = useRef(new Map<string, Playing>())
  const raf = useRef<number | null>(null)
  const [frames, setFrames] = useState<ReadonlyMap<string, ReadonlyMap<string, MotionFrame>>>(new Map())

  const step = useCallback(function tick(now: number) {
    const next = new Map<string, ReadonlyMap<string, MotionFrame>>()
    const done: Array<() => void> = []
    for (const [groupId, p] of playing.current) {
      const elapsed = now - p.start
      if (elapsed >= p.plan.total) {
        playing.current.delete(groupId)
        if (p.onDone) done.push(p.onDone)
      } else {
        next.set(groupId, framesAt(p.plan, elapsed))
      }
    }
    setFrames(next)
    done.forEach((f) => f())
    raf.current = playing.current.size ? requestAnimationFrame(tick) : null
  }, [])

  const play = useCallback((groupId: string, plan: MotionPlan, onDone?: () => void) => {
    if (plan.total <= 0) {
      playing.current.delete(groupId)
      setFrames((prev) => {
        const next = new Map(prev)
        next.delete(groupId)
        return next
      })
      onDone?.()
      return
    }
    const start = performance.now()
    playing.current.set(groupId, { plan, start, onDone })
    // Draw the first frame now, so nothing flashes at its resting place before the first tick.
    setFrames((prev) => new Map(prev).set(groupId, framesAt(plan, 0)))
    if (raf.current === null) raf.current = requestAnimationFrame(step)
  }, [step])

  useEffect(() => () => {
    if (raf.current !== null) cancelAnimationFrame(raf.current)
  }, [])

  const frame = useCallback((groupId: string, key: string): MotionFrame | undefined => frames.get(groupId)?.get(key), [frames])
  const isPlaying = useCallback((groupId: string) => frames.has(groupId), [frames])

  return { play, frame, isPlaying }
}

/** A frame as a CSS transform + opacity, for whatever element draws the target. */
export function frameStyle(f: MotionFrame | undefined): { transform?: string; opacity?: number } {
  if (!f) return {}
  return { transform: `translate(${f.dx}px, ${f.dy}px) rotate(${f.rotate}deg) scale(${f.scale})`, opacity: f.opacity }
}
