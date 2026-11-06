import { useState } from 'react'
import { Activity } from 'lucide-react'
import { splitDetails } from '../lib/apiError'
import { useShellStore } from '../lib/orgSwitcher'

/**
 * An error, said plainly. A message built with technical detail (E66, `withDetails`) shows its plain
 * sentence first and keeps the detail behind "Details" for whoever fixes it; one about a service not
 * answering also offers "Check status", which opens the same Service status window as the top bar.
 */
export default function ErrorBanner({ message }: { message: string | null }) {
  const [open, setOpen] = useState(false)
  const show = useShellStore((s) => s.show)
  if (!message) return null
  const { message: text, details, offerStatus } = splitDetails(message)

  return (
    <div className="rounded border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
      <p>{text}</p>
      {(offerStatus || details) && (
        <div className="mt-1.5 flex flex-wrap items-center gap-3 text-xs">
          {offerStatus && (
            <button type="button" onClick={() => show('status')}
              className="flex items-center gap-1 rounded border border-red-300 px-2 py-0.5 font-medium hover:bg-red-100 dark:border-red-800 dark:hover:bg-red-900">
              <Activity size={12} aria-hidden="true" /> Check status
            </button>
          )}
          {details && (
            <button type="button" onClick={() => setOpen((o) => !o)} aria-expanded={open} className="underline underline-offset-2">
              {open ? 'Hide details' : 'Details'}
            </button>
          )}
        </div>
      )}
      {details && open && <p className="mt-1.5 text-xs opacity-90">{details}</p>}
    </div>
  )
}
