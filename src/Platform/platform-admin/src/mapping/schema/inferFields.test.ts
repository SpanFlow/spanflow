import { describe, it, expect } from 'vitest';
import { inferSourceFields, inferTargetFields, inferStructure } from './inferFields';

const sample = {
  user: { name: 'Bob', age: 30 },
  orders: [
    { id: 1, amount: 100 },
    { id: 2, amount: 250 },
  ],
  tags: ['a', 'b'],
};

describe('inferSourceFields', () => {
  it('emits leaf paths plus the list path, descending into list elements', () => {
    expect(inferSourceFields(sample)).toEqual([
      'user.name',
      'user.age',
      'orders',
      'orders.id',
      'orders.amount',
      'tags',
    ]);
  });
});

describe('inferTargetFields', () => {
  it('expands list elements so the parent shows its children', () => {
    const target = { summary: { total: 0 }, customers: [{ id: 1, name: 'x' }] };
    expect(inferTargetFields(target)).toEqual(['summary.total', 'customers', 'customers.id', 'customers.name']);
  });
});

describe('inferStructure types', () => {
  it('reports a type for every leaf, object parent, and array parent', () => {
    const { types } = inferStructure(sample);
    expect(types).toMatchObject({
      user: 'object',
      'user.name': 'string',
      'user.age': 'number',
      orders: 'array',
      'orders.id': 'number',
      tags: 'array',
    });
  });

  it('flags a top-level array as rootArray', () => {
    const { rootArray, fields, types } = inferStructure([{ employeeId: 1, name: 'a' }]);
    expect(rootArray).toBe(true);
    expect(fields).toEqual(['employeeId', 'name']);
    expect(types).toEqual({ employeeId: 'number', name: 'string' });
  });
});
