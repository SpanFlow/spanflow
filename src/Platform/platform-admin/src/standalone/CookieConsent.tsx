import { useEffect, useState } from 'react'

/** Where the visitor's answer is kept: "all" or "essential". The website and the libraries playground use the same key. */
export const CONSENT_KEY = 'sf-consent'

function readConsent(): string | null {
  try {
    return localStorage.getItem(CONSENT_KEY)
  } catch {
    return null // private window
  }
}

/** Opens the notice again from anywhere on the page (the "Cookie settings" button). */
export function openCookieSettings() {
  window.dispatchEvent(new Event('sf-consent-open'))
}

/**
 * The cookie and storage notice of the standalone mapping page: shown until the visitor picks, then only when
 * "Cookie settings" opens it again.
 */
export default function CookieConsent() {
  const [open, setOpen] = useState(() => {
    const saved = readConsent()
    return saved !== 'all' && saved !== 'essential'
  })

  useEffect(() => {
    const show = () => setOpen(true)
    window.addEventListener('sf-consent-open', show)
    return () => window.removeEventListener('sf-consent-open', show)
  }, [])

  if (!open) return null

  const choose = (choice: 'all' | 'essential') => {
    try {
      localStorage.setItem(CONSENT_KEY, choice)
    } catch {
      /* private window: the notice comes back next visit */
    }
    setOpen(false)
  }

  return (
    <div
      role="dialog"
      aria-modal="false"
      aria-labelledby="consent-title"
      className="fixed z-50 inset-x-4 bottom-4 mx-auto max-w-lg rounded-xl border border-slate-200 bg-white p-4 shadow-2xl dark:border-slate-700 dark:bg-slate-900"
    >
      <h2 id="consent-title" className="text-sm font-semibold text-slate-800 dark:text-slate-100">
        Cookies and site storage
      </h2>
      <p className="mt-1.5 text-[13px] leading-relaxed text-slate-600 dark:text-slate-400">
        This page keeps your mappings and settings in your browser so they are here next time. No ads, and nothing
        that follows you to other sites.
      </p>
      <div className="mt-3 flex flex-wrap justify-end gap-2">
        <button
          type="button"
          onClick={() => choose('essential')}
          className="flex-1 sm:flex-none rounded-lg border border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-700 hover:border-slate-400 dark:border-slate-700 dark:text-slate-200"
        >
          Essential only
        </button>
        <button
          type="button"
          onClick={() => choose('all')}
          className="flex-1 sm:flex-none rounded-lg bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
        >
          Accept all
        </button>
      </div>
    </div>
  )
}
