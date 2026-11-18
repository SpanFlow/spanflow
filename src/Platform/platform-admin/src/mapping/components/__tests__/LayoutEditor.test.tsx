import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PreviewPanel } from '../PreviewPanel';
import { useStore } from '../../store/useStore';
import { useFormatCatalog, useStarters } from '../../formats/formatsApi';
import type { FormatInfo } from '../../formats/formats';

vi.mock('../../../lib/ufmappingApi', () => ({ ufmappingApi: { post: vi.fn(), get: vi.fn(), defaults: { baseURL: 'x' } } }));

const catalog: FormatInfo[] = [
  { type: 'json', contentType: 'application/json', options: [] },
  {
    type: 'positional',
    contentType: 'text/plain',
    options: [
      { name: 'layout', kind: 'object', use: 'both', description: 'The fields.' },
      { name: 'strict', kind: 'bool', use: 'decodeOnly', description: 'Fail on leftovers.' },
    ],
  },
];

const PK = 'PK000111222334403235544789' + '05AB123';
const node = (id: string, type: string, data: Record<string, unknown> = {}) => ({ id, type, position: { x: 0, y: 0 }, data });
const sourceFormat = () => useStore.getState().nodes.find((n) => n.type === 'source')!.data.format as any;

// These drive the whole preview panel and the editor with many clicks: slower than the 5 s default under load.
describe('positional layout editor (E73-T8)', { timeout: 15_000 }, () => {
  beforeEach(() => {
    useFormatCatalog.setState({ formats: catalog, status: 'ready' });
    useStore.setState({
      nodes: [node('s', 'source', { label: 'Source', fields: [], format: { type: 'positional' } }), node('t', 'target', { label: 'Target', fields: [] })] as never,
      edges: [],
      docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
      activeDocId: 'main',
      sampleInput: PK,
    });
  });

  const openEditor = async () => {
    render(<PreviewPanel />);
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ })[0]);
    expect(screen.getByTestId('opt-input-positional-layout-summary').textContent).toBe('No layout yet');
    await userEvent.click(screen.getByRole('button', { name: 'Edit layout' }));
    return screen.getByRole('dialog', { name: 'Input layout' });
  };

  it('guesses fields from the sample, saves them on the Source node and draws them on the ruler', async () => {
    const dialog = await openEditor();
    expect((within(dialog).getByLabelText('Sample') as HTMLTextAreaElement).value).toBe(PK);

    await userEvent.click(within(dialog).getByRole('button', { name: /Guess from sample/ }));

    const layout = sourceFormat().options.layout;
    expect(layout.fields[0]).toEqual({ name: 'field1', length: 2, type: 'alpha' });
    const ruler = within(dialog).getByTestId('layout-ruler');
    expect(ruler.querySelector('[title^="field1: positions 1 to 2"]')?.textContent).toBe('PK');
    expect(within(dialog).getAllByLabelText(/^Kind of/).length).toBe(layout.fields.length);
  });

  it('turns a field into a length-prefixed one and shows where it reads, live', async () => {
    useStore.setState({
      nodes: [
        node('s', 'source', {
          label: 'Source',
          fields: [],
          format: {
            type: 'positional',
            options: {
              layout: {
                fields: [
                  { name: 'country', length: 2, type: 'alpha' },
                  { name: 'account', length: 13, type: 'numeric' },
                  { name: 'phone', length: 11, type: 'numeric' },
                  { name: 'reference', length: 7 },
                ],
              },
            },
          },
        }),
        node('t', 'target', { label: 'Target', fields: [] }),
      ] as never,
    });
    render(<PreviewPanel />);
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ })[0]);
    expect(screen.getByTestId('opt-input-positional-layout-summary').textContent).toBe('4 fields');
    await userEvent.click(screen.getByRole('button', { name: 'Edit layout' }));
    const dialog = screen.getByRole('dialog');

    await userEvent.selectOptions(within(dialog).getByLabelText('Width of reference'), 'prefix');

    expect(sourceFormat().options.layout.fields[3]).toEqual({ name: 'reference', prefix: 2 });
    const ruler = within(dialog).getByTestId('layout-ruler');
    expect(ruler.querySelector('[title^="reference length (5)"]')?.textContent).toBe('05');
    expect(ruler.querySelector('[title^="reference: positions 29 to 33"]')?.textContent).toBe('AB123');
    expect(within(dialog).queryByLabelText('Layout problems')).toBeNull();

    // A click on a colour selects its row.
    await userEvent.click(ruler.querySelector('[title^="phone:"]')!);
    const rows = within(dialog).getAllByRole('row').filter((r) => r.getAttribute('aria-selected') === 'true');
    expect((within(rows[0]).getByLabelText('Field name') as HTMLInputElement).value).toBe('phone');
  });

  it('shows what is left over as a problem', async () => {
    const dialog = await openEditor();
    await userEvent.click(within(dialog).getByRole('button', { name: /Add field/ }));
    expect(within(dialog).getByLabelText('Layout problems').textContent).toContain('32 characters left after the last field.');
  });

  it('asks before a guess replaces fields already there', async () => {
    const dialog = await openEditor();
    await userEvent.click(within(dialog).getByRole('button', { name: /Add field/ }));
    await userEvent.click(within(dialog).getByRole('button', { name: /Guess from sample/ }));
    expect(sourceFormat().options.layout.fields).toHaveLength(1);
    expect(within(dialog).getByText('This replaces the fields below.')).toBeTruthy();
    await userEvent.click(within(dialog).getByRole('button', { name: /Replace with a guess/ }));
    expect(sourceFormat().options.layout.fields.length).toBeGreaterThan(1);
  });

  it('starts a side from a bundled starter and shows its bitmap on the ruler (E73-T9)', async () => {
    useStarters.setState({
      status: 'ready',
      starters: [
        {
          name: 'iso8583-mini',
          title: 'ISO 8583 (mini)',
          format: 'positional',
          description: 'test',
          options: {
            layout: {
              fields: [
                { name: 'mti', length: 4, type: 'numeric' },
                { bitmap: 'hex', fields: [{ bit: 2, name: 'pan', prefix: 2, type: 'numeric' }, { bit: 3, name: 'processingCode', length: 6, type: 'numeric' }] },
              ],
            },
          },
        },
      ],
    });
    useStore.setState({ sampleInput: '0200' + '6000000000000000' + '04' + '1234' + '000000' });
    render(<PreviewPanel />);
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ })[0]);

    await userEvent.selectOptions(screen.getByLabelText('Start from'), 'iso8583-mini');

    expect(sourceFormat().options.layout.fields[1].bitmap).toBe('hex');
    expect(screen.getByTestId('opt-input-positional-layout-summary').textContent).toBe('3 fields');
    await userEvent.click(screen.getByRole('button', { name: 'Edit layout' }));
    const dialog = screen.getByRole('dialog');
    const ruler = within(dialog).getByTestId('layout-ruler');
    expect(ruler.querySelector('[title^="bitmap primary"]')?.textContent).toBe('6000000000000000');
    expect(ruler.querySelector('[title^="processingCode:"]')?.textContent).toBe('000000');
    expect((within(dialog).getByLabelText('Bit of pan') as HTMLInputElement).value).toBe('2');
    expect(within(dialog).queryByLabelText('Layout problems')).toBeNull();
  });
});
