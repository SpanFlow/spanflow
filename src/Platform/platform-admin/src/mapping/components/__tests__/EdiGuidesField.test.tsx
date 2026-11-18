import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PreviewPanel } from '../PreviewPanel';
import { useStore } from '../../store/useStore';
import { useEdiGuides, useFormatCatalog } from '../../formats/formatsApi';
import type { FormatInfo } from '../../formats/formats';

vi.mock('../../../lib/ufmappingApi', () => ({ ufmappingApi: { post: vi.fn(), get: vi.fn(), defaults: { baseURL: 'x' } } }));

const catalog: FormatInfo[] = [
  {
    type: 'edi',
    contentType: 'text/plain',
    options: [
      { name: 'guides', kind: 'any', use: 'both', description: 'Level 2 guides.' },
      { name: 'validate', kind: 'bool', use: 'both', description: 'Stop at the first problem.' },
    ],
  },
];

const node = (id: string, type: string, data: Record<string, unknown> = {}) => ({ id, type, position: { x: 0, y: 0 }, data });
const sourceFormat = () => useStore.getState().nodes.find((n) => n.type === 'source')!.data.format as any;

describe('EDI guides picker (E73-T11)', () => {
  beforeEach(() => {
    useFormatCatalog.setState({ formats: catalog, status: 'ready' });
    useEdiGuides.setState({
      status: 'ready',
      guides: [
        { name: 'x12-850', standard: 'x12', message: '850', version: '004010', title: 'Purchase order' },
        { name: 'x12-997', standard: 'x12', message: '997', version: '004010', title: 'Functional acknowledgement' },
      ],
    });
    useStore.setState({
      nodes: [node('s', 'source', { label: 'Source', fields: [], format: { type: 'edi' } }), node('t', 'target', { label: 'Target', fields: [] })] as never,
      edges: [],
      docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
      activeDocId: 'main',
      sampleInput: 'ISA*...',
    });
  });

  const open = async () => {
    render(<PreviewPanel />);
    await userEvent.click(screen.getAllByRole('button', { name: /Options/ })[0]);
  };

  it('picks guides one by one, or all of them', async () => {
    await open();
    await userEvent.click(screen.getByRole('checkbox', { name: /850/ }));
    await userEvent.click(screen.getByRole('checkbox', { name: /997/ }));
    expect(sourceFormat().options.guides).toEqual(['x12-850', 'x12-997']);

    await userEvent.click(screen.getByRole('checkbox', { name: 'All bundled guides' }));
    expect(sourceFormat().options.guides).toBe('bundled');
    expect((screen.getByRole('checkbox', { name: /850/ }) as HTMLInputElement).disabled).toBe(true);

    await userEvent.click(screen.getByRole('checkbox', { name: 'All bundled guides' }));
    expect(sourceFormat().options.guides).toEqual(['x12-850', 'x12-997']);
  });

  it('takes a guide of your own as JSON, before the bundled ones', async () => {
    await open();
    await userEvent.click(screen.getByRole('checkbox', { name: 'All bundled guides' }));
    const own = screen.getByLabelText('Your own guides (JSON, used first)');
    await userEvent.click(own);
    await userEvent.paste('{"name":"acme-850","standard":"x12","message":"850","segments":[{"id":"BEG"}]}');

    expect(sourceFormat().options.guides).toEqual([{ name: 'acme-850', standard: 'x12', message: '850', segments: [{ id: 'BEG' }] }, 'bundled']);
  });

  it('says so while the JSON is not finished, and keeps the last good choice', async () => {
    await open();
    await userEvent.click(screen.getByRole('checkbox', { name: /850/ }));
    await userEvent.click(screen.getByLabelText('Your own guides (JSON, used first)'));
    await userEvent.paste('{"name":');
    expect(screen.getByText(/Not valid JSON yet/)).toBeTruthy();
    expect(sourceFormat().options.guides).toBe('x12-850');
  });
});
