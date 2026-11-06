import { describe, it, expect } from 'vitest';
import { buildFieldTree, renamePath, removePath } from './fieldTree';

describe('buildFieldTree', () => {
  it('nests dotted paths into a depth-annotated tree', () => {
    const tree = buildFieldTree(['user.name', 'orders', 'orders.id', 'orders.amount']);
    expect(tree).toEqual([
      { segment: 'user', path: 'user', connectable: false, depth: 0, hasChildren: true },
      { segment: 'name', path: 'user.name', connectable: true, depth: 1, hasChildren: false },
      { segment: 'orders', path: 'orders', connectable: true, depth: 0, hasChildren: true },
      { segment: 'id', path: 'orders.id', connectable: true, depth: 1, hasChildren: false },
      { segment: 'amount', path: 'orders.amount', connectable: true, depth: 1, hasChildren: false },
    ]);
  });

  it('keeps children grouped even when fields are unordered', () => {
    const tree = buildFieldTree(['orders.id', 'user.name', 'orders.amount']);
    expect(tree.map((n) => n.path)).toEqual(['orders', 'orders.id', 'orders.amount', 'user', 'user.name']);
  });
});

describe('renamePath / removePath', () => {
  it('renames a node and its descendants', () => {
    expect(renamePath(['orders', 'orders.id'], 'orders', 'lines')).toEqual(['lines', 'lines.id']);
  });
  it('removes a node and everything under it', () => {
    expect(removePath(['orders', 'orders.id', 'user'], 'orders')).toEqual(['user']);
  });
});
