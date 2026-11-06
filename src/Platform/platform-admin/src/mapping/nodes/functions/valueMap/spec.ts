import { Table2 } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { ValueMapNode } from './ValueMapNode';

export const valueMapSpec: NodeSpec = {
  type: 'valueMap',
  group: 'functions',
  label: 'Value-Map',
  icon: Table2,
  color: 'amber',
  component: ValueMapNode,
  defaultData: () => ({
    label: 'ValueMap',
    pairs: [{ key: '1', value: 'Jan' }, { key: '2', value: 'Feb' }],
    default: '',
  }),
  ports: { inputs: 'one', output: true },
  compile: (ctx) => {
    const pairs = (ctx.node.data.pairs as { key: string; value: string }[] | undefined) ?? [];
    const table: Record<string, string> = {};
    pairs.forEach((p) => {
      if (p.key !== '') table[p.key] = p.value;
    });

    const valueMap: Record<string, unknown> = { table };
    const input = ctx.inputExpr('in'); // path for a source field, nested expr otherwise
    if (input !== undefined) valueMap.input = input;

    const def = (ctx.node.data.default as string) ?? '';
    if (def !== '') valueMap.default = def;

    return { valueMap };
  },
};
