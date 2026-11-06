import { describe, it, expect } from 'vitest';
import type { Node } from '@xyflow/react';
import { matchChildConnections } from './autoConnect';

const nodes: Node[] = [
  { id: 'src', type: 'source', position: { x: 0, y: 0 }, data: { fields: ['orders', 'orders.id', 'orders.amount'] } } as Node,
  { id: 'tgt', type: 'target', position: { x: 0, y: 0 }, data: { fields: ['orders', 'orders.id', 'orders.total'] } } as Node,
];

describe('matchChildConnections', () => {
  it('maps same-named children when connecting two parents', () => {
    const out = matchChildConnections({ source: 'src', sourceHandle: 'orders', target: 'tgt', targetHandle: 'orders' }, nodes, []);
    // id matches; amount/total do not
    expect(out).toEqual([
      { source: 'src', sourceHandle: 'orders.id', target: 'tgt', targetHandle: 'orders.id' },
    ]);
  });

  it('skips children already wired', () => {
    const existing = [{ id: 'e1', source: 'src', sourceHandle: 'orders.id', target: 'tgt', targetHandle: 'orders.id' }] as any;
    const out = matchChildConnections({ source: 'src', sourceHandle: 'orders', target: 'tgt', targetHandle: 'orders' }, nodes, existing);
    expect(out).toEqual([]);
  });

  it('does nothing for non source->target connections', () => {
    const out = matchChildConnections({ source: 'tgt', sourceHandle: 'orders', target: 'src', targetHandle: 'orders' }, nodes, []);
    expect(out).toEqual([]);
  });
});
