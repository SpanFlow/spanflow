import { Database } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { SourceNode } from './SourceNode';

export const sourceSpec: NodeSpec = {
  type: 'source',
  group: 'io',
  label: 'Source',
  icon: Database,
  color: 'indigo',
  component: SourceNode,
  defaultData: () => ({ label: 'Source', fields: ['new_field'] }),
  ports: { inputs: 'none', output: true },
  // The value is the connected field, referenced as a scalar path.
  compile: (ctx) => `input.${ctx.outHandle}`,
};
