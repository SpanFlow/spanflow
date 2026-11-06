import { describe, it, expect } from 'vitest';
import { RECIPES } from './recipes';
import { dslToGraph } from '../nodes/decompile';
import { compileGraphToDsl } from '../engine/dslBuilder';
import { TRANSFORMS, TRANSFORM_GROUPS } from '../nodes/functions/transform/transforms';

const norm = (d: any) => [...(d?.target?.mappings ?? [])].sort((x, y) => String(x.target).localeCompare(String(y.target)));

describe('conversion recipes', () => {
  const conversions = RECIPES.filter((r) => r.category === 'Conversions');

  it('exist', () => expect(conversions.length).toBeGreaterThan(0));

  // "Load into canvas" only opens the faithful node graph when it recompiles to
  // the same DSL; these must, so the example is editable as the real nodes.
  for (const r of conversions) {
    it(`${r.id} round-trips through the canvas`, () => {
      const g = dslToGraph(r.dsl);
      expect(norm(compileGraphToDsl(g.nodes, g.edges))).toEqual(norm(r.dsl));
    });
  }

  it('every function a recipe uses is in the dropdown', () => {
    const known = new Set(TRANSFORMS.map((t) => t.value));
    const used = conversions.flatMap((r) => r.dsl.target.mappings ?? []).map((m: any) => m.function).filter(Boolean);
    expect(used.filter((f: string) => !known.has(f))).toEqual([]);
  });
});

describe('transform catalogue', () => {
  it('puts every function in a known group, once', () => {
    expect(TRANSFORMS.filter((t) => !TRANSFORM_GROUPS.includes(t.group))).toEqual([]);
    const values = TRANSFORMS.map((t) => t.value);
    expect(new Set(values).size).toBe(values.length);
  });
});

describe('decompiling a transform', () => {
  it('keeps a null argument blank instead of turning it into the text null', () => {
    const dsl = {
      target: {
        type: 'object' as const,
        mappings: [{ target: 'd', type: 'transform', function: 'formatDate', args: ['input.at', "'iso'", null, "'utc'"] }],
      },
    };
    const g = dslToGraph(dsl as any);
    const node = g.nodes.find((n) => n.type === 'transform')!;
    expect((node.data as any).args[2].value).toBe('');
    // ...which compiles to an empty literal, read by the engine as "not given".
    expect((compileGraphToDsl(g.nodes, g.edges) as any).target.mappings[0].args[2]).toBe("''");
  });
});
