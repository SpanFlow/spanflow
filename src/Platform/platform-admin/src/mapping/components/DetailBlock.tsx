import { useState } from 'react';
import type { ReactNode } from 'react';
import { Maximize2, X, Copy, Check } from 'lucide-react';
import JsonViewer from '../../components/JsonViewer';
import { copyText } from '../../lib/clipboard';

/** Shared sizing for every resizable modal: a native browser resize handle
 *  (bottom-right corner drag) bounded so it can't grow past the viewport.
 *  `overflow-auto` is required for the handle to render and doubles as a
 *  fallback scrollbar if a child can't shrink. Callers add their own
 *  `min-w-*`/`min-h-*` sized to their content (a two-pane modal needs a
 *  bigger floor than a single text block). */
export const RESIZABLE_MODAL = 'resize overflow-auto max-w-[92vw] max-h-[92vh]';

/** Full-content viewer: the expand button on a DetailBlock (or any custom
 *  trigger) opens this so nothing gets lost in a small inline box. */
export const ExpandModal = ({
  title,
  format = 'json',
  tone,
  content,
  onClose,
}: {
  title: string;
  format?: string;
  tone?: 'error';
  content: string;
  onClose: () => void;
}) => {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await copyText(content);
      setCopied(true);
      setTimeout(() => setCopied(false), 1200);
    } catch {
      /* clipboard unavailable — ignore */
    }
  };

  return (
    <div className="fixed inset-0 z-[60] bg-black/50 flex items-center justify-center p-6" onClick={onClose}>
      <div
        className={`bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-[min(48rem,92vw)] h-[70vh] min-w-[320px] min-h-[220px] flex flex-col border border-slate-200 dark:border-slate-700 ${RESIZABLE_MODAL}`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0">
          <div className="flex items-center gap-2">
            <h3 className="font-bold text-slate-800 dark:text-white text-sm">{title}</h3>
            <span className="text-[10px] font-mono uppercase text-slate-400">{format}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <button
              onClick={copy}
              className="text-xs font-medium text-slate-500 hover:text-indigo-600 dark:hover:text-indigo-400 px-2 py-1 rounded-md hover:bg-slate-100 dark:hover:bg-slate-800 flex items-center gap-1"
              title="Copy to clipboard"
            >
              {copied ? <Check size={12} /> : <Copy size={12} />} {copied ? 'Copied' : 'Copy'}
            </button>
            <button onClick={onClose} className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500">
              <X size={16} />
            </button>
          </div>
        </div>
        {tone !== 'error' && format === 'json' ? (
          <div className="min-h-0 flex-1 overflow-auto p-4">
            <JsonViewer text={content} maxHeightClass="max-h-[60vh]" />
          </div>
        ) : (
          <pre
            className={`flex-1 min-h-0 overflow-auto p-4 text-xs leading-relaxed whitespace-pre-wrap font-mono ${
              tone === 'error' ? 'text-red-700 dark:text-red-300' : 'text-slate-800 dark:text-slate-200'
            }`}
          >
            {content}
          </pre>
        )}
      </div>
    </div>
  );
};

/**
 * A labeled, monospace content block (JSON today; format is just a badge so
 * XML/other formats can be added later without changing callers). Content is
 * capped at a small height inline by default — the expand button opens the
 * same text in a large, scrollable, copyable modal so nothing gets lost.
 */
interface DetailBlockProps {
  title: string;
  children: string;
  tone?: 'error';
  format?: string;
  /** Wrapper classes, e.g. `flex flex-col flex-1 min-h-[30%]` to fill a flex parent. */
  rootClassName?: string;
  /** Sizing classes for the inline box; defaults to a capped, scrollable height. */
  className?: string;
  /** Renders an editable textarea instead of read-only `<pre>`, inline and in the modal. */
  editable?: boolean;
  onChange?: (value: string) => void;
  /** Small helper/error text shown under the inline box (e.g. JSON parse errors). */
  hint?: string;
  /** Optional control rendered in the header, left of the expand button (e.g. "apply this JSON"). */
  action?: ReactNode;
}

export const DetailBlock = ({
  title,
  children,
  tone,
  format = 'json',
  rootClassName,
  className,
  editable,
  onChange,
  hint,
  action,
}: DetailBlockProps) => {
  const [open, setOpen] = useState(false);

  const toneClasses =
    tone === 'error'
      ? 'bg-red-50 dark:bg-red-500/10 border-red-200 dark:border-red-500/30 text-red-700 dark:text-red-300'
      : 'bg-slate-50 dark:bg-slate-950 border-slate-200 dark:border-slate-800 text-slate-800 dark:text-slate-200';

  const boxClass = `text-[11px] leading-snug rounded-md p-2 overflow-auto whitespace-pre-wrap border ${toneClasses} ${className ?? 'max-h-64'}`;

  return (
    <div className={rootClassName}>
      <div className="flex items-center justify-between mb-1">
        <div className="flex items-center gap-1.5">
          <span className="text-[10px] font-bold uppercase tracking-wide text-slate-400">{title}</span>
          <span className="text-[9px] font-mono uppercase text-slate-300 dark:text-slate-600">{format}</span>
        </div>
        <div className="flex items-center gap-1.5">
          {action}
          <button
            onClick={() => setOpen(true)}
            className="text-slate-400 hover:text-indigo-500 p-0.5 rounded"
            title="View full content"
          >
            <Maximize2 size={11} />
          </button>
        </div>
      </div>

      {editable ? (
        <textarea
          value={children}
          onChange={(e) => onChange?.(e.target.value)}
          spellCheck={false}
          className={`${boxClass} resize-none outline-none focus:ring-1 focus:ring-indigo-500 font-mono`}
        />
      ) : (
        <pre className={`${boxClass} cursor-pointer`} onClick={() => setOpen(true)}>
          {children}
        </pre>
      )}
      {hint && <span className="text-[10px] text-red-500 mt-1 block">{hint}</span>}

      {open &&
        (editable ? (
          <div className="fixed inset-0 z-[60] bg-black/50 flex items-center justify-center p-6" onClick={() => setOpen(false)}>
            <div
              className={`bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-[min(48rem,92vw)] h-[70vh] min-w-[320px] min-h-[220px] flex flex-col border border-slate-200 dark:border-slate-700 ${RESIZABLE_MODAL}`}
              onClick={(e) => e.stopPropagation()}
            >
              <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0">
                <div className="flex items-center gap-2">
                  <h3 className="font-bold text-slate-800 dark:text-white text-sm">{title}</h3>
                  <span className="text-[10px] font-mono uppercase text-slate-400">{format}</span>
                </div>
                <button
                  onClick={() => setOpen(false)}
                  className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500"
                >
                  <X size={16} />
                </button>
              </div>
              <textarea
                value={children}
                onChange={(e) => onChange?.(e.target.value)}
                spellCheck={false}
                className="flex-1 min-h-0 overflow-auto p-4 text-xs leading-relaxed font-mono text-slate-800 dark:text-slate-200 bg-transparent outline-none resize-none"
              />
            </div>
          </div>
        ) : (
          <ExpandModal title={title} format={format} tone={tone} content={children} onClose={() => setOpen(false)} />
        ))}
    </div>
  );
};
