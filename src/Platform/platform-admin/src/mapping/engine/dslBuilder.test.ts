import { describe, it, expect } from 'vitest';
import type { Edge, Node } from '@xyflow/react';
import { compileGraphToDsl } from './dslBuilder';

let counter = 0;
const reset = () => { counter = 0; };

function node(type: string, data: Record<string, unknown> = {}): Node {
  return { id: `${type}-${++counter}`, type, position: { x: 0, y: 0 }, data } as Node;
}

function edge(source: Node, target: Node, sourceHandle: string, targetHandle: string): Edge {
  return {
    id: `e-${++counter}`,
    source: source.id,
    target: target.id,
    sourceHandle,
    targetHandle,
  } as Edge;
}

const mappings = (nodes: Node[], edges: Edge[]) => compileGraphToDsl(nodes, edges).target.mappings ?? [];

describe('compileGraphToDsl', () => {
  it('produces an empty object target for an empty graph', () => {
    reset();
    expect(compileGraphToDsl([], [])).toEqual({ target: { type: 'object', mappings: [] } });
  });

  describe('aggregate', () => {
    it('compiles source(field).array -> aggregate(sum) -> target with from + select', () => {
      reset();
      const src = node('source', { fields: ['orders.amount'] });
      const agg = node('aggregate', { function: 'sum' });
      const tgt = node('target', { fields: ['totalAmount'] });
      const edges = [edge(src, agg, 'orders.amount', 'in'), edge(agg, tgt, 'out', 'totalAmount')];

      expect(mappings([src, agg, tgt], edges)).toEqual([
        { target: 'totalAmount', type: 'aggregate', function: 'sum', from: 'input.orders[]', select: 'amount' },
      ]);
    });

    it('omits select for count', () => {
      reset();
      const src = node('source', { fields: ['orders.amount'] });
      const agg = node('aggregate', { function: 'count' });
      const tgt = node('target');
      const edges = [edge(src, agg, 'orders.amount', 'in'), edge(agg, tgt, 'out', 'n')];

      expect(mappings([src, agg, tgt], edges)).toEqual([
        { target: 'n', type: 'aggregate', function: 'count', from: 'input.orders[]' },
      ]);
    });

    it('emits separator for joinString', () => {
      reset();
      const src = node('source', { fields: ['tags'] });
      const agg = node('aggregate', { function: 'joinString', separator: '-' });
      const tgt = node('target');
      const edges = [edge(src, agg, 'tags', 'in'), edge(agg, tgt, 'out', 'joined')];

      expect(mappings([src, agg, tgt], edges)).toEqual([
        { target: 'joined', type: 'aggregate', function: 'joinString', from: 'input.tags[]', separator: '-' },
      ]);
    });

    it('treats a single-segment field as the array itself', () => {
      reset();
      const src = node('source', { fields: ['values'] });
      const agg = node('aggregate', { function: 'min' });
      const tgt = node('target');
      const edges = [edge(src, agg, 'values', 'in'), edge(agg, tgt, 'out', 'r')];

      expect(mappings([src, agg, tgt], edges)).toEqual([
        { target: 'r', type: 'aggregate', function: 'min', from: 'input.values[]' },
      ]);
    });
  });

  describe('plain field copy', () => {
    it('compiles source -> target directly to a `from` path', () => {
      reset();
      const src = node('source', { fields: ['user.name'] });
      const tgt = node('target');
      const edges = [edge(src, tgt, 'user.name', 'name')];

      expect(mappings([src, tgt], edges)).toEqual([{ target: 'name', type: 'fieldCopy', path: 'input.user.name' }]);
    });
  });

  describe('value-map', () => {
    it('compiles a source-less value-map to table + default only', () => {
      reset();
      const vm = node('valueMap', { pairs: [{ key: '1', value: 'Jan' }], default: 'NA' });
      const tgt = node('target');
      const edges = [edge(vm, tgt, 'out', 'month')];

      expect(mappings([vm, tgt], edges)).toEqual([
        { target: 'month', type: 'valueMap', table: { '1': 'Jan' }, default: 'NA' },
      ]);
    });

    it('wires a source field in as a path input and skips empty keys/default', () => {
      reset();
      const src = node('source', { fields: ['code'] });
      const vm = node('valueMap', { pairs: [{ key: 'A', value: 'Active' }, { key: '', value: 'skip' }], default: '' });
      const tgt = node('target');
      const edges = [edge(src, vm, 'code', 'in'), edge(vm, tgt, 'out', 'label')];

      expect(mappings([src, vm, tgt], edges)).toEqual([
        { target: 'label', type: 'valueMap', table: { A: 'Active' }, input: 'input.code' },
      ]);
    });

    it('nests an aggregate as the value-map input (chaining)', () => {
      reset();
      const src = node('source', { fields: ['orders.id'] });
      const agg = node('aggregate', { function: 'min' });
      const vm = node('valueMap', { pairs: [{ key: '1', value: 'Jan' }], default: 'NA' });
      const tgt = node('target');
      const edges = [
        edge(src, agg, 'orders.id', 'in'),
        edge(agg, vm, 'out', 'in'),
        edge(vm, tgt, 'out', 'monthName'),
      ];

      expect(mappings([src, agg, vm, tgt], edges)).toEqual([
        {
          target: 'monthName',
          type: 'valueMap',
          table: { '1': 'Jan' },
          default: 'NA',
          input: { aggregate: { function: 'min', from: 'input.orders[]', select: 'id' } },
        },
      ]);
    });
  });

  describe('sort', () => {
    it('compiles a source collection -> sort(keys) with from', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const srt = node('sort', { keys: [{ by: 'amount', order: 'desc' }] });
      const tgt = node('target');
      const edges = [edge(src, srt, 'orders', 'in'), edge(srt, tgt, 'out', 'sorted')];

      expect(mappings([src, srt, tgt], edges)).toEqual([
        { target: 'sorted', type: 'sort', from: 'input.orders', keys: [{ by: 'amount', order: 'desc' }] },
      ]);
    });

    it('omits an empty `by` (sort by item) and defaults order to asc', () => {
      reset();
      const src = node('source', { fields: ['values'] });
      const srt = node('sort', { keys: [{ by: '', order: 'asc' }] });
      const tgt = node('target');
      const edges = [edge(src, srt, 'values', 'in'), edge(srt, tgt, 'out', 'r')];

      expect(mappings([src, srt, tgt], edges)).toEqual([
        { target: 'r', type: 'sort', from: 'input.values', keys: [{ order: 'asc' }] },
      ]);
    });
  });

  describe('transform', () => {
    it('mixes a literal arg with a wired input arg', () => {
      reset();
      const src = node('source', { fields: ['firstName'] });
      const t = node('transform', { function: 'concat', args: [{ value: 'Hi ' }, { value: '' }] });
      const tgt = node('target');
      const edges = [edge(src, t, 'firstName', 'a1'), edge(t, tgt, 'out', 'greeting')];

      expect(mappings([src, t, tgt], edges)).toEqual([
        { target: 'greeting', type: 'transform', function: 'concat', args: ["'Hi '", 'input.firstName'] },
      ]);
    });

    it('passes numeric literals through for add', () => {
      reset();
      const t = node('transform', { function: 'add', args: [{ value: '2' }, { value: '3' }] });
      const tgt = node('target');
      const edges = [edge(t, tgt, 'out', 'r')];

      expect(mappings([t, tgt], edges)).toEqual([
        { target: 'r', type: 'transform', function: 'add', args: [2, 3] },
      ]);
    });
  });

  describe('filter', () => {
    it('compiles a source collection -> filter(where) with from', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const f = node('filter', { field: 'status', op: 'eq', value: 'Completed' });
      const tgt = node('target');
      const edges = [edge(src, f, 'orders', 'in'), edge(f, tgt, 'out', 'done')];

      expect(mappings([src, f, tgt], edges)).toEqual([
        { target: 'done', type: 'filter', where: { field: 'status', op: 'eq', value: 'Completed' }, from: 'input.orders' },
      ]);
    });
  });

  describe('foreach', () => {
    it('compiles a source collection -> foreach with a projection map', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const fe = node('foreach', { rows: [{ name: 'id', from: 'id' }, { name: 'label', from: '$' }] });
      const tgt = node('target');
      const edges = [edge(src, fe, 'orders', 'in'), edge(fe, tgt, 'out', 'items')];

      expect(mappings([src, fe, tgt], edges)).toEqual([
        { target: 'items', type: 'foreach', from: 'input.orders[]', map: { id: 'id', label: '$' } },
      ]);
    });

    it('maps a blank "from" to a same-named field', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const fe = node('foreach', { rows: [{ name: 'amount', from: '' }] });
      const tgt = node('target');
      const edges = [edge(src, fe, 'orders', 'in'), edge(fe, tgt, 'out', 'items')];

      expect(mappings([src, fe, tgt], edges)).toEqual([
        { target: 'items', type: 'foreach', from: 'input.orders[]', map: { amount: 'amount' } },
      ]);
    });
  });

  describe('groupBy', () => {
    it('compiles key + per-group aggregates', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const gb = node('groupBy', {
        key: 'customerId',
        rows: [
          { name: 'customerId', agg: 'key', field: '' },
          { name: 'total', agg: 'sum', field: 'amount' },
          { name: 'orders', agg: 'count', field: '' },
        ],
      });
      const tgt = node('target');
      const edges = [edge(src, gb, 'orders', 'in'), edge(gb, tgt, 'out', 'customers')];

      expect(mappings([src, gb, tgt], edges)).toEqual([
        {
          target: 'customers',
          type: 'groupBy',
          from: 'input.orders[]',
          key: 'customerId',
          map: {
            customerId: 'key',
            total: { aggregate: { function: 'sum', select: 'amount' } },
            orders: { aggregate: { function: 'count' } },
          },
        },
      ]);
    });

    it('folds a wired Where node into where (pre-filter) and having (post-filter)', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const gb = node('groupBy', {
        key: 'customerId',
        rows: [
          { name: 'customerId', agg: 'key', field: '' },
          { name: 'total', agg: 'sum', field: 'amount' },
        ],
      });
      // Pre-filter: two rules combined with OR.
      const pre = node('where', {
        match: 'any',
        clauses: [
          { field: 'status', op: 'eq', value: 'Completed' },
          { field: 'amount', op: 'gte', value: '100' },
        ],
      });
      // Post-filter: a lone rule needs no all/any wrapper.
      const post = node('where', { match: 'all', clauses: [{ field: 'total', op: 'gt', value: '500' }] });
      const tgt = node('target');
      const edges = [
        edge(src, gb, 'orders', 'in'),
        edge(pre, gb, 'out', 'where'),
        edge(post, gb, 'out', 'having'),
        edge(gb, tgt, 'out', 'customers'),
      ];

      expect(mappings([src, gb, pre, post, tgt], edges)).toEqual([
        {
          target: 'customers',
          type: 'groupBy',
          from: 'input.orders[]',
          key: 'customerId',
          map: {
            customerId: 'key',
            total: { aggregate: { function: 'sum', select: 'amount' } },
          },
          where: {
            any: [
              { field: 'status', op: 'eq', value: 'Completed' },
              { field: 'amount', op: 'gte', value: 100 },
            ],
          },
          having: { field: 'total', op: 'gt', value: 500 },
        },
      ]);
    });

    it('omits where/having entirely when no Where node is wired', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const gb = node('groupBy', { key: 'customerId', rows: [{ name: 'customerId', agg: 'key', field: '' }] });
      const tgt = node('target');
      const edges = [edge(src, gb, 'orders', 'in'), edge(gb, tgt, 'out', 'customers')];

      const [rule] = mappings([src, gb, tgt], edges) as any[];
      expect(rule).not.toHaveProperty('where');
      expect(rule).not.toHaveProperty('having');
    });

    it('compiles an items row into the group members, whole or projected', () => {
      reset();
      const src = node('source', { fields: ['orders'] });
      const gb = node('groupBy', {
        key: 'customerId',
        rows: [
          { name: 'customerId', agg: 'key', field: '' },
          { name: 'orders', agg: 'items', field: '' },
          { name: 'amounts', agg: 'items', field: 'amount' },
        ],
      });
      const tgt = node('target');
      const edges = [edge(src, gb, 'orders', 'in'), edge(gb, tgt, 'out', 'customers')];

      expect(mappings([src, gb, tgt], edges)).toEqual([
        {
          target: 'customers',
          type: 'groupBy',
          from: 'input.orders[]',
          key: 'customerId',
          map: {
            customerId: 'key',
            orders: { aggregate: { function: 'items' } },
            amounts: { aggregate: { function: 'items', select: 'amount' } },
          },
        },
      ]);
    });
  });

  describe('join', () => {
    it('compiles two wired source collections + keys', () => {
      reset();
      const orders = node('source', { fields: ['orders'] });
      const customers = node('source', { fields: ['customers'] });
      const j = node('join', { leftKey: 'customerId', rightKey: 'custId' });
      const tgt = node('target');
      const edges = [
        edge(orders, j, 'orders', 'left'),
        edge(customers, j, 'customers', 'right'),
        edge(j, tgt, 'out', 'rows'),
      ];

      expect(mappings([orders, customers, j, tgt], edges)).toEqual([
        {
          target: 'rows',
          type: 'join',
          leftKey: 'customerId', rightKey: 'custId', left: 'input.orders[]', right: 'input.customers[]',
        },
      ]);
    });
  });

  describe('exception', () => {
    it('compiles a wired source value + condition + message', () => {
      reset();
      const src = node('source', { fields: ['amount'] });
      const ex = node('exception', { op: 'lt', value: '0', message: 'Amount cannot be negative' });
      const tgt = node('target');
      const edges = [edge(src, ex, 'amount', 'in'), edge(ex, tgt, 'out', 'checked')];

      expect(mappings([src, ex, tgt], edges)).toEqual([
        { target: 'checked', type: 'exception', op: 'lt', value: 0, message: 'Amount cannot be negative', from: 'input.amount' },
      ]);
    });
  });

  describe('constant', () => {
    it('compiles a numeric constant to a coalesce transform', () => {
      reset();
      const c = node('constant', { value: '42' });
      const tgt = node('target');
      const edges = [edge(c, tgt, 'out', 'n')];

      expect(mappings([c, tgt], edges)).toEqual([
        { target: 'n', type: 'transform', function: 'coalesce', args: [42] },
      ]);
    });

    it('compiles a string constant to a quoted literal via `from`', () => {
      reset();
      const c = node('constant', { value: 'Win' });
      const tgt = node('target');
      const edges = [edge(c, tgt, 'out', 's')];

      expect(mappings([c, tgt], edges)).toEqual([{ target: 's', type: 'fieldCopy', path: "'Win'" }]);
    });
  });

  describe('condition', () => {
    it('compiles a single clause to a leaf with nested input + literal then/else', () => {
      reset();
      const src = node('source', { fields: ['orders.id'] });
      const agg = node('aggregate', { function: 'min' });
      const cond = node('condition', { combinator: 'all', clauses: [{ op: 'eq', value: '1' }], then: 'Win', else: 'Lose' });
      const tgt = node('target');
      const edges = [
        edge(src, agg, 'orders.id', 'in'),
        edge(agg, cond, 'out', 'c0'),
        edge(cond, tgt, 'out', 'outcome'),
      ];

      expect(mappings([src, agg, cond, tgt], edges)).toEqual([
        {
          target: 'outcome',
          type: 'condition',
          if: { op: 'eq', input: { aggregate: { function: 'min', from: 'input.orders[]', select: 'id' } }, value: 1 },
          then: "'Win'",
          else: "'Lose'",
        },
      ]);
    });

    it('combines multiple clauses with the chosen combinator', () => {
      reset();
      const a = node('source', { fields: ['x'] });
      const aggA = node('aggregate', { function: 'min' });
      const b = node('source', { fields: ['y'] });
      const aggB = node('aggregate', { function: 'max' });
      const cond = node('condition', {
        combinator: 'all',
        clauses: [{ op: 'eq', value: '1' }, { op: 'eq', value: '0' }],
        then: 'Win', else: 'Lose',
      });
      const tgt = node('target');
      const edges = [
        edge(a, aggA, 'x', 'in'),
        edge(aggA, cond, 'out', 'c0'),
        edge(b, aggB, 'y', 'in'),
        edge(aggB, cond, 'out', 'c1'),
        edge(cond, tgt, 'out', 'r'),
      ];

      const rules = mappings([a, aggA, b, aggB, cond, tgt], edges);
      expect(rules[0]).toEqual({
        target: 'r',
        type: 'condition',
        if: {
          all: [
            { op: 'eq', input: { aggregate: { function: 'min', from: 'input.x[]' } }, value: 1 },
            { op: 'eq', input: { aggregate: { function: 'max', from: 'input.y[]' } }, value: 0 },
          ],
        },
        then: "'Win'",
        else: "'Lose'",
      });
    });

    it('uses a wired node for the then branch instead of the literal', () => {
      reset();
      const cnt = node('source', { fields: ['orders.amount'] });
      const agg = node('aggregate', { function: 'sum' });
      const cond = node('condition', { combinator: 'all', clauses: [{ op: 'gte', value: '2' }], then: 'fallback', else: 'none' });
      const tgt = node('target');
      const edges = [
        edge(agg, cond, 'out', 'then'),
        edge(cnt, agg, 'orders.amount', 'in'),
        edge(cond, tgt, 'out', 'r'),
      ];

      const rules = mappings([cnt, agg, cond, tgt], edges);
      expect(rules[0]).toMatchObject({
        target: 'r',
        type: 'condition',
        then: { aggregate: { function: 'sum', from: 'input.orders[]', select: 'amount' } },
        else: "'none'",
      });
    });
  });

  describe('list -> list', () => {
    it('compiles a source array mapped to a target array as a foreach', () => {
      reset();
      const src = node('source', { fields: ['orders', 'orders.id', 'orders.amount'], lists: ['orders'] });
      const tgt = node('target', { fields: ['orders', 'orders.id', 'orders.amount'], lists: ['orders'] });
      const edges = [
        edge(src, tgt, 'orders', 'orders'),
        edge(src, tgt, 'orders.id', 'orders.id'),
        edge(src, tgt, 'orders.amount', 'orders.amount'),
      ];

      expect(mappings([src, tgt], edges)).toEqual([
        { target: 'orders', type: 'foreach', from: 'input.orders[]', map: { id: 'id', amount: 'amount' } },
      ]);
    });

    it('folds a function into the foreach map with item-relative paths', () => {
      reset();
      const src = node('source', { fields: ['person', 'person.id', 'person.fullName', 'person.department'], lists: ['person'] });
      const t = node('transform', { function: 'concat', args: [{ value: '' }, { value: ' - ' }, { value: '' }] });
      const tgt = node('target', { fields: ['person', 'person.employeeId', 'person.displayName'], lists: ['person'] });
      const edges = [
        edge(src, tgt, 'person', 'person'), // parent / iteration
        edge(src, tgt, 'person.id', 'person.employeeId'), // direct rename
        edge(src, t, 'person.fullName', 'a0'),
        edge(src, t, 'person.department', 'a2'),
        edge(t, tgt, 'out', 'person.displayName'), // function -> target child
      ];

      expect(mappings([src, t, tgt], edges)).toEqual([
        {
          target: 'person',
          type: 'foreach',
          from: 'input.person[]',
          map: {
            employeeId: 'id',
            displayName: { function: 'concat', args: ['fullName', "' - '", 'department'] },
          },
        },
      ]);
    });

    it('copies a primitive list when only the parent is wired', () => {
      reset();
      const src = node('source', { fields: ['tags'], lists: ['tags'] });
      const tgt = node('target', { fields: ['tags'], lists: ['tags'] });
      const edges = [edge(src, tgt, 'tags', 'tags')];

      expect(mappings([src, tgt], edges)).toEqual([
        { target: 'tags', type: 'fieldCopy', path: 'input.tags' },
      ]);
    });
  });

  describe('array-root target', () => {
    const src = () =>
      node('source', {
        fields: ['person', 'person.id', 'person.fullName', 'person.salary', 'person.department'],
        lists: ['person'],
      });

    it('renames each item into a top-level array', () => {
      reset();
      const s = src();
      const tgt = node('target', { fields: ['employeeId', 'name', 'annualSalary'], rootArray: true });
      const edges = [
        edge(s, tgt, 'person.id', 'employeeId'),
        edge(s, tgt, 'person.fullName', 'name'),
        edge(s, tgt, 'person.salary', 'annualSalary'),
      ];

      expect(compileGraphToDsl([s, tgt], edges).target).toEqual({
        type: 'array',
        from: 'input.person[]',
        map: { employeeId: 'id', name: 'fullName', annualSalary: 'salary' },
      });
    });

    it('resolves the source list from a $root parent edge (MapForce-style)', () => {
      reset();
      const s = src();
      const tgt = node('target', { fields: ['employeeId', 'name'], rootArray: true });
      const edges = [
        edge(s, tgt, 'person', '$root'), // parent array -> array-root
        edge(s, tgt, 'person.id', 'employeeId'),
        edge(s, tgt, 'person.fullName', 'name'),
      ];

      expect(compileGraphToDsl([s, tgt], edges).target).toEqual({
        type: 'array',
        from: 'input.person[]',
        map: { employeeId: 'id', name: 'fullName' },
      });
    });

    it('iterates a root-array source (input[]) into a root-array target', () => {
      reset();
      // Source is itself a top-level array: fields are top-level, rootArray set.
      const s = node('source', { fields: ['id', 'fullName', 'department'], lists: [], rootArray: true });
      const t = node('transform', { function: 'concat', args: [{ value: '' }, { value: ' - ' }, { value: '' }] });
      const tgt = node('target', { fields: ['employeeId', 'label'], rootArray: true });
      const edges = [
        edge(s, tgt, '$root', '$root'), // array -> array parent link
        edge(s, tgt, 'id', 'employeeId'),
        edge(s, t, 'fullName', 'a0'),
        edge(s, t, 'department', 'a2'),
        edge(t, tgt, 'out', 'label'),
      ];

      expect(compileGraphToDsl([s, t, tgt], edges).target).toEqual({
        type: 'array',
        from: 'input[]',
        map: {
          employeeId: 'id',
          label: { function: 'concat', args: ['fullName', "' - '", 'department'] },
        },
      });
    });

    it('folds a function into the array-root map (item-relative)', () => {
      reset();
      const s = src();
      const t = node('transform', { function: 'concat', args: [{ value: '' }, { value: ' - ' }, { value: '' }] });
      const tgt = node('target', { fields: ['displayName'], rootArray: true });
      const edges = [
        edge(s, t, 'person.fullName', 'a0'),
        edge(s, t, 'person.department', 'a2'),
        edge(t, tgt, 'out', 'displayName'),
      ];

      expect(compileGraphToDsl([s, t, tgt], edges).target).toEqual({
        type: 'array',
        from: 'input.person[]',
        map: { displayName: { function: 'concat', args: ['fullName', "' - '", 'department'] } },
      });
    });
  });

  describe('custom functions', () => {
    it('inlines a function call, substituting parameters with caller inputs', () => {
      reset();
      // Function fullName(first, last) = concat(first, ' ', last)
      const p1 = node('param', { name: 'first' });
      const p2 = node('param', { name: 'last' });
      const tr = node('transform', { function: 'concat', args: [{ value: '' }, { value: ' ' }, { value: '' }] });
      const ret = node('return', {});
      const fn = {
        id: 'fn1',
        name: 'fullName',
        params: ['first', 'last'],
        nodes: [p1, p2, tr, ret],
        edges: [edge(p1, tr, 'out', 'a0'), edge(p2, tr, 'out', 'a2'), edge(tr, ret, 'out', 'value')],
      };

      const src = node('source', { fields: ['firstName', 'lastName'] });
      const fc = node('functionCall', { functionId: 'fn1' });
      const tgt = node('target', { fields: ['displayName'] });
      const edges = [
        edge(src, fc, 'firstName', 'first'),
        edge(src, fc, 'lastName', 'last'),
        edge(fc, tgt, 'out', 'displayName'),
      ];

      const dsl = compileGraphToDsl([src, fc, tgt], edges, { fn1: fn } as any);
      expect(dsl.target.mappings).toEqual([
        { target: 'displayName', type: 'transform', function: 'concat', args: ['input.firstName', "' '", 'input.lastName'] },
      ]);
    });

    it('inlines an object-returning function into per-field rules', () => {
      reset();
      // point(x, y) → { a: x, b: y } (Return with two fields)
      const p1 = node('param', { name: 'x' });
      const p2 = node('param', { name: 'y' });
      const ret = node('return', { fields: ['a', 'b'] });
      const fn = { id: 'pt', name: 'point', params: ['x', 'y'], nodes: [p1, p2, ret], edges: [edge(p1, ret, 'out', 'a'), edge(p2, ret, 'out', 'b')] };

      const src = node('source', { fields: ['lat', 'lng'] });
      const fc = node('functionCall', { functionId: 'pt' });
      const tgt = node('target', { fields: ['pos'] });
      const edges = [edge(src, fc, 'lat', 'x'), edge(src, fc, 'lng', 'y'), edge(fc, tgt, 'out', 'pos')];

      const dsl = compileGraphToDsl([src, fc, tgt], edges, { pt: fn } as any);
      expect(dsl.target.mappings).toEqual([
        { target: 'pos.a', type: 'fieldCopy', path: 'input.lat' },
        { target: 'pos.b', type: 'fieldCopy', path: 'input.lng' },
      ]);
    });
  });

  it('emits one mapping per connected target field', () => {
    reset();
    const src = node('source', { fields: ['a', 'b'] });
    const tgt = node('target', { fields: ['x', 'y'] });
    const edges = [edge(src, tgt, 'a', 'x'), edge(src, tgt, 'b', 'y')];

    expect(mappings([src, tgt], edges)).toHaveLength(2);
  });
});
