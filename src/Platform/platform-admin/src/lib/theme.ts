// Light / dark / follow the device, for the whole studio. One switch: the `dark` class on <html>, which
// Tailwind's `dark:` variant and every dark rule in index.css / brand.css read (see `@custom-variant dark`).
// The choice lives in this browser (like the website's toggle); public/theme-init.js applies it before the
// first paint so a dark choice never flashes white.
import { create } from 'zustand'

export type ThemeChoice = 'system' | 'light' | 'dark'

/** Same key public/theme-init.js reads. */
export const THEME_KEY = 'studio-theme'

const media = () =>
  typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null

export function readChoice(): ThemeChoice {
  try {
    const saved = localStorage.getItem(THEME_KEY)
    return saved === 'light' || saved === 'dark' ? saved : 'system'
  } catch {
    return 'system'
  }
}

/** Whether a choice comes out dark on this device right now. */
export function resolveDark(choice: ThemeChoice): boolean {
  return choice === 'dark' || (choice === 'system' && !!media()?.matches)
}

function apply(dark: boolean) {
  document.documentElement.classList.toggle('dark', dark)
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
}

interface ThemeState {
  choice: ThemeChoice
  dark: boolean
  setChoice: (choice: ThemeChoice) => void
}

export const useThemeStore = create<ThemeState>((set) => ({
  choice: readChoice(),
  dark: resolveDark(readChoice()),
  setChoice: (choice) => {
    try {
      if (choice === 'system') localStorage.removeItem(THEME_KEY)
      else localStorage.setItem(THEME_KEY, choice)
    } catch {
      // Storage refused (private window): the choice still holds for this page.
    }
    const dark = resolveDark(choice)
    apply(dark)
    set({ choice, dark })
  },
}))

/** Light → dark → system → light: what the top-bar button does on each click. */
export function nextChoice(choice: ThemeChoice): ThemeChoice {
  return choice === 'light' ? 'dark' : choice === 'dark' ? 'system' : 'light'
}

let started = false

/**
 * Applies the saved choice and keeps it current: a device switching between light and dark while "System" is
 * chosen, and a choice made in another tab or in the split pane's frame (the `storage` event).
 */
export function startTheme() {
  if (started || typeof document === 'undefined') return
  started = true
  const refresh = () => {
    const choice = readChoice()
    const dark = resolveDark(choice)
    apply(dark)
    useThemeStore.setState({ choice, dark })
  }
  refresh()
  media()?.addEventListener?.('change', refresh)
  window.addEventListener('storage', (e) => { if (e.key === THEME_KEY || e.key === null) refresh() })
}
