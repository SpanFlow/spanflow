import { Layers } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { GroupByNode } from './GroupByNode';

export const groupBySpec: NodeSpec = {
  type: 'groupBy',
  group: 'collections',
  label: 'Group By',
  icon: Layers,
  color: 'lime',
  component: GroupByNode,
  defaultData: () => ({ label: 'GroupBy', key: '', rows: [{ name: 'key', agg: 'key', field: '' }] }),
  // `in` (the collection) plus two optional Where ports: `where` and `having`.
  ports: { inputs: 'dynamic', output: true },
  // The filter ports take a Where node and nothing else.
  accepts: { where: ['where'], having: ['where'] },
  help: {
    summary:
      'Groups a collection by a key and emits one object per group. Each output field is either the group key, an aggregate over the group, or the group rows themselves.',
    details: [
      'key: the field to group on. An output row set to the "key" aggregate returns that group value.',
      'items: keeps the rows instead of collapsing them. Leave "of" blank for whole objects, or name a field to get an array of just that field.',
      'group[]: inside this node, any nested expression can refer to the group\'s own rows, so you can foreach/filter/sort them.',
      'where (port, optional): connect a Where node to drop rows BEFORE grouping. It names a row\'s field (e.g. status). Excluded rows never reach the key, the aggregates, or items.',
      'having (port, optional): connect a Where node to drop whole groups AFTER the aggregates are computed. It names an output field above (e.g. total, orderCount).',
      'Both ports are optional. Leave them empty and every row is grouped, exactly as before.',
    ],
    example: {
      caption: 'Completed orders per customer, keeping only customers over 100',
      code: `{
  "target": "customers",
  "type": "groupBy",
  "from": "input.orders[]",
  "key": "customerId",
  "where":  { "field": "status", "op": "eq", "value": "Completed" },
  "having": { "field": "total",  "op": "gt", "value": 100 },
  "map": {
    "customerId": "key",
    "total":  { "aggregate": { "function": "sum", "select": "amount" } },
    "orders": { "aggregate": { "function": "items" } }
  }
}`,
    },
  },
  compile: (ctx) => {
    const rows = (ctx.node.data.rows as { name: string; agg: string; field: string }[] | undefined) ?? [];
    const map: Record<string, unknown> = {};
    rows.forEach((r) => {
      if (!r.name) return;
      if (r.agg === 'key') {
        map[r.name] = 'key';
      } else if (r.agg === 'count') {
        map[r.name] = { aggregate: { function: 'count' } };
      } else {
        const aggregate: Record<string, unknown> = { function: r.agg };
        if (r.field) aggregate.select = r.field;
        map[r.name] = { aggregate };
      }
    });

    const groupBy: Record<string, unknown> = { key: (ctx.node.data.key as string) || '', map };
    const edge = ctx.incomingEdge('in');
    if (edge) {
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      if (upstream?.type === 'source' && edge.sourceHandle) {
        groupBy.from = `input.${edge.sourceHandle}[]`;
      } else if (upstream) {
        groupBy.input = ctx.build(upstream, edge.sourceHandle);
      }
    }

    // Both filters are optional: a Where node wired to the `where` port drops
    // rows before grouping, one wired to `having` drops whole groups after.
    // Unwired, the node groups everything exactly as it did before.
    const where = ctx.inputExpr('where');
    if (where) groupBy.where = where;

    const having = ctx.inputExpr('having');
    if (having) groupBy.having = having;

    return { groupBy };
  },
};
