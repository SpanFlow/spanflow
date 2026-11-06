import { describe, expect, it } from 'vitest'
import { closePlan, framesAt, openPlan, REST, type MotionMember } from '../groupMotion'
import { BUILT_IN_GROUP_STYLE, OPEN_STYLES, type GroupStyle } from '../nodeGroups'

const members: MotionMember[] = [
  { id: 'a', toCard: { dx: 50, dy: 80 }, rank: 0 },
  { id: 'b', toCard: { dx: 50, dy: -80 }, rank: 1 },
]
const style = (over: Partial<GroupStyle> = {}): GroupStyle => ({ ...BUILT_IN_GROUP_STYLE, ...over })
const nodeTracks = (plan: ReturnType<typeof openPlan>) => plan.tracks.filter((t) => t.target.kind === 'node')

describe('open and close plans', () => {
  it.each(OPEN_STYLES)('%s opens every member onto its resting place and ends with the boundary shown', (open) => {
    const plan = openPlan(style({ open }), members)
    const end = framesAt(plan, plan.total)
    expect(end.get('node:a')).toEqual(REST)
    expect(end.get('node:b')).toEqual(REST)
    expect(end.get('boundary')).toEqual(REST)
    expect(end.get('card')?.opacity).toBe(0)
  })

  it.each(OPEN_STYLES)('%s closes back into a visible card', (open) => {
    const plan = closePlan(style({ open }), members)
    const end = framesAt(plan, plan.total)
    expect(end.get('card')).toEqual(REST)
    expect(end.get('boundary')?.opacity).toBe(0)
    expect(end.get('node:a')?.opacity).toBe(0)
  })

  it('the styles really differ: spiral turns and staggers, burst opens all at once, fan follows the flow', () => {
    const spiral = nodeTracks(openPlan(style({ open: 'spiral' }), members))
    expect(spiral.map((t) => t.from.rotate)).toEqual([-45, -135])
    expect(spiral[1].delay).toBeGreaterThan(spiral[0].delay)

    const burst = nodeTracks(openPlan(style({ open: 'burst' }), members))
    expect(new Set(burst.map((t) => t.delay)).size).toBe(1)
    expect(burst.every((t) => t.from.rotate === 0)).toBe(true)

    const fan = nodeTracks(openPlan(style({ open: 'fan' }), [{ ...members[0], rank: 1 }, { ...members[1], rank: 0 }]))
    expect(fan[1].delay).toBeLessThan(fan[0].delay)

    const unfold = nodeTracks(openPlan(style({ open: 'unfold' }), members))
    expect(unfold.every((t) => t.from.dx === 0 && t.from.dy === 0)).toBe(true)
  })

  it('starts folded members on the card\'s centre', () => {
    const plan = openPlan(style(), members)
    const start = framesAt(plan, 0).get('node:a')
    expect(start).toMatchObject({ dx: 50, dy: 80, opacity: 0 })
  })

  it('speed divides every delay and duration', () => {
    const normal = openPlan(style({ speed: 1 }), members)
    const double = openPlan(style({ speed: 2 }), members)
    expect(double.total).toBeCloseTo(normal.total / 2)
  })

  it('reduced motion makes it instant at any speed', () => {
    expect(openPlan(style({ speed: 0.25 }), members, true).total).toBe(0)
    expect(closePlan(style(), members, true).total).toBe(0)
  })

  it('a target with two tracks shows the one that has started', () => {
    const plan = openPlan(style(), members)
    expect(framesAt(plan, 50).get('card')?.scale).toBeGreaterThan(1)
    expect(framesAt(plan, 300).get('card')?.scale).toBeLessThan(1)
  })
})
