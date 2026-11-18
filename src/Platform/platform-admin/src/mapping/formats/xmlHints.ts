/**
 * E73: the elements an XML sample repeats under one parent, as paths from the root (/Order/Line), for the xml
 * format's `arrays` option. Listing them keeps one item and two in the same JSON shape. Names keep their prefixes,
 * as the codec's paths do. Returns null when the text is not well-formed XML.
 */
export function repeatedPaths(xml: string): string[] | null {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  if (doc.getElementsByTagName('parsererror').length > 0 || !doc.documentElement) return null;

  const found = new Set<string>();
  const walk = (element: Element, path: string) => {
    const seen = new Map<string, number>();
    for (const child of Array.from(element.children)) seen.set(child.tagName, (seen.get(child.tagName) ?? 0) + 1);
    for (const [name, count] of seen) if (count > 1) found.add(`${path}/${name}`);
    for (const child of Array.from(element.children)) walk(child, `${path}/${child.tagName}`);
  };
  walk(doc.documentElement, `/${doc.documentElement.tagName}`);
  return [...found];
}

/** The list a fill gives: what was there, then the new paths, without repeats. */
export const mergeLists = (current: string[], more: string[]): string[] => [...new Set([...current, ...more])];
