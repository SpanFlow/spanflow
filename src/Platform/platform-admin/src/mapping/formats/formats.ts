import type { Node } from '@xyflow/react';

/**
 * E73: the wire formats around a mapping. The engine always maps JSON; a format says how the raw input is read into
 * that JSON and how the mapped JSON is written out. The input format lives on the Source node and the output format
 * on the Target node (`data.format`), so it travels with the graph everywhere a graph goes (tabs, autosave, undo,
 * saved mappings), and compiling the graph writes it into the definition's `formats` block.
 */
export interface FormatSpec {
  type: string;
  options?: Record<string, unknown>;
}

export interface MappingFormats {
  input?: FormatSpec;
  output?: FormatSpec;
}

export type FormatOptionKind = 'bool' | 'int' | 'string' | 'stringList' | 'object' | 'any';
export type FormatOptionUse = 'both' | 'decodeOnly' | 'encodeOnly';

export interface FormatOptionInfo {
  name: string;
  kind: FormatOptionKind;
  use: FormatOptionUse;
  description: string;
}

export interface FormatInfo {
  type: string;
  contentType: string;
  options: FormatOptionInfo[];
}

export type Direction = 'input' | 'output';

/** The formats offered before the catalog has loaded (and the order they are listed in). */
export const FORMAT_TYPES = ['json', 'text', 'xml', 'positional', 'edi'] as const;

export const FORMAT_LABELS: Record<string, string> = {
  json: 'JSON',
  text: 'Text (lines, CSV, pattern, fixed)',
  xml: 'XML',
  positional: 'Positional (ISO, bank files)',
  edi: 'EDI (X12, EDIFACT)',
};

/**
 * Choices for the text options that take one of a few words. The codecs describe every option themselves (the
 * catalog); this only turns the well-known ones into a select instead of a free text box.
 */
export const OPTION_CHOICES: Record<string, string[]> = {
  'text.mode': ['whole', 'lines', 'delimited', 'pattern', 'fixed'],
  'text.types': ['string', 'infer'],
  'xml.types': ['string', 'infer'],
  'text.unit': ['chars', 'bytes'],
  'positional.unit': ['chars', 'bytes'],
  'edi.standard': ['x12', 'edifact'],
};

/** JSON with no options is the default on either side: nothing to write into the definition. */
export const isPlainJson = (spec?: FormatSpec | null): boolean =>
  !spec || !spec.type || (spec.type === 'json' && Object.keys(spec.options ?? {}).length === 0);

/** The formats a graph declares: the Source node's input format and the Target node's output format. */
export function graphFormats(nodes: Node[]): MappingFormats | undefined {
  const input = nodes.find((n) => n.type === 'source')?.data?.format as FormatSpec | undefined;
  const output = nodes.find((n) => n.type === 'target')?.data?.format as FormatSpec | undefined;
  const formats: MappingFormats = {};
  if (!isPlainJson(input)) formats.input = clean(input!);
  if (!isPlainJson(output)) formats.output = clean(output!);
  return formats.input || formats.output ? formats : undefined;
}

/** The input format of a graph, json when none is set. */
export const inputFormatOf = (nodes: Node[]): FormatSpec =>
  (nodes.find((n) => n.type === 'source')?.data?.format as FormatSpec | undefined) ?? { type: 'json' };

/** Options that only apply in the other direction are left out of a side's form. */
export const optionsFor = (info: FormatInfo | undefined, direction: Direction): FormatOptionInfo[] =>
  (info?.options ?? []).filter((o) => o.use === 'both' || (direction === 'input' ? o.use === 'decodeOnly' : o.use === 'encodeOnly'));

function clean(spec: FormatSpec): FormatSpec {
  const options = Object.fromEntries(Object.entries(spec.options ?? {}).filter(([, v]) => v !== undefined && v !== ''));
  return Object.keys(options).length ? { type: spec.type, options } : { type: spec.type };
}

export const isJsonType = (spec?: FormatSpec | null): boolean => !spec || !spec.type || spec.type === 'json';
