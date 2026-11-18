import { describe, expect, it } from 'vitest';
import type { Node } from '@xyflow/react';
import { graphFormats, inputFormatOf, isPlainJson, optionsFor, type FormatInfo } from './formats';
import { compileGraphToDsl } from '../nodes/compile';
import { dslToGraph } from '../nodes/decompile';

const node = (type: string, data: Record<string, unknown> = {}): Node => ({ id: type, type, position: { x: 0, y: 0 }, data });

describe('graph formats (E73)', () => {
  it('reads the input format from the Source node and the output format from the Target node', () => {
    const nodes = [
      node('source', { format: { type: 'xml', options: { arrays: ['Line'] } } }),
      node('target', { format: { type: 'text', options: { mode: 'delimited', header: undefined } } }),
    ];

    expect(graphFormats(nodes)).toEqual({
      input: { type: 'xml', options: { arrays: ['Line'] } },
      output: { type: 'text', options: { mode: 'delimited' } },
    });
    expect(inputFormatOf(nodes).type).toBe('xml');
  });

  it('leaves plain JSON out, so a graph without formats compiles as before', () => {
    expect(graphFormats([node('source'), node('target', { format: { type: 'json' } })])).toBeUndefined();
    expect(isPlainJson({ type: 'json', options: { indent: true } })).toBe(false);
    expect(compileGraphToDsl([node('source'), node('target')], [])).not.toHaveProperty('formats');
  });

  it('compiles formats into the definition and puts them back on the nodes when decompiled', () => {
    const nodes = [
      node('source', { label: 'Source', fields: ['id'], format: { type: 'edi' } }),
      node('target', { label: 'Target', fields: ['id'], format: { type: 'xml', options: { root: 'Order' } } }),
    ];
    const edges = [{ id: 'e1', source: 'source', sourceHandle: 'id', target: 'target', targetHandle: 'id' }];

    const dsl = compileGraphToDsl(nodes, edges);
    expect(dsl.formats).toEqual({ input: { type: 'edi' }, output: { type: 'xml', options: { root: 'Order' } } });

    const back = dslToGraph(dsl);
    expect(back.nodes.find((n) => n.type === 'source')?.data.format).toEqual({ type: 'edi' });
    expect(back.nodes.find((n) => n.type === 'target')?.data.format).toEqual({ type: 'xml', options: { root: 'Order' } });
  });

  it('shows each side only the options read in its direction', () => {
    const xml: FormatInfo = {
      type: 'xml',
      contentType: 'application/xml',
      options: [
        { name: 'arrays', kind: 'stringList', use: 'decodeOnly', description: '' },
        { name: 'indent', kind: 'bool', use: 'encodeOnly', description: '' },
        { name: 'root', kind: 'string', use: 'both', description: '' },
      ],
    };

    expect(optionsFor(xml, 'input').map((o) => o.name)).toEqual(['arrays', 'root']);
    expect(optionsFor(xml, 'output').map((o) => o.name)).toEqual(['indent', 'root']);
  });
});
