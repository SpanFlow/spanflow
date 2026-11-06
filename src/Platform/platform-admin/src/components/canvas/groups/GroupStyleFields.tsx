import {
  BOUNDARY_STYLES,
  CARD_STYLES,
  GROUP_SPEED,
  OPEN_STYLES,
  STYLE_LABELS,
  type GroupStyle,
} from './nodeGroups'

const field = 'flex flex-col gap-1 text-[11px] text-slate-500 dark:text-slate-400'
const select = 'rounded border border-slate-300 bg-white px-1.5 py-1 text-xs text-slate-800 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100'

/**
 * The four choices a group's look is made of (E61-T6): how it opens, its card, its boundary and its speed.
 * Shared by a group's Style popover and the "Default look for new groups" profile setting, so the two can
 * never offer different things. It shows `value` (what is in effect) and reports one changed key at a time.
 */
export default function GroupStyleFields({
  value,
  onChange,
  idPrefix,
}: {
  value: GroupStyle
  onChange: (patch: Partial<GroupStyle>) => void
  /** Keeps label/input ids unique when two of these are on one page. */
  idPrefix: string
}) {
  return (
    <div className="flex flex-col gap-2">
      <label className={field} htmlFor={`${idPrefix}-open`}>
        Opens with
        <select id={`${idPrefix}-open`} className={select} value={value.open}
          onChange={(e) => onChange({ open: e.target.value as GroupStyle['open'] })}>
          {OPEN_STYLES.map((s) => <option key={s} value={s}>{STYLE_LABELS.open[s]}</option>)}
        </select>
      </label>
      <label className={field} htmlFor={`${idPrefix}-card`}>
        Card when folded
        <select id={`${idPrefix}-card`} className={select} value={value.card}
          onChange={(e) => onChange({ card: e.target.value as GroupStyle['card'] })}>
          {CARD_STYLES.map((s) => <option key={s} value={s}>{STYLE_LABELS.card[s]}</option>)}
        </select>
      </label>
      <label className={field} htmlFor={`${idPrefix}-boundary`}>
        Boundary when open
        <select id={`${idPrefix}-boundary`} className={select} value={value.boundary}
          onChange={(e) => onChange({ boundary: e.target.value as GroupStyle['boundary'] })}>
          {BOUNDARY_STYLES.map((s) => <option key={s} value={s}>{STYLE_LABELS.boundary[s]}</option>)}
        </select>
      </label>
      <label className={field} htmlFor={`${idPrefix}-speed`}>
        <span className="flex justify-between">
          Speed <span className="tabular-nums text-slate-700 dark:text-slate-200">{value.speed}×</span>
        </span>
        <input id={`${idPrefix}-speed`} type="range" min={GROUP_SPEED.min} max={GROUP_SPEED.max} step={GROUP_SPEED.step}
          value={value.speed} onChange={(e) => onChange({ speed: Number(e.target.value) })} />
      </label>
    </div>
  )
}
