import { ArrowDownUp } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { SortNode } from './SortNode';
// no extra helpers needed; uses ctx.build / ctx.incomingEdge

export const sortSpec: NodeSpec = {
  type: 'sort',
  group: 'functions',
  label: 'Sort',
  icon: ArrowDownUp,
  color: 'teal',
  component: SortNode,
  defaultData: () => ({ label: 'Sort', keys: [{ by: '', order: 'asc' }] }),
  ports: { inputs: 'one', output: true },
  compile: (ctx) => {
    const rawKeys = (ctx.node.data.keys as { by: string; order: string }[] | undefined) ?? [];
    const keys = rawKeys.map((k) => {
      const key: Record<string, string> = { order: k.order === 'desc' ? 'desc' : 'asc' };
      if (k.by) key.by = k.by;
      return key;
    });

    const sort: Record<string, unknown> = { keys };
    const edge = ctx.incomingEdge('in');
    if (edge) {
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      if (upstream) {
        const expr = ctx.build(upstream, edge.sourceHandle);
        // A source field resolves to a path string; anything else is a nested expr.
        if (typeof expr === 'string') sort.from = expr;
        else sort.input = expr;
      }
    }
    return { sort };
  },
};
