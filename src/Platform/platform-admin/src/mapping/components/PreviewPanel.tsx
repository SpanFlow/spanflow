import { useEffect, useState } from 'react';
import { useStore } from '../store/useStore';
import { compileGraphToDsl, compileFunctionDoc } from '../nodes/compile';
import { EXECUTE_PATH } from '../config';
import { Play, PanelRightClose, PanelRightOpen, Bug, X, Maximize2, Wand2 } from 'lucide-react';
import { DetailBlock, ExpandModal } from './DetailBlock';
import { ufmappingApi } from '../../lib/ufmappingApi';

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
  const [isLoading, setIsLoading] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [inputError, setInputError] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [outputExpanded, setOutputExpanded] = useState(false);

  const buildFunctions = useStore((s) => s.buildFunctions);
  const isFunctionDoc = useStore((s) => s.docs.find((d) => d.id === s.activeDocId)?.kind === 'function');

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
  // going to disk for a file, and without hand-adding each field.
  const applyToInput = () => {
    try {
      importSource(inputText);
      setInputError('');
    } catch (e: any) {
      setInputError(`Invalid JSON: ${e.message}`);
    }
  };

  const executeMapping = async () => {
    // Parse the (editable) sample input first so we fail fast on bad JSON.
    let inputObject: unknown;
    try {
      inputObject = JSON.parse(inputText);
      setInputError('');
    } catch (e: any) {
      setInputError(`Invalid JSON: ${e.message}`);
      setExecutionResult('// Fix the Sample Input JSON before executing.');
      return;
    }

    setIsLoading(true);
    try {
      const dsl = compileActive(nodes, edges);
      const res = await ufmappingApi.post(EXECUTE_PATH, {
        mapping: dsl,
        inputObject,
      });
      setErrorMessage('');
      setExecutionResult(JSON.stringify(res.data, null, 2));
    } catch (err: any) {
      // Surface the engine's human-readable message instead of raw JSON.
      setExecutionResult('');
      const apiError = err?.response?.data;
      if (apiError) {
        setErrorMessage(apiError.Error ?? apiError.error ?? JSON.stringify(apiError));
      } else {
        setErrorMessage(`Couldn't reach the mapping engine at ${ufmappingApi.defaults.baseURL}: it may be stopped or restarting. Try again in a few seconds.`);
      }
    } finally {
      setIsLoading(false);
    }
  };

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

        <DetailBlock
          title="Sample Input"
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
              title="Rebuild the Input JSON node's fields from this sample: no file picker, no adding fields by hand"
            >
              <Wand2 size={11} /> Apply to Input JSON
            </button>
          }
        >
          {inputText}
        </DetailBlock>

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
                 </div>
                 <pre className="p-3 text-xs font-mono text-emerald-700 dark:text-emerald-300 overflow-auto flex-1">
                   {executionResult || '// Press execute to view result...'}
                 </pre>
               </>
             )}
          </div>
        </div>

        {outputExpanded && (
          <ExpandModal
            title="Output Result"
            tone={errorMessage ? 'error' : undefined}
            content={errorMessage || executionResult || '// Press execute to view result...'}
            onClose={() => setOutputExpanded(false)}
          />
        )}
      </div>
    </div>
  );
};
