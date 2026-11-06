// The graph compiler now lives in the nodes layer, where each node spec owns
// its own compile logic. This module re-exports it for existing imports.
export { compileGraphToDsl } from '../nodes/compile';
export type { MappingDSL } from '../nodes/compile';
