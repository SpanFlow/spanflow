import { useEffect, useState } from 'react';
import { useStore } from '../store/useStore';
import { compileGraphToDsl, compileFunctionDoc } from '../nodes/compile';
import { EXECUTE_PATH } from '../config';
import { Play, PanelRightClose, PanelRightOpen, Bug, X, Maximize2, Wand2 } from 'lucide-react';
import { DetailBlock, ExpandModal } from './DetailBlock';
import { FormatPicker } from './FormatPicker';
import { ufmappingApi } from '../../lib/ufmappingApi';
import { graphFormats, inputFormatOf, isJsonType } from '../formats/formats';
import { decodeSample, problemMessage, runMapping, type RunResult } from '../formats/formatsApi';

/** What the result box shows after a run with formats: the written output, or the JSON on either side of the mapping. */
type ResultView = 'output' | 'mapped' | 'input';

const VIEW_LABELS: Record<ResultView, string> = { output: 'Output', mapped: 'Mapped JSON', input: 'Seen as JSON' };

export const PreviewPanel = () => {
  const { nodes, edges } = useStore();
  const inputText = useStore((s) => s.sampleInput);
  const setInputText = useStore((s) => s.setSampleInput);
  const importSource = useStore((s) => s.importSource);
  const runDebug = useStore((s) => s.runDebug);
  const clearDebug = useStore((s) => s.clearDebug);
  const debugActive = useStore((s) => s.debug.active);
  const debugRunning = useStore((s) => s.debug.running);
  const [dslOutput, setDslOutput] = useState<string>('');
  const [executionResult, setExecutionResult] = useState<string>('');
  const [run, setRun] = useState<RunResult | null>(null);
  const [view, setView] = useState<ResultView>('output');
  const [isLoading, setIsLoading] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [inputError, setInputError] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [outputExpanded, setOutputExpanded] = useState(false);

  const buildFunctions = useStore((s) => s.buildFunctions);
  const isFunctionDoc = useStore((s) => s.docs.find((d) => d.id === s.activeDocId)?.kind === 'function');

  // E73: the formats live on the Source (input) and Target (output) nodes. A function document has neither.
  const sourceNode = nodes.find((n) => n.type === 'source');
  const targetNode = nodes.find((n) => n.type === 'target');
  const inputFormat = inputFormatOf(nodes);
  const rawSample = !isFunctionDoc && !isJsonType(inputFormat);
  const usesFormats = !isFunctionDoc && graphFormats(nodes) !== undefined;

  // A function document is compiled as its Return (parameters bound to input.<name>).
  const compileActive = (n: typeof nodes, e: typeof edges) =>
    isFunctionDoc ? compileFunctionDoc(n, e, buildFunctions()) : compileGraphToDsl(n, e, buildFunctions());

  useEffect(() => {
    const compile = setTimeout(() => {
      setDslOutput(JSON.stringify(compileActive(nodes, edges), null, 2));
    }, 300);
    return () => clearTimeout(compile);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodes, edges, buildFunctions, isFunctionDoc]);

  // Rebuild the Input JSON node's fields from whatever is in the Sample Input box, so a sample
  // typed or pasted here reaches the canvas the same way the "Source" file picker does — without
  // going to disk for a file, and without hand-adding each field. A sample in another format is
  // first read by that format on the service (E73), and the box keeps the raw text.
  const applyToInput = async () => {
    if (rawSample) {
      try {
        importSource(inputText, await decodeSample(inputFormat, inputText));
        setInputError('');
      } catch (e: any) {
        setInputError(problemMessage(e));
      }
      return;
    }
    try {
      importSource(inputText);
      setInputError('');
    } catch (e: any) {
      setInputError(`Invalid JSON: ${e.message}`);
    }
  };

  const executeMapping = async () => {
    // A JSON sample is parsed first so we fail fast on bad JSON; another format is read by the service.
    let inputObject: unknown;
    if (!rawSample) {
      try {
        inputObject = JSON.parse(inputText);
        setInputError('');
      } catch (e: any) {
        setInputError(`Invalid JSON: ${e.message}`);
        setExecutionResult('// Fix the Sample Input JSON before executing.');
        setRun(null);
        return;
      }
    }

    setIsLoading(true);
    try {
      const dsl = compileActive(nodes, edges);
      if (usesFormats) {
        // E73: decode, map, encode in one call; the JSON on both sides comes back for the other views.
        const result = await runMapping(dsl, inputText, inputFormat);
        setRun(result);
        setExecutionResult(result.output);
      } else {
        const res = await ufmappingApi.post(EXECUTE_PATH, { mapping: dsl, inputObject });
        setRun(null);
        setExecutionResult(JSON.stringify(res.data, null, 2));
      }
      setErrorMessage('');
      setInputError('');
    } catch (err: any) {
      // Surface the engine's human-readable message instead of raw JSON.
      setExecutionResult('');
      setRun(null);
      setErrorMessage(problemMessage(err));
    } finally {
      setIsLoading(false);
    }
  };

  // The text in the result box for the chosen view, and its badge.
  const shown = !run
    ? { text: executionResult, format: 'json' }
    : view === 'mapped'
      ? { text: JSON.stringify(run.mapped, null, 2), format: 'json' }
      : view === 'input'
        ? { text: JSON.stringify(run.decoded, null, 2), format: 'json' }
        : { text: run.output, format: run.outputFormat };

  if (collapsed) {
    return (
      <div className="w-10 bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 flex flex-col items-center py-3 shrink-0 z-10 transition-colors h-full">
        <button
          onClick={() => setCollapsed(false)}
          className="p-2 rounded-md text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title="Show preview"
        >
          <PanelRightOpen size={18} />
        </button>
      </div>
    );
  }

  return (
    <div className="w-80 bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 flex flex-col p-4 shrink-0 shadow-xl z-10 transition-colors h-full">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-sm font-bold text-slate-800 dark:text-slate-100 flex items-center gap-2">
          Real-Time Preview
        </h2>
        <div className="flex items-center gap-1">
          {debugActive && (
            <button
              onClick={clearDebug}
              className="p-1.5 rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
              title="Hide debug values"
            >
              <X size={15} />
            </button>
          )}
          <button
            onClick={() => runDebug()}
            disabled={debugRunning}
            className={`rounded-full p-2 transition-all flex items-center justify-center disabled:opacity-50 ${
              debugActive
                ? 'bg-amber-500 hover:bg-amber-600 text-white'
                : 'text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800'
            }`}
            title="Run debug: show each node's request, response, and timing"
          >
            <Bug size={14} className={debugRunning ? 'animate-pulse' : ''} />
          </button>
          <button
            onClick={executeMapping}
            disabled={isLoading}
            className="bg-indigo-600 hover:bg-indigo-700 text-white rounded-full p-2 transition-all flex items-center justify-center disabled:opacity-50"
            title="Execute Engine"
          >
            <Play size={14} className={isLoading ? "animate-pulse" : ""} fill="currentColor"/>
          </button>
          <button
            onClick={() => setCollapsed(true)}
            className="p-1.5 rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
            title="Hide panel"
          >
            <PanelRightClose size={16} />
          </button>
        </div>
      </div>

      <div className="flex-1 flex flex-col gap-4 overflow-y-auto">
        <DetailBlock
          title="Generated DSL (Mapping Definition)"
          rootClassName="flex flex-col flex-1 min-h-[90px] shrink-0"
          className="flex-1"
        >
          {dslOutput || '// Connect nodes to generate DSL...'}
        </DetailBlock>

        {!isFunctionDoc && <FormatPicker direction="input" node={sourceNode} />}

        <DetailBlock
          title="Sample Input"
          format={inputFormat.type}
          rootClassName="flex flex-col flex-[1.6] min-h-[140px] shrink-0"
          className="flex-1 min-h-[100px]"
          tone={inputError ? 'error' : undefined}
          editable
          onChange={setInputText}
          hint={inputError}
          action={
            <button
              onClick={applyToInput}
              className="text-[10px] font-semibold text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-1"
              title={
                rawSample
                  ? 'Read this sample with its format and rebuild the Input JSON node\'s fields from what it gives'
                  : 'Rebuild the Input JSON node\'s fields from this sample: no file picker, no adding fields by hand'
              }
            >
              <Wand2 size={11} /> Apply to Input JSON
            </button>
          }
        >
          {inputText}
        </DetailBlock>

        {!isFunctionDoc && <FormatPicker direction="output" node={targetNode} />}

        <div className="flex flex-col flex-1 min-h-[90px] shrink-0">
          <div className="flex items-center justify-between mb-1">
            <label className="text-xs font-semibold text-slate-500 dark:text-slate-400">Output Result</label>
            <button
              onClick={() => setOutputExpanded(true)}
              className="text-slate-400 hover:text-indigo-500 p-0.5 rounded"
              title="View full content"
            >
              <Maximize2 size={11} />
            </button>
          </div>
          {run && !errorMessage && (
            <div className="flex gap-1 mb-1" role="tablist" aria-label="Result view">
              {(Object.keys(VIEW_LABELS) as ResultView[]).map((v) => (
                <button
                  key={v}
                  role="tab"
                  aria-selected={view === v}
                  onClick={() => setView(v)}
                  className={`text-[10px] font-semibold px-2 py-0.5 rounded ${
                    view === v
                      ? 'bg-indigo-600 text-white'
                      : 'text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800'
                  }`}
                >
                  {VIEW_LABELS[v]}
                </button>
              ))}
            </div>
          )}
          <div
            onClick={() => setOutputExpanded(true)}
            className={`flex-1 relative bg-slate-100 dark:bg-slate-950/80 border rounded-lg overflow-hidden flex flex-col shadow-inner cursor-pointer ${errorMessage ? 'border-red-400 dark:border-red-500/50' : 'border-slate-300 dark:border-slate-700'}`}
          >
             {errorMessage ? (
               <>
                 <div className="bg-red-500/10 border-b border-red-500/20 px-3 py-1 flex items-center gap-2">
                   <div className="w-2 h-2 rounded-full bg-red-500"></div>
                   <span className="text-[10px] font-bold text-red-600 dark:text-red-400 uppercase tracking-widest">Error</span>
                 </div>
                 <div className="p-3 text-xs font-medium text-red-700 dark:text-red-300 overflow-auto flex-1 whitespace-pre-wrap">
                   {errorMessage}
                 </div>
               </>
             ) : (
               <>
                 <div className="bg-emerald-500/10 border-b border-emerald-500/20 px-3 py-1 flex items-center gap-2">
                   <div className="w-2 h-2 rounded-full bg-emerald-500"></div>
                   <span className="text-[10px] font-bold text-emerald-600 dark:text-emerald-400 uppercase tracking-widest">Engine Result</span>
                   {run && <span className="ml-auto text-[9px] font-mono uppercase text-emerald-600/70 dark:text-emerald-400/70">{shown.format}</span>}
                 </div>
                 <pre className="p-3 text-xs font-mono text-emerald-700 dark:text-emerald-300 overflow-auto flex-1 whitespace-pre-wrap">
                   {shown.text || '// Press execute to view result...'}
                 </pre>
               </>
             )}
          </div>
        </div>

        {outputExpanded && (
          <ExpandModal
            title={run ? `Output Result: ${VIEW_LABELS[view]}` : 'Output Result'}
            format={shown.format}
            tone={errorMessage ? 'error' : undefined}
            content={errorMessage || shown.text || '// Press execute to view result...'}
            onClose={() => setOutputExpanded(false)}
          />
        )}
      </div>
    </div>
  );
};
