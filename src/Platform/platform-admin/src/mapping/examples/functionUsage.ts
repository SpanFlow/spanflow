import type { Edge, Node } from '@xyflow/react';
import { compileGraphToDsl, type FunctionDef } from '../nodes/compile';
import { FUNCTION_EXAMPLES } from '../nodes/function/functionExamples';
import type { Recipe } from './recipes';

// Gallery examples that USE a custom function: loading one seeds the function
// (as its own tab) and opens a mapping that calls it. The displayed DSL is the
// inlined result (compiled with the function), and "Run live" runs that.

const src = (fields: string[]): Node => ({ id: 'src', type: 'source', position: { x: 40, y: 60 }, data: { label: 'Source', fields } } as Node);
const call = (fnId: string): Node => ({ id: 'fc', type: 'functionCall', position: { x: 380, y: 60 }, data: { functionId: fnId } } as Node);
const tgt = (fields: string[]): Node => ({ id: 'tgt', type: 'target', position: { x: 700, y: 60 }, data: { label: 'Target', fields } } as Node);
const wire = (s: string, sh: string, t: string, th: string): Edge => ({ id: `${s}-${t}-${th}`, source: s, sourceHandle: sh, target: t, targetHandle: th } as Edge);

interface UsageSpec {
  id: string;
  title: string;
  description: string;
  functionName: string;
  input: unknown;
  output: unknown;
  buildMapping: (fnId: string) => { nodes: Node[]; edges: Edge[] };
}

/** Compile a usage spec's mapping (with the referenced function) to get its DSL. */
function toRecipe(spec: UsageSpec): Recipe {
  const ex = FUNCTION_EXAMPLES.find((e) => e.name === spec.functionName)!;
  const def: FunctionDef = {
    id: spec.functionName,
    name: spec.functionName,
    params: ex.nodes.filter((n) => n.type === 'param').map((n) => (n.data as any).name as string),
    nodes: ex.nodes,
    edges: ex.edges,
  };
  const g = spec.buildMapping(spec.functionName);
  const dsl = compileGraphToDsl(g.nodes, g.edges, { [spec.functionName]: def });
  return {
    id: spec.id,
    title: spec.title,
    category: 'Using functions',
    description: spec.description,
    input: spec.input,
    output: spec.output,
    dsl,
    functionName: spec.functionName,
    buildMapping: spec.buildMapping,
  };
}

const SPECS: UsageSpec[] = [
  {
    id: 'use-fullname',
    title: 'fullName() → displayName',
    description: 'Call fullName(first, last) and write the result to displayName.',
    functionName: 'fullName',
    input: { first: 'Ada', last: 'Lovelace' },
    output: { displayName: 'Ada Lovelace' },
    buildMapping: (fnId) => ({
      nodes: [src(['first', 'last']), call(fnId), tgt(['displayName'])],
      edges: [wire('src', 'first', 'fc', 'first'), wire('src', 'last', 'fc', 'last'), wire('fc', 'out', 'tgt', 'displayName')],
    }),
  },
  {
    id: 'use-total',
    title: 'total() over a list',
    description: 'Pass a source array into total(items) to sum it.',
    functionName: 'total',
    input: { values: [5, 15, 100] },
    output: { grandTotal: 120 },
    buildMapping: (fnId) => ({
      nodes: [src(['values']), call(fnId), tgt(['grandTotal'])],
      edges: [wire('src', 'values', 'fc', 'items'), wire('fc', 'out', 'tgt', 'grandTotal')],
    }),
  },
  {
    id: 'use-person',
    title: 'person() → object field',
    description: 'Call person(first, last) and write its { fullName, lastName } object to card.',
    functionName: 'person',
    input: { first: 'Grace', last: 'Hopper' },
    output: { card: { fullName: 'Grace Hopper', lastName: 'Hopper' } },
    buildMapping: (fnId) => ({
      nodes: [src(['first', 'last']), call(fnId), tgt(['card'])],
      edges: [wire('src', 'first', 'fc', 'first'), wire('src', 'last', 'fc', 'last'), wire('fc', 'out', 'tgt', 'card')],
    }),
  },
];

export const FUNCTION_USAGE: Recipe[] = SPECS.map(toRecipe);
