import { describe, it, expect } from 'vitest';
import { dslToGraph } from './decompile';
import { compileGraphToDsl } from '../engine/dslBuilder';

// Decompiling a DSL into a graph and recompiling it should reproduce the DSL
// (node positions aside). Rules are sorted by target for a stable comparison.
const roundTrip = (dsl: any) => {
  const graph = dslToGraph(dsl);
  const out = compileGraphToDsl(graph.nodes, graph.edges);
  const byTarget = (m: any[]) => [...m].sort((a, b) => a.target.localeCompare(b.target));
  return { actual: byTarget(out.target.mappings ?? []), expected: byTarget(dsl.target.mappings) };
};

describe('dslToGraph round-trip', () => {
  it('reproduces a mix of field copy, aggregate, filter, and chained value-map', () => {
    const dsl = {
      target: {
        type: 'object',
        mappings: [
          { target: 'name', type: 'fieldCopy', path: 'input.user.name' },
          { target: 'total', type: 'aggregate', function: 'sum', from: 'input.orders[]', select: 'amount' },
          { target: 'count', type: 'aggregate', function: 'count', from: 'input.orders[]' },
          { target: 'done', type: 'filter', where: { field: 'status', op: 'eq', value: 'Completed' }, from: 'input.orders' },
          {
            target: 'monthName',
            type: 'valueMap',
            table: { '1': 'Jan' },
            default: 'NA',
            input: { aggregate: { function: 'min', from: 'input.orders[]', select: 'id' } },
          },
        ],
      },
    };

    const { actual, expected } = roundTrip(dsl);
    expect(actual).toEqual(expected);
  });

  it('reproduces sort and join', () => {
    const dsl = {
      target: {
        type: 'object',
        mappings: [
          { target: 'sorted', type: 'sort', from: 'input.orders', keys: [{ by: 'amount', order: 'desc' }] },
          { target: 'rows', type: 'join', left: 'input.orders[]', right: 'input.customers[]', leftKey: 'customerId', rightKey: 'custId' },
        ],
      },
    };

    const { actual, expected } = roundTrip(dsl);
    expect(actual).toEqual(expected);
  });

  it('produces a single source and target node', () => {
    const graph = dslToGraph({
      target: { type: 'object', mappings: [{ target: 'x', type: 'fieldCopy', path: 'input.a' }] },
    });
    expect(graph.nodes.filter((n) => n.type === 'source')).toHaveLength(1);
    expect(graph.nodes.filter((n) => n.type === 'target')).toHaveLength(1);
  });
});
