import { Monitor, Moon, Sun } from 'lucide-react'
import { nextChoice, useThemeStore, type ThemeChoice } from '../lib/theme'

export const THEME_LABELS: Record<ThemeChoice, string> = { light: 'Light', dark: 'Dark', system: 'System' }

const ICONS = { light: Sun, dark: Moon, system: Monitor }

/** The top bar's light / dark switch: each click moves Light → Dark → System (follow this device). */
export default function ThemeButton({ className }: { className: string }) {
  const { choice, setChoice } = useThemeStore()
  const Icon = ICONS[choice]
  const next = nextChoice(choice)
  return (
    <button
      type="button"
      onClick={() => setChoice(next)}
      className={className}
      aria-label={`Theme: ${THEME_LABELS[choice]}. Switch to ${THEME_LABELS[next]}`}
      title={`Theme: ${THEME_LABELS[choice]}${choice === 'system' ? ' (follows this device)' : ''}: click for ${THEME_LABELS[next]}`}
    >
      <Icon size={15} /> <span className="hidden 2xl:inline">{THEME_LABELS[choice]}</span>
    </button>
  )
}
