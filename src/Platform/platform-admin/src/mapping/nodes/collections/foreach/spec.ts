import { Repeat } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { ForeachNode } from './ForeachNode';

export const foreachSpec: NodeSpec = {
  type: 'foreach',
  group: 'collections',
  label: 'For Each',
  icon: Repeat,
  color: 'violet',
  component: ForeachNode,
  defaultData: () => ({ label: 'Foreach', rows: [{ name: '', from: '' }] }),
  ports: { inputs: 'one', output: true },
  help: {
    summary: 'Builds one object per item in a list: the array-to-array workhorse.',
    details: [
      'Each row maps an output field to a path read from the CURRENT item (e.g. id, amount).',
      'Leave the from box empty to reuse the output name as the source field.',
      "Inside a Group By, point it at group[] to project that group's own rows.",
    ],
    example: {
      caption: 'Rename each order line',
      code: `{
  "target": "lines",
  "type": "foreach",
  "from": "input.orders[]",
  "map": { "orderId": "id", "value": "amount" }
}`,
    },
  },
  compile: (ctx) => {
    const rows = (ctx.node.data.rows as { name: string; from: string }[] | undefined) ?? [];
    const map: Record<string, string> = {};
    // A blank "from" maps the output field to a same-named item field.
    rows.forEach((r) => {
      if (r.name) map[r.name] = r.from || r.name;
    });

    const edge = ctx.incomingEdge('in');
    if (edge) {
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      if (upstream?.type === 'source' && edge.sourceHandle) {
        return { foreach: `input.${edge.sourceHandle}[]`, map };
      }
      if (upstream) {
        // A wired input (function parameter / another node) → nested collection.
        return { foreachInput: ctx.build(upstream, edge.sourceHandle), map };
      }
    }
    return { foreach: '$', map };
  },
};
