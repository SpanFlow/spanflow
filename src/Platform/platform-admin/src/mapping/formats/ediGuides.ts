/**
 * E73-T11: the edi format's `guides` option, as the picker sees it. The option is "bundled", a bundled guide's
 * name, a guide object of your own, or a list of those; the picker reads it into three parts and writes it back in
 * the shortest form.
 */
export interface GuidesChoice {
  /** Every bundled guide. */
  all: boolean;
  /** Bundled guides picked one by one (ignored when `all`). */
  names: string[];
  /** Guides of your own (objects), which come before the bundled ones. */
  own: Record<string, unknown>[];
}

export function readGuides(value: unknown): GuidesChoice {
  const items = value === undefined || value === null ? [] : Array.isArray(value) ? value : [value];
  const choice: GuidesChoice = { all: false, names: [], own: [] };
  for (const item of items) {
    if (item === 'bundled') choice.all = true;
    else if (typeof item === 'string') choice.names.push(item);
    else if (item && typeof item === 'object') choice.own.push(item as Record<string, unknown>);
  }
  return choice;
}

/** Back to the option: own guides first (they win), then "bundled" or the picked names; nothing picked is unset. */
export function writeGuides(choice: GuidesChoice): unknown {
  const items: unknown[] = [...choice.own, ...(choice.all ? ['bundled'] : choice.names)];
  if (items.length === 0) return undefined;
  return items.length === 1 ? items[0] : items;
}
