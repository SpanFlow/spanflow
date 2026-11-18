import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PreviewPanel } from '../PreviewPanel';
import { useStore } from '../../store/useStore';
import { useFormatCatalog } from '../../formats/formatsApi';
import type { FormatInfo } from '../../formats/formats';

vi.mock('../../../lib/ufmappingApi', () => ({ ufmappingApi: { post: vi.fn(), get: vi.fn(), defaults: { baseURL: 'x' } } }));

const catalog: FormatInfo[] = [
  {
    type: 'xml',
    contentType: 'application/xml',
    options: [
      { name: 'arrays', kind: 'stringList', use: 'decodeOnly', description: 'Always lists.' },
      { name: 'xsd', kind: 'string', use: 'decodeOnly', description: 'The schema.' },
      { name: 'root', kind: 'string', use: 'both', description: 'Root.' },
    ],
  },
];

const node = (id: string, type: string, data: Record<string, unknown> = {}) => ({ id, type, position: { x: 0, y: 0 }, data });
const sourceFormat = () => useStore.getState().nodes.find((n) => n.type === 'source')!.data.format as any;

describe('XML options (E73-T10)', () => {
  beforeEach(() => {
    useFormatCatalog.setState({ formats: catalog, status: 'ready' });
    useStore.setState({
      nodes: [
        node('s', 'source', { label: 'Source', fields: [], format: { type: 'xml', options: { arrays: ['Note'] } } }),
        node('t', 'target', { label: 'Target', fields: [], format: { type: 'xml' } }),
      ] as never,
      edges: [],
      docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
      activeDocId: 'main',
      sampleInput: '<Order><Line><Tag/><Tag/></Line><Line/><Note/></Order>',
    });
  });

  const openInput = async () => {
    render(<PreviewPanel />);
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ })[0]);
  };

  it('fills the arrays from what the sample repeats, keeping what was there', async () => {
    await openInput();
    await userEvent.click(screen.getByRole('button', { name: 'Fill from sample' }));

    expect(sourceFormat().options.arrays).toEqual(['Note', '/Order/Line', '/Order/Line/Tag']);
    expect((screen.getByLabelText('arrays') as HTMLInputElement).value).toBe('Note, /Order/Line, /Order/Line/Tag');
  });

  it('says so when the sample is not XML yet', async () => {
    useStore.setState({ sampleInput: '<Order><Line>' });
    await openInput();
    await userEvent.click(screen.getByRole('button', { name: 'Fill from sample' }));
    expect(screen.getByText('The Sample Input is not well-formed XML yet.')).toBeTruthy();
    expect(sourceFormat().options.arrays).toEqual(['Note']);
  });

  it('reads an XSD from a file into the xsd option', async () => {
    await openInput();
    const xsd = '<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"/>';
    await userEvent.upload(screen.getByLabelText('Load an XSD file'), new File([xsd], 'order.xsd', { type: 'application/xml' }));

    expect(sourceFormat().options.xsd).toBe(xsd);
    expect((screen.getByLabelText('xsd') as HTMLTextAreaElement).value).toBe(xsd);
    expect(screen.getByText(`${xsd.length} characters of schema.`)).toBeTruthy();
  });

  it('the output side offers neither: both are for reading', async () => {
    render(<PreviewPanel />);
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ })[1]);
    const output = screen.getByTestId('format-picker-output');
    expect(output.textContent).not.toContain('Fill from sample');
    expect(output.textContent).not.toContain('Load .xsd');
  });
});
