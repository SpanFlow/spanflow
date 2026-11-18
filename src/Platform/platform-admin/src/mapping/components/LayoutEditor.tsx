import { useMemo, useState, type ReactNode } from 'react';
import { ArrowDown, ArrowUp, Plus, Trash2, Wand2, X } from 'lucide-react';
import {
  FIELD_TYPES,
  addField,
  addRecord,
  changeKind,
  changeSize,
  fieldKind,
  fieldsOf,
  firstPositions,
  guessLayout,
  locKey,
  measure,
  moveField,
  removeField,
  setField,
  sizeKind,
  switchMode,
  type FieldKind,
  type FieldLoc,
  type Layout,
  type LayoutField,
  type LayoutRecord,
  type RulerSpan,
  type SizeKind,
} from '../formats/layout';

/** Field colours on the ruler and in the table, by top-level field index. Literal classes so Tailwind keeps them. */
const COLOURS = [
  { bg: 'bg-sky-200 dark:bg-sky-500/40', dot: 'bg-sky-400' },
  { bg: 'bg-amber-200 dark:bg-amber-500/40', dot: 'bg-amber-400' },
  { bg: 'bg-emerald-200 dark:bg-emerald-500/40', dot: 'bg-emerald-400' },
  { bg: 'bg-fuchsia-200 dark:bg-fuchsia-500/40', dot: 'bg-fuchsia-400' },
  { bg: 'bg-lime-200 dark:bg-lime-500/40', dot: 'bg-lime-400' },
  { bg: 'bg-orange-200 dark:bg-orange-500/40', dot: 'bg-orange-400' },
  { bg: 'bg-cyan-200 dark:bg-cyan-500/40', dot: 'bg-cyan-400' },
  { bg: 'bg-violet-200 dark:bg-violet-500/40', dot: 'bg-violet-400' },
];
const colourOf = (slot: number) => COLOURS[((slot % COLOURS.length) + COLOURS.length) % COLOURS.length];

const MAX_RULER_LINES = 40;

const cell =
  'text-xs rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 px-1 py-0.5';
const iconButton = 'p-1 rounded text-slate-400 hover:text-indigo-600 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30';

/**
 * E73-T8: the positional layout editor. A table of fields (name, width or length prefix, type, padding) under a
 * ruler that colours the sample's characters by the field that takes them, updated as you type; a click on a colour
 * selects its row. "Guess from sample" proposes a first layout from the sample's columns.
 */
export const LayoutEditor = ({
  title,
  layout,
  sample: initialSample,
  onChange,
  onClose,
}: {
  title: string;
  layout: Layout;
  sample: string;
  onChange: (layout: Layout) => void;
  onClose: () => void;
}) => {
  const [sample, setSample] = useState(initialSample);
  const [selected, setSelected] = useState<string | null>(null);
  const [recordIndex, setRecordIndex] = useState(0);
  const [confirmGuess, setConfirmGuess] = useState(false);

  const isRecords = !!layout.records;
  const record = isRecords ? Math.min(recordIndex, (layout.records?.length ?? 1) - 1) : null;
  const ruler = useMemo(() => measure(layout, sample), [layout, sample]);
  const positions = useMemo(() => firstPositions(ruler), [ruler]);
  const hasFields = (layout.fields?.length ?? 0) > 0 || (layout.records?.length ?? 0) > 0;

  const guess = () => {
    if (hasFields && !confirmGuess) {
      setConfirmGuess(true);
      return;
    }
    setConfirmGuess(false);
    setSelected(null);
    setRecordIndex(0);
    onChange(guessLayout(sample));
  };

  const pickSpan = (span: RulerSpan) => {
    if (!span.loc) return;
    if (span.loc.record !== null) setRecordIndex(span.loc.record);
    setSelected(locKey(span.loc));
  };

  const updateRecord = (patch: Partial<LayoutRecord>) => {
    if (record === null || !layout.records) return;
    const records = layout.records.map((r, i) => (i === record ? { ...r, ...patch } : r));
    onChange({ records });
  };

  return (
    <div className="fixed inset-0 z-[60] bg-black/50 flex items-center justify-center p-4" onClick={onClose}>
      <div
        role="dialog"
        aria-label={title}
        className="bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-[min(64rem,96vw)] max-h-[90vh] flex flex-col border border-slate-200 dark:border-slate-700"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0">
          <h3 className="font-bold text-slate-800 dark:text-white text-sm">{title}</h3>
          <button onClick={onClose} className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500" title="Close">
            <X size={16} />
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-auto p-4 flex flex-col gap-4">
          <section className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between gap-2">
              <label htmlFor="layout-sample" className="text-xs font-semibold text-slate-500 dark:text-slate-400">
                Sample
              </label>
              <div className="flex items-center gap-2">
                {confirmGuess && <span className="text-[11px] text-amber-600 dark:text-amber-400">This replaces the fields below.</span>}
                <button
                  type="button"
                  onClick={guess}
                  disabled={!sample.trim()}
                  className="text-[11px] font-semibold text-indigo-600 dark:text-indigo-400 px-2 py-1 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-1 disabled:opacity-40"
                  title="Propose fields from where the sample's characters change between digits, letters and spaces"
                >
                  <Wand2 size={12} /> {confirmGuess ? 'Replace with a guess' : 'Guess from sample'}
                </button>
              </div>
            </div>
            <textarea
              id="layout-sample"
              rows={3}
              spellCheck={false}
              value={sample}
              onChange={(e) => {
                setSample(e.target.value);
                setConfirmGuess(false);
              }}
              placeholder="Paste a line (or a few lines) of the data to see where each field falls."
              className={`${cell} w-full font-mono py-1`}
            />
            <RulerView sample={sample} spans={ruler.spans} selected={selected} onPick={pickSpan} />
            {ruler.problems.length > 0 && (
              <ul className="text-[11px] text-red-600 dark:text-red-400 flex flex-col gap-0.5" aria-label="Layout problems">
                {ruler.problems.slice(0, 6).map((p, i) => (
                  <li key={i}>
                    {ruler.lineRecords.length > 1 ? `Line ${p.line + 1}, ` : ''}position {p.offset + 1}: {p.message}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="flex flex-col gap-2">
            <div className="flex items-center gap-3 flex-wrap">
              <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">The data is</span>
              <label className="text-xs flex items-center gap-1 text-slate-700 dark:text-slate-200">
                <input type="radio" name="layout-mode" checked={!isRecords} onChange={() => onChange(switchMode(layout, false))} />
                one record
              </label>
              <label className="text-xs flex items-center gap-1 text-slate-700 dark:text-slate-200">
                <input type="radio" name="layout-mode" checked={isRecords} onChange={() => onChange(switchMode(layout, true))} />
                one record per line (header, details, trailer)
              </label>
            </div>

            {isRecords && layout.records && record !== null && (
              <div className="flex flex-col gap-2 rounded-md border border-slate-200 dark:border-slate-800 p-2">
                <div className="flex items-center gap-1 flex-wrap" role="tablist" aria-label="Records">
                  {layout.records.map((r, i) => (
                    <button
                      key={i}
                      role="tab"
                      aria-selected={i === record}
                      onClick={() => setRecordIndex(i)}
                      className={`text-[11px] font-semibold px-2 py-0.5 rounded ${
                        i === record ? 'bg-indigo-600 text-white' : 'text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800'
                      }`}
                    >
                      {r.name || `record ${i + 1}`}
                    </button>
                  ))}
                  <button
                    type="button"
                    onClick={() => {
                      onChange(addRecord(layout));
                      setRecordIndex(layout.records!.length);
                    }}
                    className="text-[11px] text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-0.5"
                  >
                    <Plus size={11} /> Record
                  </button>
                </div>
                <RecordForm
                  record={layout.records[record]}
                  canRemove={layout.records.length > 1}
                  onChange={updateRecord}
                  onRemove={() => {
                    onChange({ records: layout.records!.filter((_, i) => i !== record) });
                    setRecordIndex(0);
                  }}
                />
              </div>
            )}

            <FieldTable
              layout={layout}
              record={record}
              positions={positions}
              selected={selected}
              onSelect={setSelected}
              onChange={onChange}
            />
          </section>
        </div>

        <div className="flex items-center justify-between gap-2 px-4 py-3 border-t border-slate-200 dark:border-slate-800 shrink-0">
          <p className="text-[11px] text-slate-400">Positions count characters. Changes are saved on the mapping as you make them.</p>
          <button onClick={onClose} className="text-xs font-semibold bg-indigo-600 hover:bg-indigo-700 text-white rounded-md px-3 py-1.5">
            Done
          </button>
        </div>
      </div>
    </div>
  );
};

/** The sample in a fixed-width font with every field's characters coloured, a scale above, a click selects. */
const RulerView = ({
  sample,
  spans,
  selected,
  onPick,
}: {
  sample: string;
  spans: RulerSpan[];
  selected: string | null;
  onPick: (span: RulerSpan) => void;
}) => {
  const lines = sample.split('\n').map((l) => (l.endsWith('\r') ? l.slice(0, -1) : l));
  if (!sample) return null;
  const width = Math.max(...lines.map((l) => l.length), 1);
  const scale = Array.from({ length: Math.ceil(width / 10) }, (_, i) => String((i + 1) * 10).padStart(10, ' ')).join('');

  return (
    <div className="overflow-x-auto rounded-md border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/60 p-2" data-testid="layout-ruler">
      <pre className="text-[11px] leading-5 font-mono text-slate-400 select-none">{scale}</pre>
      {lines.slice(0, MAX_RULER_LINES).map((line, i) => (
        <RulerLine key={i} line={line} spans={spans.filter((s) => s.line === i)} selected={selected} onPick={onPick} />
      ))}
      {lines.length > MAX_RULER_LINES && <p className="text-[10px] text-slate-400 mt-1">Showing the first {MAX_RULER_LINES} lines.</p>}
    </div>
  );
};

const RulerLine = ({
  line,
  spans,
  selected,
  onPick,
}: {
  line: string;
  spans: RulerSpan[];
  selected: string | null;
  onPick: (span: RulerSpan) => void;
}) => {
  const parts: ReactNode[] = [];
  let at = 0;
  const sorted = [...spans].sort((a, b) => a.start - b.start);
  sorted.forEach((span, i) => {
    if (span.start > at) parts.push(<span key={`g${i}`}>{line.slice(at, span.start)}</span>);
    const text = line.slice(span.start, span.end);
    const isSelected = !!span.loc && locKey(span.loc) === selected;
    const tone =
      span.kind === 'left' || span.kind === 'unmatched'
        ? 'bg-red-200 dark:bg-red-500/40 line-through decoration-red-500'
        : span.kind === 'filler'
          ? 'bg-slate-200 dark:bg-slate-700'
          : span.kind === 'bitmap'
            ? 'bg-slate-300 dark:bg-slate-600 italic'
            : colourOf(span.colour).bg;
    parts.push(
      <span
        key={`s${i}`}
        title={`${span.label}: positions ${span.start + 1} to ${span.end}`}
        onClick={() => onPick(span)}
        className={`${tone} ${span.kind === 'prefix' ? 'underline decoration-dotted' : ''} ${
          isSelected ? 'outline outline-2 outline-indigo-600 relative z-10' : ''
        } ${span.loc ? 'cursor-pointer' : ''}`}
      >
        {text}
      </span>,
    );
    at = Math.max(at, span.end);
  });
  if (at < line.length) parts.push(<span key="tail">{line.slice(at)}</span>);
  return <pre className="text-[12px] leading-5 font-mono text-slate-800 dark:text-slate-100 whitespace-pre">{parts.length ? parts : ' '}</pre>;
};

const RecordForm = ({
  record,
  canRemove,
  onChange,
  onRemove,
}: {
  record: LayoutRecord;
  canRemove: boolean;
  onChange: (patch: Partial<LayoutRecord>) => void;
  onRemove: () => void;
}) => (
  <div className="flex items-end gap-3 flex-wrap text-[11px] text-slate-600 dark:text-slate-300">
    <label className="flex flex-col gap-0.5">
      Record name
      <input aria-label="Record name" className={`${cell} w-32`} value={record.name} onChange={(e) => onChange({ name: e.target.value })} />
    </label>
    <label className="flex flex-col gap-0.5">
      Line starts at
      <input
        aria-label="Code position"
        type="number"
        min={1}
        className={`${cell} w-16`}
        value={(record.when?.start ?? 0) + 1}
        onChange={(e) => onChange({ when: { equals: record.when?.equals ?? '', start: Math.max(0, Math.trunc(Number(e.target.value) || 1) - 1) } })}
      />
    </label>
    <label className="flex flex-col gap-0.5">
      with
      <input
        aria-label="Record code"
        className={`${cell} w-20 font-mono`}
        placeholder="any line"
        value={record.when?.equals ?? ''}
        onChange={(e) =>
          onChange({ when: e.target.value ? { start: record.when?.start ?? 0, equals: e.target.value } : undefined })
        }
      />
    </label>
    <label className="flex items-center gap-1 pb-1">
      <input type="checkbox" checked={!!record.many} onChange={(e) => onChange({ many: e.target.checked || undefined })} />
      repeats (a list)
    </label>
    {canRemove && (
      <button type="button" onClick={onRemove} className="ml-auto text-red-600 dark:text-red-400 hover:underline pb-1">
        Remove record
      </button>
    )}
  </div>
);

const FieldTable = ({
  layout,
  record,
  positions,
  selected,
  onSelect,
  onChange,
}: {
  layout: Layout;
  record: number | null;
  positions: Map<string, { start: number; end: number }>;
  selected: string | null;
  onSelect: (key: string) => void;
  onChange: (layout: Layout) => void;
}) => {
  const fields = fieldsOf(layout, record);
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs text-left border-collapse" aria-label="Layout fields">
        <thead>
          <tr className="text-[10px] uppercase tracking-wide text-slate-400 border-b border-slate-200 dark:border-slate-800">
            <th className="py-1 pr-1 w-4" />
            <th className="py-1 pr-2 font-semibold">Kind</th>
            <th className="py-1 pr-2 font-semibold">Name / value</th>
            <th className="py-1 pr-2 font-semibold">Width</th>
            <th className="py-1 pr-2 font-semibold">Type</th>
            <th className="py-1 pr-2 font-semibold">Pad</th>
            <th className="py-1 pr-2 font-semibold">Align</th>
            <th className="py-1 pr-2 font-semibold">More</th>
            <th className="py-1 pr-2 font-semibold">Position</th>
            <th className="py-1" />
          </tr>
        </thead>
        <tbody>
          {fields.map((f, i) => (
            <FieldRows
              key={i}
              layout={layout}
              field={f}
              loc={{ record, path: [i] }}
              depth={0}
              colour={i}
              siblings={fields.length}
              positions={positions}
              selected={selected}
              onSelect={onSelect}
              onChange={onChange}
            />
          ))}
        </tbody>
      </table>
      {fields.length === 0 && <p className="text-[11px] text-slate-400 py-2">No fields yet. Add one, or guess them from the sample.</p>}
      <button
        type="button"
        onClick={() => onChange(addField(layout, record))}
        className="mt-1 text-[11px] font-semibold text-indigo-600 dark:text-indigo-400 px-2 py-1 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-1"
      >
        <Plus size={12} /> Add field
      </button>
    </div>
  );
};

const FieldRows = ({
  layout,
  field,
  loc,
  depth,
  colour,
  siblings,
  positions,
  selected,
  onSelect,
  onChange,
  inBitmap = false,
}: {
  layout: Layout;
  field: LayoutField;
  loc: FieldLoc;
  depth: number;
  colour: number;
  siblings: number;
  inBitmap?: boolean;
  positions: Map<string, { start: number; end: number }>;
  selected: string | null;
  onSelect: (key: string) => void;
  onChange: (layout: Layout) => void;
}) => {
  const key = locKey(loc);
  const kind = fieldKind(field);
  const index = loc.path[loc.path.length - 1];
  const pos = positions.get(key);
  const set = (next: LayoutField) => onChange(setField(layout, loc, next));
  const patch = (p: Partial<LayoutField>) => set({ ...field, ...p });
  const label = field.name || (kind === 'const' ? 'constant' : kind);

  return (
    <>
      <tr
        onClick={() => onSelect(key)}
        aria-selected={selected === key}
        className={`border-b border-slate-100 dark:border-slate-800/70 ${selected === key ? 'bg-indigo-50 dark:bg-indigo-500/10' : ''}`}
      >
        <td className="py-1 pr-1">
          <span className={`inline-block w-2.5 h-2.5 rounded-full ${kind === 'filler' ? 'bg-slate-300' : colourOf(colour).dot}`} />
        </td>
        <td className="py-1 pr-2" style={{ paddingLeft: depth * 14 }}>
          <div className="flex items-center gap-1">
            {inBitmap && (
              <input
                aria-label={`Bit of ${label}`}
                type="number"
                min={2}
                max={128}
                title="The bitmap bit that says this field is present (2 to 128)"
                className={`${cell} w-12`}
                value={field.bit ?? ''}
                onChange={(e) => patch({ bit: e.target.value === '' ? undefined : Math.min(128, Math.max(2, Math.trunc(Number(e.target.value)))) })}
              />
            )}
            <select aria-label={`Kind of ${label}`} className={cell} value={kind} onChange={(e) => set({ ...changeKind(field, e.target.value as FieldKind), ...(inBitmap ? { bit: field.bit } : {}) })}>
              <option value="data">data</option>
              {!inBitmap && <option value="const">constant</option>}
              {!inBitmap && <option value="filler">filler</option>}
              <option value="group">group</option>
              {!inBitmap && <option value="bitmap">bitmap</option>}
            </select>
          </div>
        </td>
        <td className="py-1 pr-2">
          {kind === 'const' ? (
            <input
              aria-label="Constant value"
              className={`${cell} w-28 font-mono`}
              value={field.const ?? ''}
              onChange={(e) => patch({ const: e.target.value, length: field.length !== undefined ? e.target.value.length || 1 : undefined })}
            />
          ) : kind === 'filler' ? (
            <span className="text-[11px] text-slate-400">not data</span>
          ) : kind === 'bitmap' ? (
            <input
              aria-label="Bitmap name"
              className={`${cell} w-28`}
              placeholder="(fields at this level)"
              title="Leave empty to put the bitmap's fields beside the others; a name nests them under it"
              value={field.name ?? ''}
              onChange={(e) => patch({ name: e.target.value || undefined })}
            />
          ) : (
            <input aria-label="Field name" className={`${cell} w-28`} value={field.name ?? ''} onChange={(e) => patch({ name: e.target.value })} />
          )}
        </td>
        <td className="py-1 pr-2">
          {kind === 'bitmap' ? (
            <select
              aria-label={`Bitmap form of ${label}`}
              className={cell}
              title="hex: 16 characters per 64 bits; binary: 8 bytes per 64 bits. Bit 1 set means a secondary bitmap follows."
              value={field.bitmap}
              onChange={(e) => patch({ bitmap: e.target.value as 'hex' | 'binary' })}
            >
              <option value="hex">hex (16 chars)</option>
              <option value="binary">binary (8 bytes)</option>
            </select>
          ) : kind === 'group' ? (
            <input
              aria-label={`Repeat ${label}`}
              className={`${cell} w-28`}
              placeholder="once"
              title="Repeat the group: a number of times, or the name of an earlier count field"
              value={field.repeat ?? ''}
              onChange={(e) => {
                const v = e.target.value.trim();
                patch({ repeat: v === '' ? undefined : /^[0-9]+$/.test(v) ? Number(v) : v });
              }}
            />
          ) : (
            <div className="flex items-center gap-1">
              <select
                aria-label={`Width of ${label}`}
                className={cell}
                value={sizeKind(field)}
                onChange={(e) => set(changeSize(field, e.target.value as SizeKind))}
              >
                <option value="length">fixed</option>
                <option value="prefix">length first</option>
                <option value="rest">to the end</option>
              </select>
              {sizeKind(field) === 'length' && (
                <input
                  aria-label={`Length of ${label}`}
                  type="number"
                  min={1}
                  className={`${cell} w-14`}
                  value={field.length ?? ''}
                  onChange={(e) => patch({ length: e.target.value === '' ? undefined : Math.max(1, Math.trunc(Number(e.target.value))) })}
                />
              )}
              {sizeKind(field) === 'prefix' && (
                <select
                  aria-label={`Length digits of ${label}`}
                  className={cell}
                  title="How many digits the length is written in: 2 is LL (up to 99), 3 is LLL (up to 999)"
                  value={field.prefix}
                  onChange={(e) => patch({ prefix: Number(e.target.value) })}
                >
                  {[1, 2, 3, 4, 5, 6].map((n) => (
                    <option key={n} value={n}>
                      {'L'.repeat(n)}
                    </option>
                  ))}
                </select>
              )}
            </div>
          )}
        </td>
        <td className="py-1 pr-2">
          {kind === 'data' && (
            <select aria-label={`Type of ${label}`} className={cell} value={field.type ?? 'text'} onChange={(e) => patch({ type: e.target.value })}>
              {FIELD_TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          )}
        </td>
        <td className="py-1 pr-2">
          {kind === 'data' && (
            <input
              aria-label={`Pad of ${label}`}
              maxLength={1}
              className={`${cell} w-8 font-mono text-center`}
              placeholder={['numeric', 'integer', 'decimal'].includes(field.type ?? '') ? '0' : '␣'}
              value={field.pad ?? ''}
              onChange={(e) => patch({ pad: e.target.value || undefined })}
            />
          )}
        </td>
        <td className="py-1 pr-2">
          {kind === 'data' && (
            <select aria-label={`Align of ${label}`} className={cell} value={field.align ?? ''} onChange={(e) => patch({ align: e.target.value || undefined })}>
              <option value="">default</option>
              <option value="left">left</option>
              <option value="right">right</option>
            </select>
          )}
        </td>
        <td className="py-1 pr-2">{kind === 'data' && <MoreCell field={field} label={label} patch={patch} />}</td>
        <td className="py-1 pr-2 font-mono text-[11px] text-slate-500 whitespace-nowrap">{pos ? `${pos.start + 1}-${pos.end}` : '-'}</td>
        <td className="py-1 whitespace-nowrap text-right">
          {(kind === 'group' || kind === 'bitmap') && (
            <button type="button" className={iconButton} title={kind === 'bitmap' ? 'Add a field to this bitmap' : 'Add a field inside this group'} onClick={(e) => (e.stopPropagation(), onChange(addField(layout, loc.record, undefined, loc)))}>
              <Plus size={13} />
            </button>
          )}
          <button type="button" className={iconButton} title="Move up" disabled={index === 0} onClick={(e) => (e.stopPropagation(), onChange(moveField(layout, loc, -1)))}>
            <ArrowUp size={13} />
          </button>
          <button
            type="button"
            className={iconButton}
            title="Move down"
            disabled={index === siblings - 1}
            onClick={(e) => (e.stopPropagation(), onChange(moveField(layout, loc, 1)))}
          >
            <ArrowDown size={13} />
          </button>
          <button type="button" className={iconButton} title={`Remove ${label}`} onClick={(e) => (e.stopPropagation(), onChange(removeField(layout, loc)))}>
            <Trash2 size={13} />
          </button>
        </td>
      </tr>
      {(kind === 'group' || kind === 'bitmap') &&
        (field.fields ?? []).map((child, i) => (
          <FieldRows
            key={i}
            layout={layout}
            field={child}
            loc={{ record: loc.record, path: [...loc.path, i] }}
            depth={depth + 1}
            colour={colour}
            siblings={field.fields!.length}
            positions={positions}
            selected={selected}
            onSelect={onSelect}
            onChange={onChange}
            inBitmap={kind === 'bitmap'}
          />
        ))}
    </>
  );
};

/** The one or two settings only some types (or a length prefix) have. */
const MoreCell = ({ field, label, patch }: { field: LayoutField; label: string; patch: (p: Partial<LayoutField>) => void }) => {
  const items: ReactNode[] = [];
  if (field.type === 'decimal') {
    items.push(
      <input
        key="scale"
        aria-label={`Decimals of ${label}`}
        type="number"
        min={0}
        title="Digits after the implied decimal point"
        placeholder="decimals"
        className={`${cell} w-16`}
        value={field.scale ?? ''}
        onChange={(e) => patch({ scale: e.target.value === '' ? undefined : Math.max(0, Math.trunc(Number(e.target.value))) })}
      />,
    );
  }
  if (field.type === 'date') {
    items.push(
      <input
        key="format"
        aria-label={`Date format of ${label}`}
        placeholder="yyyyMMdd"
        className={`${cell} w-24 font-mono`}
        value={field.format ?? ''}
        onChange={(e) => patch({ format: e.target.value || undefined })}
      />,
    );
  }
  if (field.type === 'bool') {
    items.push(<BoolValuesInput key="values" field={field} label={label} patch={patch} />);
  }
  if (field.prefix !== undefined) {
    items.push(
      <input
        key="max"
        aria-label={`Max length of ${label}`}
        type="number"
        min={1}
        placeholder="max"
        title="Refuse a length prefix that says more than this"
        className={`${cell} w-16`}
        value={field.max ?? ''}
        onChange={(e) => patch({ max: e.target.value === '' ? undefined : Math.max(1, Math.trunc(Number(e.target.value))) })}
      />,
    );
  }
  return items.length ? <div className="flex items-center gap-1">{items}</div> : null;
};

/** "Y, N": kept as typed, saved once it is two texts. */
const BoolValuesInput = ({ field, label, patch }: { field: LayoutField; label: string; patch: (p: Partial<LayoutField>) => void }) => {
  const [text, setText] = useState(field.values?.join(', ') ?? '');
  return (
    <input
      aria-label={`Yes and no text of ${label}`}
      placeholder="Y, N"
      title="The text for true, then for false"
      className={`${cell} w-16 font-mono`}
      value={text}
      onChange={(e) => {
        setText(e.target.value);
        const parts = e.target.value.split(',').map((s) => s.trim());
        patch({ values: parts.length === 2 && parts.every(Boolean) ? parts : undefined });
      }}
    />
  );
};
