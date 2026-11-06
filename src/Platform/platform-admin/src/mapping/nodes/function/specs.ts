import { CircleDot, CornerDownLeft, FunctionSquare } from 'lucide-react';
import type { NodeSpec } from '../base';
import { ParamNode, ReturnNode, FunctionCallNode } from './FunctionNodes';

// param / functionCall are inlined by the compiler (buildExpr special-cases
// them); return is a structural sink. Their compile() is never called.
const noExpr = () => null;

export const paramSpec: NodeSpec = {
  type: 'param',
  group: 'function',
  label: 'Parameter',
  icon: CircleDot,
  color: 'amber',
  component: ParamNode,
  defaultData: () => ({ label: 'Parameter', name: 'param' }),
  ports: { inputs: 'none', output: true },
  compile: noExpr,
};

export const returnSpec: NodeSpec = {
  type: 'return',
  group: 'function',
  label: 'Return',
  icon: CornerDownLeft,
  color: 'emerald',
  component: ReturnNode,
  defaultData: () => ({ label: 'Return', fields: ['value'] }),
  ports: { inputs: 'perField', output: false },
  compile: noExpr,
};

export const functionCallSpec: NodeSpec = {
  type: 'functionCall',
  group: 'function',
  label: 'Function',
  icon: FunctionSquare,
  color: 'fuchsia',
  component: FunctionCallNode,
  defaultData: () => ({ label: 'Function' }),
  ports: { inputs: 'dynamic', output: true },
  compile: noExpr,
};
