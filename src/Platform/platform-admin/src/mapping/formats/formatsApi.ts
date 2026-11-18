import { create } from 'zustand';
import { ufmappingApi } from '../../lib/ufmappingApi';
import { DECODE_PATH, EDI_GUIDES_PATH, FORMATS_PATH, RUN_PATH, STARTERS_PATH } from '../config';
import { isJsonType, type FormatInfo, type FormatSpec } from './formats';

/** A format refusal from the designer API: the message names the format and the place. */
export interface FormatProblem {
  error: string;
  format?: string;
  direction?: 'decode' | 'encode';
  location?: { line?: number; column?: number; offset?: number; segment?: number; field?: string } | null;
}

/** The readable message of an API failure, or a "could not reach" line when there was no answer. */
export function problemMessage(err: any): string {
  const data = err?.response?.data;
  if (data) return data.error ?? data.Error ?? JSON.stringify(data);
  return `Couldn't reach the mapping engine at ${ufmappingApi.defaults.baseURL}: it may be stopped or restarting. Try again in a few seconds.`;
}

export interface RunResult {
  inputFormat: string;
  outputFormat: string;
  contentType: string;
  output: string;
  decoded: unknown;
  mapped: unknown;
}

/** Raw sample in, raw output out (POST /api/mapping/run). JSON input is sent as JSON. */
export async function runMapping(mapping: unknown, sample: string, input: FormatSpec): Promise<RunResult> {
  const body = isJsonType(input) ? { mapping, inputObject: JSON.parse(sample) } : { mapping, rawInput: sample };
  const res = await ufmappingApi.post(RUN_PATH, body);
  return res.data as RunResult;
}

/** The sample "seen as JSON" (POST /api/mapping/decode). */
export async function decodeSample(format: FormatSpec, sample: string): Promise<unknown> {
  const res = await ufmappingApi.post(DECODE_PATH, { format, rawInput: sample });
  return (res.data as { decoded: unknown }).decoded;
}

// ---- the catalog: every format and its options, from the codecs themselves ------------------------------------

interface CatalogState {
  formats: FormatInfo[] | null;
  status: 'idle' | 'loading' | 'ready' | 'unavailable';
  load: () => Promise<void>;
}

/**
 * Loaded on first need (a non-JSON format picked or a picker opened), never on mount, so a designer that only uses
 * JSON makes no extra request. "unavailable" when the service has no formats route (the public playground).
 */
export const useFormatCatalog = create<CatalogState>((set, get) => ({
  formats: null,
  status: 'idle',
  load: async () => {
    if (get().status === 'loading' || get().status === 'ready') return;
    set({ status: 'loading' });
    try {
      const res = await ufmappingApi.get(FORMATS_PATH);
      set({ formats: res.data as FormatInfo[], status: 'ready' });
    } catch {
      set({ status: 'unavailable' });
    }
  },
}));

// ---- E73-T9: starter layouts (ISO 8583, the positional example), copied into a side's options ------------------

export interface StarterInfo {
  name: string;
  title: string;
  format: string;
  description: string;
  options: Record<string, unknown>;
}

interface StartersState {
  starters: StarterInfo[] | null;
  status: 'idle' | 'loading' | 'ready' | 'unavailable';
  load: () => Promise<void>;
}

/** Loaded the first time a format with starters shows its options; "unavailable" on a service without the route. */
export const useStarters = create<StartersState>((set, get) => ({
  starters: null,
  status: 'idle',
  load: async () => {
    if (get().status === 'loading' || get().status === 'ready') return;
    set({ status: 'loading' });
    try {
      const res = await ufmappingApi.get(STARTERS_PATH);
      set({ starters: res.data as StarterInfo[], status: 'ready' });
    } catch {
      set({ status: 'unavailable' });
    }
  },
}));

// ---- E73-T11: the bundled EDI guides, for the edi format's guides option ----------------------------------------

export interface EdiGuideInfo {
  name: string;
  standard: string;
  message: string;
  version?: string;
  title?: string;
}

interface EdiGuidesState {
  guides: EdiGuideInfo[] | null;
  status: 'idle' | 'loading' | 'ready' | 'unavailable';
  load: () => Promise<void>;
}

export const useEdiGuides = create<EdiGuidesState>((set, get) => ({
  guides: null,
  status: 'idle',
  load: async () => {
    if (get().status === 'loading' || get().status === 'ready') return;
    set({ status: 'loading' });
    try {
      const res = await ufmappingApi.get(EDI_GUIDES_PATH);
      set({ guides: res.data as EdiGuideInfo[], status: 'ready' });
    } catch {
      set({ status: 'unavailable' });
    }
  },
}));
