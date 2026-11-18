/**
 * E73-T8: the positional layout as the designer edits it, plus the two things the layout editor draws from it: the
 * ruler (which characters of the sample each field takes) and "guess from sample" (a first layout proposed from the
 * sample's columns).
 *
 * The shape is the one the positional codec reads (UFMapping `PositionalLayout.cs`): `fields` (the whole payload is
 * one record) or `records` (one record per line, chosen by `when`). The ruler mirrors the codec's reading rules for
 * the parts that decide positions (length, prefix, rest, groups, repeats, records); the service stays the judge of
 * the values. Lengths are measured in characters, the codec's default unit.
 */
export const FIELD_TYPES = ['text', 'alpha', 'alphanumeric', 'numeric', 'integer', 'decimal', 'date', 'bool'] as const;

export interface LayoutField {
  name?: string;
  length?: number;
  prefix?: number;
  rest?: boolean;
  type?: string;
  scale?: number;
  sign?: string;
  pad?: string;
  align?: string;
  trim?: boolean;
  format?: string;
  values?: string[];
  const?: string;
  filler?: boolean;
  max?: number;
  fields?: LayoutField[];
  repeat?: number | string;
  /** E73-T9: an ISO 8583 style bitmap: its fields each carry a `bit` and only the set ones follow. */
  bitmap?: 'hex' | 'binary';
  bit?: number;
}

export interface LayoutRecord {
  name: string;
  when?: { start?: number; equals: string };
  many?: boolean;
  fields: LayoutField[];
}

export interface Layout {
  fields?: LayoutField[];
  records?: LayoutRecord[];
}

/** Where a field sits: its record (null for a one-record layout) and its index at each level of groups. */
export interface FieldLoc {
  record: number | null;
  path: number[];
}

export const locKey = (loc: FieldLoc): string => `${loc.record ?? '-'}:${loc.path.join('.')}`;

export type FieldKind = 'data' | 'const' | 'filler' | 'group' | 'bitmap';

export const fieldKind = (f: LayoutField): FieldKind =>
  f.bitmap ? 'bitmap' : f.fields ? 'group' : f.const !== undefined ? 'const' : f.filler ? 'filler' : 'data';

export type SizeKind = 'length' | 'prefix' | 'rest';

export const sizeKind = (f: LayoutField): SizeKind => (f.prefix !== undefined ? 'prefix' : f.rest ? 'rest' : 'length');

/** A layout object read from an option value; anything else is treated as empty. */
export function asLayout(value: unknown): Layout {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return { fields: [] };
  const v = value as Layout;
  if (Array.isArray(v.records)) return { records: v.records };
  return { fields: Array.isArray(v.fields) ? v.fields : [] };
}

/** The field list a record (or the one-record layout) holds. */
export function fieldsOf(layout: Layout, record: number | null): LayoutField[] {
  if (record === null) return layout.fields ?? [];
  return layout.records?.[record]?.fields ?? [];
}

// ---- the ruler ------------------------------------------------------------------------------------------------

export type SpanKind = 'value' | 'prefix' | 'const' | 'filler' | 'bitmap' | 'left' | 'unmatched';

export interface RulerSpan {
  line: number;
  start: number;
  end: number;
  kind: SpanKind;
  /** The field this span belongs to (absent for left-over or unmatched text). */
  loc?: FieldLoc;
  /** Colour slot: the top-level field index, so a group's parts share their group's colour. */
  colour: number;
  label: string;
}

export interface RulerProblem {
  line: number;
  offset: number;
  message: string;
}

export interface Ruler {
  spans: RulerSpan[];
  problems: RulerProblem[];
  /** The record each line was read as (index into layout.records), null for one-record layouts or no match. */
  lineRecords: (number | null)[];
}

const isDigits = (s: string) => /^[0-9]+$/.test(s);
const TEXTISH = new Set(['text', 'alpha', 'alphanumeric']);

class Stop extends Error {}

/** Which characters of the sample each field of the layout takes, line by line, with what went wrong where. */
export function measure(layout: Layout, sample: string): Ruler {
  const ruler: Ruler = { spans: [], problems: [], lineRecords: [] };
  const lines = sample.split('\n').map((l) => (l.endsWith('\r') ? l.slice(0, -1) : l));

  if (!layout.records) {
    // No fields yet: nothing to place, and nothing is wrong yet either.
    if ((layout.fields ?? []).length === 0) return ruler;
    // One record: the whole sample, without the single line break an editor leaves at the end.
    const text = lines.length > 1 && lines.at(-1) === '' ? lines.slice(0, -1).join('\n') : lines.join('\n');
    ruler.lineRecords.push(null);
    measureRecord(text, 0, layout.fields ?? [], null, ruler);
    return ruler;
  }

  lines.forEach((line, i) => {
    if (line.length === 0) {
      ruler.lineRecords.push(null);
      return;
    }
    const index = layout.records!.findIndex((r) => matches(r, line));
    ruler.lineRecords.push(index >= 0 ? index : null);
    if (index < 0) {
      ruler.spans.push({ line: i, start: 0, end: line.length, kind: 'unmatched', colour: -1, label: 'No record matches this line' });
      ruler.problems.push({ line: i, offset: 0, message: 'No record in the layout matches this line.' });
      return;
    }
    measureRecord(line, i, layout.records![index].fields ?? [], index, ruler);
  });
  return ruler;
}

function matches(record: LayoutRecord, line: string): boolean {
  const equals = record.when?.equals;
  if (!equals) return true;
  const start = record.when?.start ?? 0;
  return line.substr(start, equals.length) === equals;
}

function measureRecord(text: string, line: number, fields: LayoutField[], record: number | null, ruler: Ruler) {
  const cursor = { pos: 0 };
  try {
    walk(text, line, fields, record, [], cursor, ruler, null);
  } catch (e) {
    if (!(e instanceof Stop)) throw e;
    return;
  }
  if (cursor.pos < text.length) {
    ruler.spans.push({ line, start: cursor.pos, end: text.length, kind: 'left', colour: -1, label: 'Not in any field' });
    ruler.problems.push({
      line,
      offset: cursor.pos,
      message: `${text.length - cursor.pos} characters left after the last field.`,
    });
  }
}

function walk(
  text: string,
  line: number,
  fields: LayoutField[],
  record: number | null,
  path: number[],
  cursor: { pos: number },
  ruler: Ruler,
  colour: number | null,
  pick?: number[],
) {
  // Raw values of the fields read so far at this level, for a group that repeats by an earlier count field.
  const values: Record<string, string> = {};
  (pick ?? fields.map((_, i) => i)).forEach((i) => {
    const field = fields[i];
    const here = [...path, i];
    const loc: FieldLoc = { record, path: here };
    const slot = colour ?? i;
    const label = field.name ?? (field.const !== undefined ? `const "${field.const}"` : field.filler ? 'filler' : field.bitmap ? 'bitmap' : `field ${i + 1}`);
    const fail = (message: string): never => {
      ruler.problems.push({ line, offset: cursor.pos, message: `${label}: ${message}` });
      throw new Stop();
    };

    if (field.bitmap) {
      const bits = readBitmap(text, line, cursor, field.bitmap, loc, slot, label, ruler, fail);
      const members = field.fields ?? [];
      const order: number[] = [];
      for (let bit = 2; bit < bits.length; bit++) {
        if (!bits[bit]) continue;
        const j = members.findIndex((m) => m.bit === bit);
        if (j < 0) fail(`has bit ${bit} set but no field for it.`);
        order.push(j);
      }
      walk(text, line, members, record, here, cursor, ruler, slot, order);
      return;
    }

    if (field.fields) {
      let count = 1;
      if (typeof field.repeat === 'number') count = field.repeat;
      else if (typeof field.repeat === 'string') {
        const raw = values[field.repeat];
        if (raw === undefined) fail(`repeats by "${field.repeat}", which is not a field before it.`);
        count = Number.parseInt(raw.trim() || '0', 10);
        if (!Number.isFinite(count) || count < 0) fail(`count field "${field.repeat}" holds "${raw}", not a number.`);
      }
      for (let n = 0; n < count; n++) walk(text, line, field.fields, record, here, cursor, ruler, slot);
      return;
    }

    const start = cursor.pos;
    let raw: string;
    if (field.prefix !== undefined) {
      const digits = text.substr(start, field.prefix);
      if (digits.length < field.prefix || !isDigits(digits)) fail(`needs a ${field.prefix}-digit length first, found "${digits}".`);
      const length = Number.parseInt(digits, 10);
      ruler.spans.push({ line, start, end: start + field.prefix, kind: 'prefix', loc, colour: slot, label: `${label} length (${length})` });
      if (field.max !== undefined && length > field.max) fail(`says it is ${length} long, more than its max of ${field.max}.`);
      const from = start + field.prefix;
      if (from + length > text.length) {
        fail(`says it is ${length} long but only ${text.length - from} characters are left.`);
      }
      raw = text.substr(from, length);
      cursor.pos = from + length;
      ruler.spans.push({ line, start: from, end: cursor.pos, kind: spanKind(field), loc, colour: slot, label });
    } else if (field.rest) {
      raw = text.slice(start);
      cursor.pos = text.length;
      ruler.spans.push({ line, start, end: cursor.pos, kind: spanKind(field), loc, colour: slot, label });
    } else {
      const length = field.length ?? 0;
      if (length <= 0) fail('needs a length more than 0.');
      if (start + length > text.length) {
        // An editor may strip trailing spaces: a short text field at the very end is read as padded.
        const textish = TEXTISH.has(field.type ?? 'text') && field.const === undefined;
        if (!textish || start >= text.length) fail(`needs ${length} characters but only ${text.length - start} are left.`);
      }
      raw = text.substr(start, length);
      cursor.pos = Math.min(start + length, text.length);
      ruler.spans.push({ line, start, end: cursor.pos, kind: spanKind(field), loc, colour: slot, label });
    }
    if (field.const !== undefined && raw !== field.const) {
      ruler.problems.push({ line, offset: start, message: `${label}: expected "${field.const}", found "${raw}".` });
    }
    if (field.name) values[field.name] = raw;
  });
}

/** The set bits (1 to 64, or to 128 when bit 1 says a secondary bitmap follows), with a span for each bitmap part. */
function readBitmap(
  text: string,
  line: number,
  cursor: { pos: number },
  mode: 'hex' | 'binary',
  loc: FieldLoc,
  colour: number,
  label: string,
  ruler: Ruler,
  fail: (message: string) => never,
): boolean[] {
  const bits: boolean[] = [];
  const part = (first: number) => {
    const width = mode === 'hex' ? 16 : 8;
    const start = cursor.pos;
    const raw = text.substr(start, width);
    if (raw.length < width) fail(`needs ${width} ${mode === 'hex' ? 'hex characters' : 'bytes'} for its bitmap, found ${raw.length}.`);
    if (mode === 'hex' && !/^[0-9A-Fa-f]{16}$/.test(raw)) fail(`bitmap must be 16 hex characters, found "${raw}".`);
    const bytes = mode === 'hex' ? raw.match(/../g)!.map((h) => parseInt(h, 16)) : [...raw].map((c) => c.charCodeAt(0) & 0xff);
    for (let i = 0; i < 64; i++) bits[first + i] = (bytes[i >> 3] & (0x80 >> (i & 7))) !== 0;
    cursor.pos = start + width;
    ruler.spans.push({ line, start, end: cursor.pos, kind: 'bitmap', loc, colour, label: `${label} ${first === 1 ? 'primary' : 'secondary'}` });
  };
  part(1);
  if (bits[1]) part(65);
  return bits;
}

const spanKind = (f: LayoutField): SpanKind => (f.const !== undefined ? 'const' : f.filler ? 'filler' : 'value');

/** Where each field starts and ends on the first line read with it ("-" when the sample does not reach it). */
export function firstPositions(ruler: Ruler): Map<string, { start: number; end: number }> {
  const out = new Map<string, { start: number; end: number; open: boolean }>();
  for (const span of ruler.spans) {
    if (!span.loc) continue;
    const key = locKey(span.loc);
    const seen = out.get(key);
    if (!seen) out.set(key, { start: span.start, end: span.end, open: span.kind === 'prefix' });
    // A prefix and its value are two spans side by side: one field. A repeat of the field is not.
    else if (seen.open) {
      seen.end = span.end;
      seen.open = false;
    }
  }
  return out;
}

// ---- guess from sample -----------------------------------------------------------------------------------------

type CharClass = 'd' | 'a' | 's' | 'o';
const classOf = (c: string): CharClass => (/[0-9]/.test(c) ? 'd' : /\p{L}/u.test(c) ? 'a' : c === ' ' ? 's' : 'o');

/**
 * A first layout from the sample's columns: a field starts wherever the kind of character changes (digits,
 * letters, other), with trailing spaces kept as the padding of the field before them, and a split is kept when most
 * lines agree on it. A file whose lines start with different two-character codes becomes header / detail / trailer
 * records chosen by that code. It is a proposal: rename, merge and retype the fields after.
 */
export function guessLayout(sample: string): Layout {
  const lines = sample
    .split('\n')
    .map((l) => (l.endsWith('\r') ? l.slice(0, -1) : l))
    .filter((l) => l.length > 0);
  if (lines.length === 0) return { fields: [{ name: 'value', rest: true, type: 'text' }] };
  if (lines.length === 1) return { fields: guessFields(lines) };

  const codes = lines.map((l) => l.slice(0, 2));
  const distinct = [...new Set(codes)];
  if (distinct.length < 2 || distinct.length >= lines.length || distinct.some((c) => c.length < 2)) {
    return { records: [{ name: 'rows', many: true, fields: guessFields(lines) }] };
  }

  const used = new Set<string>();
  const records: LayoutRecord[] = distinct.map((code) => {
    const group = lines.filter((l) => l.startsWith(code));
    const many = group.length > 1;
    let name = !many && lines[0].startsWith(code) ? 'header' : !many && lines.at(-1)!.startsWith(code) ? 'trailer' : many ? 'details' : `record${code}`;
    if (used.has(name)) name = `record${code}`;
    used.add(name);
    return {
      name,
      when: { start: 0, equals: code },
      ...(many ? { many: true } : {}),
      fields: [{ const: code, length: 2 }, ...guessFields(group.map((l) => l.slice(2)))],
    };
  });
  return { records };
}

function guessFields(lines: string[]): LayoutField[] {
  const width = Math.max(...lines.map((l) => l.length));
  if (width === 0) return [{ name: 'value', rest: true, type: 'text' }];

  const cuts: number[] = [];
  for (let p = 1; p < width; p++) {
    let votes = 0;
    let voters = 0;
    for (const l of lines) {
      if (l.length <= p) continue;
      voters++;
      const before = classOf(l[p - 1]);
      const here = classOf(l[p]);
      if (here !== before && here !== 's') votes++;
    }
    if (voters > 0 && votes * 2 > voters) cuts.push(p);
  }

  const bounds = [0, ...cuts, width];
  const sameWidth = lines.every((l) => l.length === width);
  const fields: LayoutField[] = [];
  for (let i = 0; i < bounds.length - 1; i++) {
    const start = bounds[i];
    const end = bounds[i + 1];
    const cells = lines.map((l) => l.slice(start, end).trim()).filter(Boolean);
    const last = i === bounds.length - 2;
    let type = 'text';
    if (cells.length && cells.every((c) => /^[0-9]+$/.test(c))) type = 'numeric';
    else if (cells.length && cells.every((c) => /^\p{L}+$/u.test(c))) type = 'alpha';
    // A last column whose lines end at different places is text, which reads short as padded.
    if (last && !sameWidth && type !== 'alpha') type = 'text';
    fields.push({ name: `field${i + 1}`, length: end - start, type });
  }
  return fields;
}

// ---- editing ---------------------------------------------------------------------------------------------------

const clone = <T>(v: T): T => JSON.parse(JSON.stringify(v)) as T;

/** The list a location's field sits in, inside a copy of the layout, and the field's index in it. */
function locate(layout: Layout, loc: FieldLoc): { list: LayoutField[]; index: number } {
  let list = loc.record === null ? (layout.fields ??= []) : layout.records![loc.record].fields;
  for (let d = 0; d < loc.path.length - 1; d++) list = list[loc.path[d]].fields ??= [];
  return { list, index: loc.path[loc.path.length - 1] };
}

export function getField(layout: Layout, loc: FieldLoc): LayoutField | undefined {
  try {
    const { list, index } = locate(clone(layout), loc);
    return list[index];
  } catch {
    return undefined;
  }
}

/** Replace a field (keys set to undefined or "" are removed, except a constant, which may be cleared while typed). */
export function setField(layout: Layout, loc: FieldLoc, field: LayoutField): Layout {
  const next = clone(layout);
  const { list, index } = locate(next, loc);
  list[index] = Object.fromEntries(
    Object.entries(field).filter(([k, v]) => v !== undefined && (v !== '' || k === 'const')),
  ) as LayoutField;
  return next;
}

/** Add a field after a location, or at the end of a record (or group, with `inGroup`). */
export function addField(layout: Layout, record: number | null, after?: FieldLoc, inGroup?: FieldLoc): Layout {
  const next = clone(layout);
  const fresh: LayoutField = { name: nextName(layout), length: 1, type: 'text' };
  if (inGroup) {
    const { list, index } = locate(next, inGroup);
    const members = (list[index].fields ??= []);
    if (list[index].bitmap) fresh.bit = Math.min(128, Math.max(1, ...members.map((m) => m.bit ?? 1)) + 1);
    members.push(fresh);
  } else if (after) {
    const { list, index } = locate(next, after);
    list.splice(index + 1, 0, fresh);
  } else if (record === null) {
    (next.fields ??= []).push(fresh);
  } else {
    next.records![record].fields.push(fresh);
  }
  return next;
}

export function removeField(layout: Layout, loc: FieldLoc): Layout {
  const next = clone(layout);
  const { list, index } = locate(next, loc);
  list.splice(index, 1);
  return next;
}

export function moveField(layout: Layout, loc: FieldLoc, by: -1 | 1): Layout {
  const next = clone(layout);
  const { list, index } = locate(next, loc);
  const to = index + by;
  if (to < 0 || to >= list.length) return layout;
  [list[index], list[to]] = [list[to], list[index]];
  return next;
}

/** Turn a one-record layout into line records, or back (keeping the first record's fields). */
export function switchMode(layout: Layout, toRecords: boolean): Layout {
  if (toRecords) return layout.records ? layout : { records: [{ name: 'rows', many: true, fields: layout.fields ?? [] }] };
  return layout.records ? { fields: layout.records[0]?.fields ?? [] } : layout;
}

export function addRecord(layout: Layout): Layout {
  const records = [...(layout.records ?? [])];
  let n = records.length + 1;
  while (records.some((r) => r.name === `record${n}`)) n++;
  records.push({ name: `record${n}`, when: { start: 0, equals: '' }, fields: [{ name: 'field1', length: 1, type: 'text' }] });
  return { records };
}

function nextName(layout: Layout): string {
  const names = new Set<string>();
  const collect = (fields: LayoutField[] = []) =>
    fields.forEach((f) => {
      if (f.name) names.add(f.name);
      collect(f.fields);
    });
  collect(layout.fields);
  layout.records?.forEach((r) => collect(r.fields));
  let n = 1;
  while (names.has(`field${n}`)) n++;
  return `field${n}`;
}

/** Change what a field is (data, constant, filler or group), keeping its width where that still applies. */
export function changeKind(field: LayoutField, kind: FieldKind): LayoutField {
  const width = field.length ?? (field.const?.length || 1);
  switch (kind) {
    case 'group':
      return { name: field.name ?? 'group', fields: field.fields ?? [{ name: 'field1', length: width, type: 'text' }] };
    case 'bitmap':
      return { bitmap: 'hex', fields: [{ bit: 2, name: field.name ?? 'field2', length: width, type: 'text' }] };
    case 'const':
      return { const: field.const ?? ''.padEnd(width, ' '), length: width };
    case 'filler':
      return { filler: true, length: width };
    default:
      return { name: field.name ?? 'field', length: width, type: 'text' };
  }
}

/** Change how a field's width is given, keeping the number where it carries over. */
export function changeSize(field: LayoutField, kind: SizeKind): LayoutField {
  const { length, prefix, rest, ...others } = field;
  void rest;
  if (kind === 'length') return { ...others, length: length ?? 1 };
  if (kind === 'prefix') return { ...others, prefix: prefix ?? 2 };
  return { ...others, rest: true };
}

/** One line for the picker: how many fields (or records) and how wide. */
export function summarise(layout: Layout): string {
  const count = (fields: LayoutField[] = []): number => fields.reduce((n, f) => n + (f.fields ? count(f.fields) : 1), 0);
  if (layout.records) {
    const r = layout.records.length;
    return `${r} record${r === 1 ? '' : 's'}, ${layout.records.reduce((n, rec) => n + count(rec.fields), 0)} fields`;
  }
  const n = count(layout.fields);
  return n === 0 ? 'No fields yet' : `${n} field${n === 1 ? '' : 's'}`;
}
