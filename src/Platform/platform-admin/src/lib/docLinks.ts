/**
 * The two guides the Help drawer serves (E23-T10). Each service embeds and serves its own
 * `docs/guide/`: DataFlow documents pipelines, nodes and connections; Platform documents the admin
 * surfaces (members, roles, keys, analytics, logs).
 */
export type DocSource = 'dataflow' | 'platform'

/**
 * The drawer addresses a page by **id**, not by slug, because the two guides both have a `README`
 * and could both grow a `concepts/features`. A Platform page is prefixed (`platform:admin/roles`);
 * a DataFlow page is its bare slug, so every `LearnMore` written before the second source existed
 * still points where it did.
 */
export function docId(source: DocSource, slug: string): string {
  return source === 'platform' ? `platform:${slug}` : slug
}

export function parseDocId(id: string): { source: DocSource; slug: string } {
  return id.startsWith('platform:')
    ? { source: 'platform', slug: id.slice('platform:'.length) }
    : { source: 'dataflow', slug: id }
}

/**
 * Resolves a markdown link found inside a guide page to another page's id, so the Help drawer can
 * navigate between guides. Returns null for external links (http/https/mailto) or non-`.md` targets —
 * the caller opens those in a new tab instead.
 *
 * Slugs are repo-relative paths without the extension (e.g. `concepts/nodes`). A link like
 * `../reference/nodes.md` from `concepts/pipelines-and-graphs` resolves to `reference/nodes`.
 * A link resolves **within the source it was written in**: `admin/roles.md` inside a Platform page
 * is Platform's roles page, never DataFlow's — the two guides cross-link by URL, not by relative path.
 */
export function resolveDocSlug(currentId: string, href: string): string | null {
  if (/^[a-z]+:/i.test(href) || href.startsWith('#')) return null // external / anchor
  if (!href.endsWith('.md')) return null

  const { source, slug } = parseDocId(currentId)
  const dir = slug.includes('/') ? slug.slice(0, slug.lastIndexOf('/')) : ''
  const segments = dir ? dir.split('/') : []

  for (const part of href.replace(/\.md$/, '').split('/')) {
    if (part === '' || part === '.') continue
    if (part === '..') segments.pop()
    else segments.push(part)
  }
  return docId(source, segments.join('/'))
}
