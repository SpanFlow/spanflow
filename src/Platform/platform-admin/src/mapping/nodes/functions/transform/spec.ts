import { Wand2 } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { literalExpr } from '../../compile-types';
import { TransformNode } from './TransformNode';
import { transformInfo } from './transforms';

export const transformSpec: NodeSpec = {
  type: 'transform',
  group: 'functions',
  label: 'Transform',
  icon: Wand2,
  color: 'cyan',
  component: TransformNode,
  defaultData: () => ({ label: 'Transform', function: 'concat', args: [{ value: '' }, { value: '' }] }),
  ports: { inputs: 'dynamic', output: true },
  helpFor: (data) => {
    const info = transformInfo(data.function || 'concat');
    return {
      summary: info ? `${info.label}: ${info.description}` : 'Applies a function to its arguments.',
      details: [
        'Each argument is a path (e.g. input.firstName), a quoted literal, or a wired node.',
        'Add or remove arguments to match what the chosen function expects.',
      ],
      example: info ? { caption: 'Example', code: info.example } : undefined,
    };
  },
  compile: (ctx) => {
    const fn = (ctx.node.data.function as string) || 'concat';
    const rawArgs = (ctx.node.data.args as { value: string }[] | undefined) ?? [];
    // Each argument is a wired node's output when connected, else a literal.
    const args = rawArgs.map((arg, i) => {
      const wired = ctx.inputExpr(`a${i}`);
      return wired !== undefined ? wired : literalExpr(arg.value ?? '');
    });
    return { transform: { function: fn, args } };
  },
};
