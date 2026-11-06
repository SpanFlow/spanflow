import { BoxSelect } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { TargetNode } from './TargetNode';

export const targetSpec: NodeSpec = {
  type: 'target',
  group: 'io',
  label: 'Target',
  icon: BoxSelect,
  color: 'purple',
  component: TargetNode,
  defaultData: () => ({ label: 'Target', fields: ['totalAmount'] }),
  ports: { inputs: 'perField', output: false },
  // A target is a consumer; it never acts as an upstream expression.
  compile: () => null,
};
