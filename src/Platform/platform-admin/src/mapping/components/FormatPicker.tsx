import { useEffect, useState, type ReactNode } from 'react';
import type { Node } from '@xyflow/react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { useStore } from '../store/useStore';
import {
  FORMAT_LABELS,
  FORMAT_TYPES,
  OPTION_CHOICES,
  isJsonType,
  optionsFor,
  type Direction,
  type FormatOptionInfo,
  type FormatSpec,
} from '../formats/formats';
import { useFormatCatalog, useStarters } from '../formats/formatsApi';
import { asLayout, summarise, type Layout } from '../formats/layout';
import { LayoutEditor } from './LayoutEditor';
import { mergeLists, repeatedPaths } from '../formats/xmlHints';
import { EdiGuidesField } from './EdiGuidesField';

/**
 * E73: the format of one side of the mapping, stored on its node (input on the Source node, output on the Target
 * node). The option fields are drawn from what the format itself declares (GET /api/mapping/formats), so a new
 * option on the service shows up here without a UI change.
 */
export const FormatPicker = ({ direction, node }: { direction: Direction; node: Node | undefined }) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  // The layout editor's ruler starts from the Sample Input on the input side; the output side starts empty.
  const sample = useStore((s) => (direction === 'input' ? s.sampleInput : ''));
  const { formats, status, load } = useFormatCatalog();
  const [open, setOpen] = useState(false);
  // Bumped when a starter replaces the options, so fields that keep their own typed text start again from them.
  const [rev, setRev] = useState(0);
  const starters = useStarters();

  const spec = (node?.data?.format as FormatSpec | undefined) ?? { type: 'json' };
  const needsCatalog = !isJsonType(spec) || open;
  useEffect(() => {
    if (needsCatalog && status === 'idle') void load();
  }, [needsCatalog, status, load]);

  const types = formats ? orderTypes(formats.map((f) => f.type)) : [...FORMAT_TYPES];
  const info = formats?.find((f) => f.type === spec.type);
  const fields = optionsFor(info, direction);
  const setCount = Object.keys(spec.options ?? {}).length;
  // E73-T9: starter layouts exist for positional only today; asked for the first time positional options open.
  const wantsStarters = open && spec.type === 'positional';
  useEffect(() => {
    if (wantsStarters && starters.status === 'idle') void starters.load();
  }, [wantsStarters, starters]);
  const startersHere = (starters.starters ?? []).filter((s) => s.format === spec.type);
  const side = direction === 'input' ? 'Input' : 'Output';
  const nodeName = direction === 'input' ? 'an Input JSON (Source) node' : 'an Output JSON (Target) node';

  const setSpec = (next: FormatSpec | undefined) => {
    if (node) updateNodeData(node.id, { format: next });
  };

  const changeType = (type: string) => {
    setSpec(type === 'json' ? undefined : { type });
    if (type !== 'json') setOpen(true);
  };

  const changeOption = (name: string, value: unknown) => {
    const options = { ...(spec.options ?? {}) };
    if (value === undefined || value === '' || (Array.isArray(value) && value.length === 0)) delete options[name];
    else options[name] = value;
    setSpec(Object.keys(options).length || spec.type !== 'json' ? { type: spec.type, options } : undefined);
  };

  return (
    <div className="flex flex-col gap-1.5" data-testid={`format-picker-${direction}`}>
      <div className="flex items-center gap-2">
        <label htmlFor={`format-${direction}`} className="text-xs font-semibold text-slate-500 dark:text-slate-400 shrink-0">
          {side} format
        </label>
        <select
          id={`format-${direction}`}
          value={spec.type}
          disabled={!node}
          onChange={(e) => changeType(e.target.value)}
          className="flex-1 min-w-0 text-xs rounded-md border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-950 text-slate-700 dark:text-slate-200 px-2 py-1 disabled:opacity-50"
          title={node ? `How the ${direction} is ${direction === 'input' ? 'read' : 'written'}` : `Add ${nodeName} first`}
        >
          {types.map((t) => (
            <option key={t} value={t}>
              {FORMAT_LABELS[t] ?? t}
            </option>
          ))}
        </select>
        {node && (
          <button
            type="button"
            onClick={() => setOpen(!open)}
            className="text-[10px] font-semibold text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-0.5 shrink-0"
            aria-expanded={open}
          >
            {open ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
            Options{setCount ? ` (${setCount})` : ''}
          </button>
        )}
      </div>

      {!node && <p className="text-[10px] text-slate-400">Add {nodeName} to choose how the {direction} is {direction === 'input' ? 'read' : 'written'}.</p>}

      {node && status === 'unavailable' && !isJsonType(spec) && (
        <p className="text-[10px] text-amber-600 dark:text-amber-400">
          This mapping service cannot read or write formats other than JSON. Run it from the Studio or a self-hosted designer.
        </p>
      )}

      {node && open && (
        <div className="flex flex-col gap-2 rounded-md border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/50 p-2">
          {status === 'loading' && <p className="text-[10px] text-slate-400">Loading the format options...</p>}
          {status === 'ready' && fields.length === 0 && <p className="text-[10px] text-slate-400">No options for {FORMAT_LABELS[spec.type] ?? spec.type} on this side.</p>}
          {startersHere.length > 0 && (
            <div>
              <label htmlFor={`starter-${direction}`} className="text-[11px] font-semibold text-slate-600 dark:text-slate-300">
                Start from
              </label>
              <select
                id={`starter-${direction}`}
                className={inputClass}
                value=""
                onChange={(e) => {
                  const starter = startersHere.find((s) => s.name === e.target.value);
                  if (!starter) return;
                  setSpec({ type: spec.type, options: JSON.parse(JSON.stringify(starter.options)) });
                  setRev((r) => r + 1);
                }}
              >
                <option value="">(choose a ready-made layout)</option>
                {startersHere.map((s) => (
                  <option key={s.name} value={s.name} title={s.description}>
                    {s.title}
                  </option>
                ))}
              </select>
              <p className="text-[10px] text-slate-400 leading-snug">Replaces this side's options with a copy you can edit. Undo brings them back.</p>
            </div>
          )}
          {fields.map((field) => (
            <OptionField
              key={`${spec.type}.${field.name}.${rev}`}
              format={spec.type}
              direction={direction}
              info={field}
              value={spec.options?.[field.name]}
              sample={sample}
              onChange={(v) => changeOption(field.name, v)}
            />
          ))}
        </div>
      )}
    </div>
  );
};

function orderTypes(types: string[]): string[] {
  const known = FORMAT_TYPES.filter((t) => types.includes(t)) as string[];
  return [...known, ...types.filter((t) => !known.includes(t)).sort()];
}

const inputClass =
  'w-full text-xs rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 px-1.5 py-1';

/** One option, drawn by its kind: a yes/no choice, a number, a choice, a list or a JSON object. */
export const OptionField = ({
  format,
  direction,
  info,
  value,
  sample = '',
  onChange,
}: {
  format: string;
  direction: Direction;
  info: FormatOptionInfo;
  value: unknown;
  sample?: string;
  onChange: (value: unknown) => void;
}) => {
  // The side is in the id: XML in and XML out both have a "root" field on the same page.
  const id = `opt-${direction}-${format}-${info.name}`;
  const label = (
    <label htmlFor={id} className="text-[11px] font-semibold text-slate-600 dark:text-slate-300" title={info.description}>
      {info.name}
    </label>
  );
  const hint = <p className="text-[10px] text-slate-400 leading-snug">{info.description}</p>;

  // Three ways, not a checkbox: some of these are on by default (an XML declaration, strict reading), and an empty
  // checkbox would read as "off" when it means "not set".
  if (info.kind === 'bool') {
    return (
      <div>
        {label}
        <select
          id={id}
          className={inputClass}
          value={value === true ? 'yes' : value === false ? 'no' : ''}
          onChange={(e) => onChange(e.target.value === 'yes' ? true : e.target.value === 'no' ? false : undefined)}
        >
          <option value="">(default)</option>
          <option value="yes">yes</option>
          <option value="no">no</option>
        </select>
        {hint}
      </div>
    );
  }

  if (info.kind === 'int') {
    return (
      <div>
        {label}
        <input
          id={id}
          type="number"
          className={inputClass}
          value={typeof value === 'number' ? value : ''}
          onChange={(e) => onChange(e.target.value === '' ? undefined : Math.trunc(Number(e.target.value)))}
        />
        {hint}
      </div>
    );
  }

  if (info.kind === 'stringList') {
    // E73-T10: on the input side, XML lists can be filled from the elements the sample repeats.
    const fill = format === 'xml' && info.name === 'arrays' && direction === 'input' ? () => repeatedPaths(sample) : undefined;
    return <ListOptionField id={id} label={label} hint={hint} value={value} onChange={onChange} fill={fill} />;
  }

  // E73-T11: EDI guides are picked from the bundled ones, plus guides of your own.
  if (format === 'edi' && info.name === 'guides') {
    return <EdiGuidesField id={id} label={label} hint={hint} value={value} onChange={onChange} />;
  }

  // E73-T10: a schema is a document, not a word: a text box and a file button.
  if (format === 'xml' && info.name === 'xsd') {
    return <XsdOptionField id={id} label={label} hint={hint} value={value} onChange={onChange} />;
  }

  // E73-T8: a positional layout (also the text format's fixed mode) gets the layout editor.
  if (info.name === 'layout' && (format === 'positional' || format === 'text')) {
    return <LayoutOptionField id={id} label={label} direction={direction} value={value} sample={sample} onChange={onChange} />;
  }

  if (info.kind === 'object' || info.kind === 'any') {
    return <JsonOptionField id={id} label={label} hint={hint} value={value} onChange={onChange} />;
  }

  const choices = OPTION_CHOICES[`${format}.${info.name}`];
  return (
    <div>
      {label}
      {choices ? (
        <select id={id} className={inputClass} value={typeof value === 'string' ? value : ''} onChange={(e) => onChange(e.target.value || undefined)}>
          <option value="">(default)</option>
          {choices.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
      ) : (
        <input id={id} className={inputClass} value={typeof value === 'string' ? value : ''} onChange={(e) => onChange(e.target.value)} />
      )}
      {hint}
    </div>
  );
};

/** Lists are typed comma separated; spaces around each item are dropped. */
export const splitList = (text: string): string[] =>
  text
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);

/** A list option typed comma separated. The box keeps the text as typed (a trailing comma included); the list is saved. */
const ListOptionField = ({
  id,
  label,
  hint,
  value,
  onChange,
  fill,
}: {
  id: string;
  label: ReactNode;
  hint: ReactNode;
  value: unknown;
  onChange: (value: unknown) => void;
  /** Proposes more items (null when it cannot read the sample). */
  fill?: () => string[] | null;
}) => {
  const [text, setText] = useState(() => (Array.isArray(value) ? value.join(', ') : ''));
  const [note, setNote] = useState('');
  const doFill = () => {
    const found = fill?.();
    if (found == null) {
      setNote('The Sample Input is not well-formed XML yet.');
      return;
    }
    const merged = mergeLists(splitList(text), found);
    setText(merged.join(', '));
    onChange(merged);
    setNote(found.length ? '' : 'The sample repeats no element under one parent.');
  };
  return (
    <div>
      <div className="flex items-center justify-between">
        {label}
        {fill && (
          <button
            type="button"
            onClick={doFill}
            className="text-[10px] font-semibold text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10"
            title="Add every element the Sample Input repeats under one parent"
          >
            Fill from sample
          </button>
        )}
      </div>
      {note && <p className="text-[10px] text-amber-600 dark:text-amber-400">{note}</p>}
      <input
        id={id}
        className={inputClass}
        placeholder="one, two, three"
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          onChange(splitList(e.target.value));
        }}
      />
      {hint}
    </div>
  );
};

/** An XSD as text: pasted, or read from a .xsd file. The service compiles it, so a broken one fails on Apply or Execute. */
const XsdOptionField = ({
  id,
  label,
  hint,
  value,
  onChange,
}: {
  id: string;
  label: ReactNode;
  hint: ReactNode;
  value: unknown;
  onChange: (value: unknown) => void;
}) => {
  const text = typeof value === 'string' ? value : '';
  const load = async (file: File | undefined) => {
    if (file) onChange((await file.text()) || undefined);
  };
  return (
    <div>
      <div className="flex items-center justify-between">
        {label}
        <label className="text-[10px] font-semibold text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10 cursor-pointer">
          Load .xsd
          <input
            type="file"
            accept=".xsd,.xml,application/xml,text/xml"
            className="sr-only"
            aria-label="Load an XSD file"
            onChange={(e) => {
              void load(e.target.files?.[0]);
              e.target.value = '';
            }}
          />
        </label>
      </div>
      <textarea
        id={id}
        rows={4}
        spellCheck={false}
        placeholder="<xs:schema ...>"
        className={`${inputClass} font-mono`}
        value={text}
        onChange={(e) => onChange(e.target.value || undefined)}
      />
      {text && <p className="text-[10px] text-slate-500">{text.length.toLocaleString()} characters of schema.</p>}
      {hint}
    </div>
  );
};

/** An object option (a positional layout, XML namespaces) as JSON text, kept as typed until it parses. */
const JsonOptionField = ({
  id,
  label,
  hint,
  value,
  onChange,
}: {
  id: string;
  label: ReactNode;
  hint: ReactNode;
  value: unknown;
  onChange: (value: unknown) => void;
}) => {
  const [text, setText] = useState(() => (value === undefined ? '' : JSON.stringify(value, null, 2)));
  const [error, setError] = useState('');

  const commit = (next: string) => {
    setText(next);
    if (next.trim() === '') {
      setError('');
      onChange(undefined);
      return;
    }
    try {
      onChange(JSON.parse(next));
      setError('');
    } catch (e: any) {
      setError(`Not valid JSON yet: ${e.message}`);
    }
  };

  return (
    <div>
      {label}
      <textarea
        id={id}
        rows={6}
        spellCheck={false}
        className={`${inputClass} font-mono ${error ? 'border-red-400 dark:border-red-500/60' : ''}`}
        value={text}
        onChange={(e) => commit(e.target.value)}
      />
      {error ? <p className="text-[10px] text-red-600 dark:text-red-400">{error}</p> : hint}
    </div>
  );
};


/** A layout: one line on what it holds, the layout editor, and its JSON for pasting a layout made elsewhere. */
const LayoutOptionField = ({
  id,
  label,
  direction,
  value,
  sample,
  onChange,
}: {
  id: string;
  label: ReactNode;
  direction: Direction;
  value: unknown;
  sample: string;
  onChange: (value: unknown) => void;
}) => {
  const [editing, setEditing] = useState(false);
  const [asJson, setAsJson] = useState(false);
  // Bumped when the editor closes, so the JSON box shows what was edited there.
  const [version, setVersion] = useState(0);
  const layout = asLayout(value);
  const save = (next: Layout) => {
    const empty = !next.records && (next.fields?.length ?? 0) === 0;
    onChange(empty ? undefined : next);
  };

  return (
    <div>
      {label}
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-[11px] text-slate-600 dark:text-slate-300" data-testid={`${id}-summary`}>
          {value === undefined ? 'No layout yet' : summarise(layout)}
        </span>
        <button
          type="button"
          onClick={() => setEditing(true)}
          className="text-[10px] font-semibold bg-indigo-600 hover:bg-indigo-700 text-white rounded px-2 py-0.5"
        >
          Edit layout
        </button>
        <button
          type="button"
          onClick={() => setAsJson(!asJson)}
          className="text-[10px] font-semibold text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10"
          aria-expanded={asJson}
        >
          {asJson ? 'Hide JSON' : 'JSON'}
        </button>
      </div>
      {asJson && <JsonOptionField key={version} id={`${id}-json`} label={null} hint={null} value={value} onChange={onChange} />}
      <p className="text-[10px] text-slate-400 leading-snug">Where each field sits: its width (or a length written before it), type and padding.</p>
      {editing && (
        <LayoutEditor
          title={`${direction === 'input' ? 'Input' : 'Output'} layout`}
          layout={layout}
          sample={sample}
          onChange={save}
          onClose={() => {
            setEditing(false);
            setVersion((v) => v + 1);
          }}
        />
      )}
    </div>
  );
};
