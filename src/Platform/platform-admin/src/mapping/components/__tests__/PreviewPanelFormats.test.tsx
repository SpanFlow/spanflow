import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PreviewPanel } from '../PreviewPanel';
import { useStore } from '../../store/useStore';
import { useFormatCatalog } from '../../formats/formatsApi';
import type { FormatInfo } from '../../formats/formats';
import { ufmappingApi } from '../../../lib/ufmappingApi';

vi.mock('../../../lib/ufmappingApi', () => ({ ufmappingApi: { post: vi.fn(), get: vi.fn(), defaults: { baseURL: 'x' } } }));
const api = vi.mocked(ufmappingApi, true);

const catalog: FormatInfo[] = [
  { type: 'json', contentType: 'application/json', options: [{ name: 'indent', kind: 'bool', use: 'encodeOnly', description: 'Pretty output.' }] },
  {
    type: 'text',
    contentType: 'text/plain',
    options: [
      { name: 'mode', kind: 'string', use: 'both', description: 'whole, lines, delimited, pattern or fixed.' },
      { name: 'template', kind: 'string', use: 'encodeOnly', description: 'One line per row.' },
    ],
  },
  { type: 'xml', contentType: 'application/xml', options: [{ name: 'arrays', kind: 'stringList', use: 'decodeOnly', description: 'Always lists.' }] },
  { type: 'positional', contentType: 'text/plain', options: [{ name: 'layout', kind: 'object', use: 'both', description: 'The fields.' }] },
  { type: 'edi', contentType: 'text/plain', options: [] },
];

const node = (id: string, type: string, data: Record<string, unknown> = {}) => ({ id, type, position: { x: 0, y: 0 }, data });

const sourceData = () => useStore.getState().nodes.find((n) => n.type === 'source')!.data;
const targetData = () => useStore.getState().nodes.find((n) => n.type === 'target')!.data;

describe('PreviewPanel formats (E73-T7)', () => {
  beforeEach(() => {
    api.post.mockReset();
    api.get.mockReset();
    useFormatCatalog.setState({ formats: catalog, status: 'ready' });
    useStore.setState({
      nodes: [node('s', 'source', { label: 'Source', fields: ['sku'] }), node('t', 'target', { label: 'Target', fields: ['sku'] })] as never,
      edges: [],
      docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
      activeDocId: 'main',
      sampleInput: '{"sku":"A"}',
    });
  });

  it('a JSON-only mapping still runs through execute, with no format request', async () => {
    useFormatCatalog.setState({ formats: null, status: 'idle' });
    api.post.mockResolvedValue({ data: { sku: 'A' } });
    render(<PreviewPanel />);

    await userEvent.click(screen.getByTitle('Execute Engine'));

    await waitFor(() => expect(api.post).toHaveBeenCalledWith('/api/mapping/execute', expect.objectContaining({ inputObject: { sku: 'A' } })));
    expect(api.get).not.toHaveBeenCalled();
    expect(screen.queryByRole('tablist')).toBeNull();
  });

  it('picking an input and output format stores them on the Source and Target nodes', async () => {
    render(<PreviewPanel />);

    await userEvent.selectOptions(screen.getByLabelText('Input format'), 'xml');
    await userEvent.type(screen.getByLabelText('arrays'), 'Line, Item');
    await userEvent.selectOptions(screen.getByLabelText('Output format'), 'text');
    await userEvent.selectOptions(screen.getAllByLabelText('mode').at(-1)!, 'delimited');

    expect(sourceData().format).toEqual({ type: 'xml', options: { arrays: ['Line', 'Item'] } });
    expect(targetData().format).toEqual({ type: 'text', options: { mode: 'delimited' } });
    // Each side shows only the options read in its direction: template is for writing text.
    const input = screen.getByTestId('format-picker-input');
    expect(input.textContent).not.toContain('template');
  });

  it('the same option on both sides has its own field (XML in and XML out both have root)', async () => {
    useFormatCatalog.setState({
      formats: [{ type: 'xml', contentType: 'application/xml', options: [{ name: 'root', kind: 'string', use: 'both', description: '' }] }],
      status: 'ready',
    });
    render(<PreviewPanel />);

    await userEvent.selectOptions(screen.getByLabelText('Input format'), 'xml');
    await userEvent.selectOptions(screen.getByLabelText('Output format'), 'xml');
    const [inRoot, outRoot] = screen.getAllByLabelText('root');
    expect(inRoot).not.toBe(outRoot);

    await userEvent.type(outRoot, 'Employee');
    expect(targetData().format).toEqual({ type: 'xml', options: { root: 'Employee' } });
    expect(sourceData().format).toEqual({ type: 'xml' });
  });

  it('a yes/no option is default, yes or no, so an option on by default can be switched off', async () => {
    render(<PreviewPanel />);

    await userEvent.selectOptions(screen.getByLabelText('Output format'), 'json');
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ }).at(-1)!);
    const indent = screen.getByLabelText('indent') as HTMLSelectElement;
    expect(indent.value).toBe('');

    await userEvent.selectOptions(indent, 'no');
    expect(targetData().format).toEqual({ type: 'json', options: { indent: false } });
    await userEvent.selectOptions(indent, '');
    expect(targetData().format).toBeUndefined();
  });

  it('a raw sample is decoded by its format when applied, and the box keeps the raw text', async () => {
    useStore.setState({ sampleInput: '<o><sku>A</sku></o>' });
    useStore.getState().updateNodeData('s', { format: { type: 'xml' } });
    api.post.mockResolvedValue({ data: { format: 'xml', decoded: { o: { sku: 'A', qty: '2' } } } });
    render(<PreviewPanel />);

    await userEvent.click(screen.getByRole('button', { name: /Apply to Input JSON/ }));

    await waitFor(() => expect(api.post).toHaveBeenCalledWith('/api/mapping/decode', { format: { type: 'xml' }, rawInput: '<o><sku>A</sku></o>' }));
    await waitFor(() => expect(sourceData().fields).toEqual(expect.arrayContaining(['o.sku', 'o.qty'])));
    expect(useStore.getState().sampleInput).toBe('<o><sku>A</sku></o>');
  });

  it('runs a mapping with formats through run and shows the output, mapped JSON and the input seen as JSON', async () => {
    useStore.setState({ sampleInput: 'sku\nA\n' });
    useStore.getState().updateNodeData('s', { format: { type: 'text', options: { mode: 'delimited' } } });
    useStore.getState().updateNodeData('t', { format: { type: 'xml' } });
    api.post.mockResolvedValue({
      data: { inputFormat: 'text', outputFormat: 'xml', contentType: 'application/xml', output: '<r><sku>A</sku></r>', decoded: { rows: [{ sku: 'A' }] }, mapped: { r: { sku: 'A' } } },
    });
    render(<PreviewPanel />);

    await userEvent.click(screen.getByTitle('Execute Engine'));

    await waitFor(() => expect(screen.getByText('<r><sku>A</sku></r>')).toBeTruthy());
    const [path, body] = api.post.mock.calls[0] as [string, any];
    expect(path).toBe('/api/mapping/run');
    expect(body.rawInput).toBe('sku\nA\n');
    expect(body.mapping.formats).toEqual({ input: { type: 'text', options: { mode: 'delimited' } }, output: { type: 'xml' } });

    await userEvent.click(screen.getByRole('tab', { name: 'Seen as JSON' }));
    expect(screen.getByText(/"rows"/)).toBeTruthy();
    await userEvent.click(screen.getByRole('tab', { name: 'Mapped JSON' }));
    expect(screen.getByText(/"r"/)).toBeTruthy();
  });

  it('a sample the format cannot read shows the service message, with where', async () => {
    useStore.setState({ sampleInput: '<o>' });
    useStore.getState().updateNodeData('s', { format: { type: 'xml' } });
    api.post.mockRejectedValue({ response: { data: { error: 'Reading xml: Unexpected end of file. (line 1, column 4)', format: 'xml' } } });
    render(<PreviewPanel />);

    await userEvent.click(screen.getByTitle('Execute Engine'));

    expect(await screen.findByText('Reading xml: Unexpected end of file. (line 1, column 4)')).toBeTruthy();
  });

  it('says so when the service has no formats (the public playground)', async () => {
    useFormatCatalog.setState({ formats: null, status: 'unavailable' });
    useStore.getState().updateNodeData('s', { format: { type: 'xml' } });
    render(<PreviewPanel />);

    expect(screen.getByText(/cannot read or write formats other than JSON/)).toBeTruthy();
  });

  it('without a Target node the output picker is disabled and says what to add', () => {
    useStore.setState({ nodes: [node('s', 'source', { label: 'Source', fields: ['sku'] })] as never });
    render(<PreviewPanel />);

    expect((screen.getByLabelText('Output format') as HTMLSelectElement).disabled).toBe(true);
    expect(screen.getByText(/Add an Output JSON \(Target\) node/)).toBeTruthy();
  });
});
