import { useEffect, useState, type ReactNode } from 'react';
import { useEdiGuides } from '../formats/formatsApi';
import { readGuides, writeGuides, type GuidesChoice } from '../formats/ediGuides';

const box =
  'w-full text-xs rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 px-1.5 py-1';

/**
 * E73-T11: which EDI guides name and loop the messages: all the bundled ones, some of them, and guides of your own
 * (JSON, for a partner's variant). Without any, EDI reads as numbered segments (level 1).
 */
export const EdiGuidesField = ({
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
  const { guides, status, load } = useEdiGuides();
  useEffect(() => {
    if (status === 'idle') void load();
  }, [status, load]);

  const choice = readGuides(value);
  const [ownText, setOwnText] = useState(() => (choice.own.length ? JSON.stringify(choice.own.length === 1 ? choice.own[0] : choice.own, null, 2) : ''));
  const [ownError, setOwnError] = useState('');

  // "bundled" does not keep the names picked one by one, so they are remembered here for turning "all" off again.
  const [picked, setPicked] = useState<string[]>(choice.names);

  const save = (next: GuidesChoice) => onChange(writeGuides(next));

  const toggle = (name: string, on: boolean) => {
    const names = on ? [...choice.names, name] : choice.names.filter((n) => n !== name);
    setPicked(names);
    save({ ...choice, names });
  };

  const toggleAll = (on: boolean) => save({ ...choice, all: on, names: on ? choice.names : picked });

  const commitOwn = (text: string) => {
    setOwnText(text);
    if (text.trim() === '') {
      setOwnError('');
      save({ ...choice, own: [] });
      return;
    }
    try {
      const parsed = JSON.parse(text);
      const own = (Array.isArray(parsed) ? parsed : [parsed]).filter((g) => g && typeof g === 'object');
      setOwnError('');
      save({ ...choice, own });
    } catch (e: any) {
      setOwnError(`Not valid JSON yet: ${e.message}`);
    }
  };

  return (
    <div>
      {label}
      <div id={id} role="group" aria-label="EDI guides" className="flex flex-col gap-1 rounded border border-slate-200 dark:border-slate-800 p-1.5">
        <label className="text-[11px] flex items-center gap-1.5 text-slate-700 dark:text-slate-200 font-semibold">
          <input type="checkbox" checked={choice.all} onChange={(e) => toggleAll(e.target.checked)} />
          All bundled guides
        </label>
        {status === 'loading' && <p className="text-[10px] text-slate-400">Loading the bundled guides...</p>}
        {status === 'unavailable' && <p className="text-[10px] text-amber-600 dark:text-amber-400">This mapping service does not list its guides.</p>}
        {(guides ?? []).map((g) => (
          <label key={g.name} className={`text-[11px] flex items-center gap-1.5 ${choice.all ? 'text-slate-400' : 'text-slate-700 dark:text-slate-200'}`}>
            <input
              type="checkbox"
              disabled={choice.all}
              checked={choice.all || choice.names.includes(g.name)}
              onChange={(e) => toggle(g.name, e.target.checked)}
            />
            <span className="font-mono">{g.message}</span>
            <span className="truncate">{g.title}</span>
            <span className="ml-auto text-[9px] uppercase text-slate-400">{g.standard} {g.version}</span>
          </label>
        ))}
      </div>
      <label htmlFor={`${id}-own`} className="text-[10px] font-semibold text-slate-500 mt-1 block">
        Your own guides (JSON, used first)
      </label>
      <textarea
        id={`${id}-own`}
        rows={3}
        spellCheck={false}
        placeholder='{ "name": "acme-850", "standard": "x12", "message": "850", "segments": [ ... ] }'
        className={`${box} font-mono ${ownError ? 'border-red-400 dark:border-red-500/60' : ''}`}
        value={ownText}
        onChange={(e) => commitOwn(e.target.value)}
      />
      {ownError ? <p className="text-[10px] text-red-600 dark:text-red-400">{ownError}</p> : hint}
    </div>
  );
};
