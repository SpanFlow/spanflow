/**
 * Copying that works wherever the console runs (E53-T4).
 *
 * `navigator.clipboard` is not always there or willing: a plain-http deployment has no Clipboard API at all,
 * an embedding browser may refuse it outright, and Safari/Firefox refuse it once a click has waited on the
 * network. `execCommand('copy')` on a hidden textarea still works in every one of those, provided it runs
 * inside the click — so callers copy text they already have, and text that must be fetched goes through
 * {@link copyLater}.
 */

function copyWithTextarea(text: string): boolean {
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  // Off-screen but still selectable; a display:none element cannot hold a selection.
  area.style.cssText = 'position:fixed;top:0;left:0;width:1px;height:1px;opacity:0;pointer-events:none'
  document.body.appendChild(area)
  const focused = document.activeElement as HTMLElement | null
  area.select()
  let ok = false
  try {
    ok = document.execCommand('copy')
  } catch {
    ok = false
  }
  area.remove()
  focused?.focus?.()
  return ok
}

/** Puts `text` on the clipboard; rejects only when every way failed. */
export async function copyText(text: string): Promise<void> {
  // A refusal comes back at once, well inside the click's few seconds, so the fallback still counts as
  // part of it.
  if (!navigator.clipboard?.writeText) {
    if (copyWithTextarea(text)) return
    throw new Error('Copying is not available in this browser.')
  }
  try {
    await navigator.clipboard.writeText(text)
  } catch (err) {
    if (copyWithTextarea(text)) return
    throw err
  }
}

/**
 * Copies text that is still being fetched. The browser is given a `ClipboardItem` whose content is the
 * promise, which Chrome and Safari accept as part of the click; elsewhere the text is awaited and copied
 * the ordinary way. Resolves to the text, so the caller can show it if copying failed.
 */
export async function copyLater(text: Promise<string>): Promise<string> {
  const Item = (globalThis as { ClipboardItem?: typeof ClipboardItem }).ClipboardItem
  if (Item && navigator.clipboard?.write) {
    try {
      await navigator.clipboard.write([new Item({ 'text/plain': text.then((t) => new Blob([t], { type: 'text/plain' })) })])
      return await text
    } catch {
      // Fall through: some browsers have ClipboardItem but not promise contents, or deny write().
    }
  }
  const value = await text
  await copyText(value)
  return value
}
