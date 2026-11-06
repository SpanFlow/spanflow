import { describe, it, expect } from 'vitest';
import { RECIPES } from './recipes';
import { dslToGraph } from '../nodes/decompile';
import { compileGraphToDsl } from '../engine/dslBuilder';

// Loading an example must never crash: decompile the DSL to a graph and
// recompile it. (Some shapes are lossy; the gallery falls back to structures,
// but neither path may throw.)
describe('recipes load without crashing', () => {
  for (const r of RECIPES) {
    it(r.id, () => {
      expect(() => {
        const g = dslToGraph(r.dsl);
        compileGraphToDsl(g.nodes, g.edges);
      }).not.toThrow();
    });
  }
});
