import { Hash } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { literalExpr } from '../../compile-types';
import { ConstantNode } from './ConstantNode';

export const constantSpec: NodeSpec = {
  type: 'constant',
  group: 'logic',
  label: 'Constant',
  icon: Hash,
  color: 'sky',
  component: ConstantNode,
  defaultData: () => ({ label: 'Constant', value: '' }),
  ports: { inputs: 'none', output: true },
  compile: (ctx) => literalExpr((ctx.node.data.value as string) ?? ''),
};
