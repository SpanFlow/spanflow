import type { Edge, Node } from '@xyflow/react';

// Ready-made example functions users can load to see how a custom function is
// built (Parameter nodes → logic → Return) and then drop as a call anywhere.

export interface FunctionExample {
  name: string;
  description: string;
  /** Sample parameter values (a JSON object) so the function runs out of the box. */
  sampleInput: string;
  nodes: Node[];
  edges: Edge[];
}

const param = (id: string, name: string, y: number): Node =>
  ({ id, type: 'param', position: { x: 40, y }, data: { label: 'Parameter', name } } as Node);

const ret = (x: number, fields: string[] = ['value']): Node =>
  ({ id: 'ret', type: 'return', position: { x, y: 120 }, data: { label: 'Return', fields } } as Node);

const transform = (id: string, x: number, y: number, fn: string, args: { value: string }[]): Node =>
  ({ id, type: 'transform', position: { x, y }, data: { label: 'Transform', function: fn, args } } as Node);

const wire = (source: string, sourceHandle: string, target: string, targetHandle: string): Edge =>
  ({ id: `${source}-${target}-${targetHandle}`, source, sourceHandle, target, targetHandle } as Edge);

export const FUNCTION_EXAMPLES: FunctionExample[] = [
  {
    name: 'fullName',
    description: 'fullName(first, last) → "first last"',
    sampleInput: '{\n  "first": "Ada",\n  "last": "Lovelace"\n}',
    nodes: [
      param('p1', 'first', 60),
      param('p2', 'last', 180),
      transform('t', 320, 110, 'concat', [{ value: '' }, { value: ' ' }, { value: '' }]),
      ret(580),
    ],
    edges: [wire('p1', 'out', 't', 'a0'), wire('p2', 'out', 't', 'a2'), wire('t', 'out', 'ret', 'value')],
  },
  {
    name: 'greeting',
    description: 'greeting(name) → "Hello, name!"',
    sampleInput: '{\n  "name": "Ada"\n}',
    nodes: [
      param('p1', 'name', 110),
      transform('t', 320, 110, 'concat', [{ value: 'Hello, ' }, { value: '' }, { value: '!' }]),
      ret(580),
    ],
    edges: [wire('p1', 'out', 't', 'a1'), wire('t', 'out', 'ret', 'value')],
  },
  {
    name: 'shout',
    description: 'shout(text) → UPPERCASE',
    sampleInput: '{\n  "text": "hello"\n}',
    nodes: [param('p1', 'text', 110), transform('t', 320, 110, 'upper', [{ value: '' }]), ret(560)],
    edges: [wire('p1', 'out', 't', 'a0'), wire('t', 'out', 'ret', 'value')],
  },
  {
    // A LIST parameter: `items` is fed into an aggregate.
    name: 'total',
    description: 'total(items) → sum of a list of numbers',
    sampleInput: '{\n  "items": [10, 20, 30]\n}',
    nodes: [
      param('p1', 'items', 110),
      { id: 'agg', type: 'aggregate', position: { x: 320, y: 110 }, data: { label: 'Aggregate', function: 'sum' } } as Node,
      ret(560),
    ],
    edges: [wire('p1', 'out', 'agg', 'in'), wire('agg', 'out', 'ret', 'value')],
  },
  {
    // An ARRAY return: a list parameter sorted, returned as the value.
    name: 'sortDesc',
    description: 'sortDesc(items) → the list sorted high → low',
    sampleInput: '{\n  "items": [10, 30, 20]\n}',
    nodes: [
      param('p1', 'items', 110),
      { id: 'srt', type: 'sort', position: { x: 320, y: 110 }, data: { label: 'Sort', keys: [{ by: '', order: 'desc' }] } } as Node,
      ret(560),
    ],
    edges: [wire('p1', 'out', 'srt', 'in'), wire('srt', 'out', 'ret', 'value')],
  },
  {
    // An OBJECT return: the Return node has multiple fields.
    name: 'person',
    description: 'person(first, last) → { fullName, lastName }',
    sampleInput: '{\n  "first": "Ada",\n  "last": "Lovelace"\n}',
    nodes: [
      param('p1', 'first', 60),
      param('p2', 'last', 180),
      transform('t', 320, 60, 'concat', [{ value: '' }, { value: ' ' }, { value: '' }]),
      ret(600, ['fullName', 'lastName']),
    ],
    edges: [
      wire('p1', 'out', 't', 'a0'),
      wire('p2', 'out', 't', 'a2'),
      wire('t', 'out', 'ret', 'fullName'),
      wire('p2', 'out', 'ret', 'lastName'),
    ],
  },
];
