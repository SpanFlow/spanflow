import { studioName } from './brand'

/**
 * What sign-in says when the request got no answer at all. The page itself is plainly running — the reader is
 * looking at it — so the old "is SFStudio running?" pointed at the one thing that wasn't wrong. What failed is
 * the API behind it: stopped, restarting (a redeploy takes a few seconds), or at a different address.
 */
export function signInUnreachable(): string {
  const url = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5041'
  return `Couldn't reach the ${studioName} API at ${url}: it may be restarting. Wait a few seconds and try again; ` +
    `if it keeps happening, the API is stopped or at a different address.`
}
