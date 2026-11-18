import { describe, it, expect } from 'vitest';
import { RECIPES, isFormatRecipe } from './recipes';
import { FORMAT_RECIPES } from './formatRecipes';
import { graphFormats } from '../formats/formats';
import { dslToGraph } from '../nodes/decompile';
import { compileGraphToDsl } from '../engine/dslBuilder';

// Loading an example must never crash: decompile the DSL to a graph and
// recompile it. (Some shapes are lossy; the gallery falls back to structures,
// but neither path may throw.)
describe('recipes load without crashing', () => {
  for (const r of [...RECIPES, ...FORMAT_RECIPES]) {
    it(r.id, () => {
      expect(() => {
        const g = dslToGraph(r.dsl);
        compileGraphToDsl(g.nodes, g.edges);
      }).not.toThrow();
    });
  }
});

// E73-T13: the format examples come from UFMapping's examples/ (scripts/sync-format-recipes.mjs). Loading one must
// keep its formats on the nodes, so the canvas compiles back to the same formats block and runs as the test ran.
describe('format recipes', () => {
  it('cover every pair of the epic', () => {
    expect(FORMAT_RECIPES.map((r) => r.id)).toEqual([
      'format-xml-order-to-json',
      'format-json-invoice-to-xml',
      'format-xml-order-to-pipe-text',
      'format-csv-customers-to-xml',
      'format-log-lines-to-json',
      'format-pk-positional-round-trip',
      'format-bank-file-to-csv',
      'format-x12-850-to-edifact-orders',
      'format-edi-997-ack',
    ]);
    expect(FORMAT_RECIPES.every(isFormatRecipe)).toBe(true);
    expect(RECIPES.some(isFormatRecipe)).toBe(false);
  });

  for (const r of FORMAT_RECIPES) {
    it(`${r.id} keeps its formats through the canvas`, () => {
      const g = dslToGraph(r.dsl);
      expect(graphFormats(g.nodes)).toEqual((r.dsl as { formats?: unknown }).formats);
    });
  }
});
