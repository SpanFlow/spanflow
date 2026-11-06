import type { Recipe } from './recipes';
import { FUNCTION_EXAMPLES } from '../nodes/function/functionExamples';
import { compileFunctionDoc } from '../nodes/compile';

// The function library gallery: each example function shown on its own (not
// wired into a mapping — see functionUsage.ts for "using a function"
// examples). Loading one opens just that function's own tab.

// Manually worked outputs for each function's sample input (compileFunctionDoc
// binds params to input.<name> and writes the return value to `result`).
const OUTPUTS: Record<string, unknown> = {
  fullName: { result: 'Ada Lovelace' },
  greeting: { result: 'Hello, Ada!' },
  shout: { result: 'HELLO' },
  total: { result: 60 },
  sortDesc: { result: [30, 20, 10] },
  person: { result: { fullName: 'Ada Lovelace', lastName: 'Lovelace' } },
};

export const FUNCTION_DEFINITIONS: Recipe[] = FUNCTION_EXAMPLES.map((ex) => ({
  id: `fn-${ex.name}`,
  title: `${ex.name}()`,
  category: 'Function library',
  description: ex.description,
  input: JSON.parse(ex.sampleInput),
  dsl: compileFunctionDoc(ex.nodes, ex.edges),
  output: OUTPUTS[ex.name] ?? null,
  functionName: ex.name,
}));
