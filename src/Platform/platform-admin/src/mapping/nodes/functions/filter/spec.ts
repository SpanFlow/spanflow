import { Filter } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { parseScalar } from '../../compile-types';
import { FilterNode } from './FilterNode';

export const filterSpec: NodeSpec = {
  type: 'filter',
  group: 'collections',
  label: 'Filter',
  icon: Filter,
  color: 'orange',
  component: FilterNode,
  defaultData: () => ({ label: 'Filter', field: '', op: 'eq', value: '' }),
  ports: { inputs: 'one', output: true },
  help: {
    summary: 'Keeps only the items of a list that match a condition, and drops the rest.',
    details: [
      'Wire the list into the input; the field names a field of each ROW (e.g. status).',
      'The output is always a list: possibly empty, never a bare object.',
      'For several rules combined with AND/OR, use a Where node on a node that accepts one.',
    ],
    example: {
      caption: 'Keep completed orders',
      code: `{
  "target": "completed",
  "type": "filter",
  "from": "input.orders[]",
  "where": { "field": "status", "op": "eq", "value": "Completed" }
}`,
    },
  },
  compile: (ctx) => {
    const where: Record<string, unknown> = {
      field: (ctx.node.data.field as string) || '',
      op: (ctx.node.data.op as string) || 'eq',
      value: parseScalar((ctx.node.data.value as string) ?? ''),
    };

    const filter: Record<string, unknown> = { where };
    const edge = ctx.incomingEdge('in');
    if (edge) {
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      if (upstream) {
        const expr = ctx.build(upstream, edge.sourceHandle);
        if (typeof expr === 'string') filter.from = expr;
        else filter.input = expr;
      }
    }
    return { filter };
  },
};
