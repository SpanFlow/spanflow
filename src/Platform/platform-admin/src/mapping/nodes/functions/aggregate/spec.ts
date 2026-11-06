import { Calculator } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { toFromSelect } from '../../compile-types';
import { AggregateNode } from './AggregateNode';
import { aggregateInfo } from './aggregates';

export const aggregateSpec: NodeSpec = {
  type: 'aggregate',
  group: 'functions',
  label: 'Aggregate',
  icon: Calculator,
  color: 'emerald',
  component: AggregateNode,
  defaultData: () => ({ label: 'Aggregate', function: 'sum' }),
  ports: { inputs: 'one', output: true },
  helpFor: (data) => {
    const info = aggregateInfo(data.function || 'sum');
    return {
      summary: info
        ? `${info.label}: ${info.description}`
        : 'Folds a list of values down to a single value.',
      details: [
        'Wire a collection into the input, or leave it inside a Group By to fold that group.',
        'select: fold one field of each row (e.g. amount) instead of the rows themselves.',
        'items: the odd one out: it keeps the rows rather than folding them, so you get an array back.',
      ],
      example: info ? { caption: 'Example', code: info.example } : undefined,
    };
  },
  compile: (ctx) => {
    const fn = (ctx.node.data.function as string) || 'sum';
    const aggregate: Record<string, unknown> = { function: fn };

    // joinString carries a separator.
    const separator = ctx.node.data.separator as string | undefined;
    if (fn === 'joinString' && separator !== undefined && separator !== '') {
      aggregate.separator = separator;
    }

    const edge = ctx.incomingEdge('in');
    if (edge) {
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      // A source array is read directly as `from`; anything else (a function
      // parameter or another node) is folded as a nested `input` collection.
      if (upstream?.type === 'source' && edge.sourceHandle) {
        const { from, select } = toFromSelect(edge.sourceHandle);
        aggregate.from = from;
        if (select && fn !== 'count') aggregate.select = select;
      } else if (upstream) {
        aggregate.input = ctx.build(upstream, edge.sourceHandle);
      }
    }
    return { aggregate };
  },
};
