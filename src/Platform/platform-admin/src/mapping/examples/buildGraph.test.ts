import { describe, it, expect } from 'vitest';
import { buildExampleGraph } from './buildGraph';
import { compileGraphToDsl } from '../engine/dslBuilder';
import type { Recipe } from './recipes';

const recipe = (dsl: any, input: unknown, output: unknown): Recipe =>
  ({ id: 'x', title: 'x', category: 'x', description: '', input, dsl, output }) as Recipe;

describe('buildExampleGraph', () => {
  it('wires an array-root recipe (parent + direct fields) so it recompiles to an array', () => {
    const r = recipe(
      { target: { type: 'array', from: 'input.person[]', map: { employeeId: 'id', name: 'fullName' } } },
      { person: [{ id: 1, fullName: 'A' }] },
      [{ employeeId: 1, name: 'A' }],
    );
    const g = buildExampleGraph(r);
    expect(g.nodes.map((n) => n.type)).toEqual(['source', 'target']);
    expect(g.edges.length).toBe(3); // $root parent + 2 fields

    const dsl = compileGraphToDsl(g.nodes, g.edges);
    expect(dsl.target).toEqual({ type: 'array', from: 'input.person[]', map: { employeeId: 'id', name: 'fullName' } });
  });

  it('wires a foreach list->list recipe', () => {
    const r = recipe(
      { target: { type: 'object', mappings: [{ target: 'lines', type: 'foreach', from: 'input.orders[]', map: { orderId: 'id' } }] } },
      { orders: [{ id: 1 }] },
      { lines: [{ orderId: 1 }] },
    );
    const dsl = compileGraphToDsl(...Object.values(buildExampleGraph(r)) as [any, any]);
    expect(dsl.target.mappings).toEqual([{ target: 'lines', type: 'foreach', from: 'input.orders[]', map: { orderId: 'id' } }]);
  });
});
