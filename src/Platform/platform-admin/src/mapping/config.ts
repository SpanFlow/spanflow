// Engine API paths, called through the shared UFMapping client (lib/ufmappingApi.ts)
// which points at UFMapping.Designer.Api and attaches the Platform JWT.
export const EXECUTE_PATH = '/api/mapping/execute';
export const TRACE_PATH = '/api/mapping/trace';
// E73: wire formats (xml, text, edi, positional ...) around a mapping.
export const RUN_PATH = '/api/mapping/run';
export const DECODE_PATH = '/api/mapping/decode';
export const FORMATS_PATH = '/api/mapping/formats';
export const STARTERS_PATH = '/api/mapping/formats/starters';
export const EDI_GUIDES_PATH = '/api/mapping/formats/edi-guides';
