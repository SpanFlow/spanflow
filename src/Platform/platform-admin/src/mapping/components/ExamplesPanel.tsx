import { useState } from 'react';
import { ufmappingApi } from '../../lib/ufmappingApi';
import { X, Play, Download, Save, Trash2, AlertTriangle } from 'lucide-react';
import { RECIPES, RECIPE_CATEGORIES, type Recipe } from '../examples/recipes';
import { FUNCTION_USAGE } from '../examples/functionUsage';
import { FUNCTION_DEFINITIONS } from '../examples/functionDefinitions';
import { buildExampleGraph } from '../examples/buildGraph';
import { dslToGraph } from '../nodes/decompile';
import { compileGraphToDsl } from '../engine/dslBuilder';
import { inferStructure } from '../schema/inferFields';
import { useStore } from '../store/useStore';
import { DetailBlock, RESIZABLE_MODAL } from './DetailBlock';
import { EXECUTE_PATH } from '../config';
import type { AppNode } from '../store/useStore';
import type { Node } from '@xyflow/react';

/** Stable, key-order-independent stringify so we can compare two DSLs. */
const stable = (v: any): string => {
  if (Array.isArray(v)) return `[${v.map(stable).join(',')}]`;
  if (v && typeof v === 'object') {
    return `{${Object.keys(v).sort().map((k) => `${k}:${stable(v[k])}`).join(',')}}`;
  }
  return JSON.stringify(v);
};
const sameMappings = (a: any, b: any) => {
  const norm = (d: any) => [...(d?.target?.mappings ?? [])].sort((x, y) => String(x.target).localeCompare(String(y.target)));
  return stable(norm(a)) === stable(norm(b));
};

/** Give a decompiled source/target node the full typed structure from a sample. */
const enrich = (n: Node, sample: unknown): Node => {
  const s = inferStructure(sample);
  const existing = (n.data?.fields as string[] | undefined) ?? [];
  const fields = [...new Set([...s.fields, ...existing])];
  return { ...n, data: { ...n.data, fields: fields.length ? fields : existing, lists: s.lists, types: s.types, rootArray: s.rootArray } };
};

// Deliberately NOT project-scoped, unlike the canvas and the History scenarios (E16-T5): these are
// personal snippets someone keeps to hand, not work that belongs to one project.
const USER_KEY = 'maplite_user_examples';
const loadUser = (): Recipe[] => {
  try {
    return JSON.parse(localStorage.getItem(USER_KEY) || '[]');
  } catch {
    return [];
  }
};
const saveUser = (list: Recipe[]) => localStorage.setItem(USER_KEY, JSON.stringify(list));

const pretty = (v: unknown) => JSON.stringify(v, null, 2);

export const ExamplesPanel = ({ onClose }: { onClose: () => void }) => {
  const openInNewTab = useStore((s) => s.openInNewTab);
  const ensureFunction = useStore((s) => s.ensureFunction);
  const switchTab = useStore((s) => s.switchTab);
  const nodes = useStore((s) => s.nodes);
  const edges = useStore((s) => s.edges);
  const sampleInput = useStore((s) => s.sampleInput);

  const [userExamples, setUserExamples] = useState<Recipe[]>(loadUser);
  const all = [...RECIPES, ...FUNCTION_DEFINITIONS, ...FUNCTION_USAGE, ...userExamples];
  const [selectedId, setSelectedId] = useState(all[0]?.id);
  const selected = all.find((r) => r.id === selectedId) ?? all[0];

  const [runResult, setRunResult] = useState('');
  const [running, setRunning] = useState(false);

  const categories = [
    ...RECIPE_CATEGORIES,
    'Function library',
    'Using functions',
    ...(userExamples.length ? ['My examples'] : []),
  ];

  const load = (r: Recipe) => {
    const input = pretty(r.input);

    // A "using a function" example: seed the function, then open a mapping
    // that calls it (the functionId matches the seeded function document).
    if (r.functionName && r.buildMapping) {
      const fnId = ensureFunction(r.functionName);
      const g = r.buildMapping(fnId);
      openInNewTab(r.title, g.nodes as AppNode[], g.edges, input);
      onClose();
      return;
    }

    // A function-library example: seed just that one function (reusing its
    // doc if already loaded) and switch to its own tab — no mapping wrapper.
    if (r.functionName && !r.buildMapping) {
      const fnId = ensureFunction(r.functionName);
      if (fnId) switchTab(fnId);
      onClose();
      return;
    }

    // Try a faithful node graph; only use it if it recompiles to the same DSL.
    // Array-root targets have no `mappings`, so build structures instead.
    let graph: { nodes: Node[]; edges: any[] } | null = null;
    if (r.dsl.target.type !== 'array' && r.dsl.target.mappings && r.dsl.target.mappings.length) {
      try {
        const g = dslToGraph(r.dsl);
        if (sameMappings(compileGraphToDsl(g.nodes, g.edges), r.dsl)) graph = g;
      } catch {
        /* fall through to structures */
      }
    }

    if (graph) {
      // Enrich source/target with the typed structures from the samples.
      const nodes = graph.nodes.map((n) =>
        n.type === 'source' ? enrich(n, r.input) : n.type === 'target' ? enrich(n, r.output) : n,
      );
      openInNewTab(r.title, nodes as AppNode[], graph.edges, input);
    } else {
      // Lossy or array-root mapping: build a typed, wired Source -> Target graph.
      const g = buildExampleGraph(r);
      openInNewTab(r.title, g.nodes as AppNode[], g.edges, input);
    }
    onClose();
  };

  const run = async (r: Recipe) => {
    setRunning(true);
    setRunResult('');
    try {
      const res = await ufmappingApi.post(EXECUTE_PATH, { mapping: r.dsl, inputObject: r.input });
      setRunResult(pretty(res.data));
    } catch (e: any) {
      const d = e?.response?.data;
      setRunResult(d ? `Error: ${d.Error ?? d.error ?? pretty(d)}` : `Couldn't reach the mapping engine at ${ufmappingApi.defaults.baseURL}: it may be stopped or restarting. Try again in a few seconds.`);
    } finally {
      setRunning(false);
    }
  };

  const saveCurrent = () => {
    const title = window.prompt('Name this example:');
    if (!title) return;
    let input: unknown = {};
    try {
      input = JSON.parse(sampleInput);
    } catch {
      /* keep {} */
    }
    const dsl = compileGraphToDsl(nodes as any, edges);
    const rec: Recipe = {
      id: `user-${Date.now()}`,
      title,
      category: 'My examples',
      description: 'Saved from your canvas. Click Run to see the output.',
      input,
      dsl,
      output: null,
    };
    const next = [...userExamples, rec];
    setUserExamples(next);
    saveUser(next);
    setSelectedId(rec.id);
    setRunResult('');
  };

  const deleteUser = (id: string) => {
    const next = userExamples.filter((r) => r.id !== id);
    setUserExamples(next);
    saveUser(next);
    if (selectedId === id) setSelectedId(RECIPES[0].id);
  };

  const isUser = selected?.category === 'My examples';

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4" onClick={onClose}>
      <div
        className={`bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-[min(64rem,92vw)] h-[80vh] min-w-[640px] min-h-[360px] flex flex-col border border-slate-200 dark:border-slate-700 ${RESIZABLE_MODAL}`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0">
          <h2 className="font-bold text-slate-800 dark:text-white">Examples &amp; recipes</h2>
          <div className="flex items-center gap-2">
            <button
              onClick={saveCurrent}
              className="text-xs font-semibold text-emerald-600 dark:text-emerald-400 px-2.5 py-1.5 rounded-full border border-emerald-200 dark:border-emerald-500/30 hover:bg-emerald-50 dark:hover:bg-emerald-500/10 flex items-center gap-1"
              title="Save the current canvas as a reusable example"
            >
              <Save size={13} /> Save current
            </button>
            <button onClick={onClose} className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500">
              <X size={18} />
            </button>
          </div>
        </div>

        <div className="flex flex-1 min-h-0">
          {/* Recipe list */}
          <div className="w-56 shrink-0 border-r border-slate-200 dark:border-slate-800 overflow-y-auto p-2">
            {categories.map((cat) => (
              <div key={cat} className="mb-2">
                <div className="text-[10px] font-bold uppercase tracking-wide text-slate-400 px-2 py-1">{cat}</div>
                {all
                  .filter((r) => r.category === cat)
                  .map((r) => (
                    <button
                      key={r.id}
                      onClick={() => {
                        setSelectedId(r.id);
                        setRunResult('');
                      }}
                      className={`w-full text-left text-xs px-2 py-1.5 rounded-md flex items-center justify-between group ${
                        selectedId === r.id
                          ? 'bg-indigo-50 dark:bg-indigo-500/15 text-indigo-700 dark:text-indigo-300 font-semibold'
                          : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800'
                      }`}
                    >
                      <span className="truncate">{r.title}</span>
                      {r.category === 'My examples' && (
                        <Trash2
                          size={13}
                          className="opacity-0 group-hover:opacity-100 text-slate-400 hover:text-red-500 shrink-0"
                          onClick={(e) => {
                            e.stopPropagation();
                            deleteUser(r.id);
                          }}
                        />
                      )}
                    </button>
                  ))}
              </div>
            ))}
          </div>

          {/* Detail */}
          {selected && (
            <div className="flex-1 min-w-0 overflow-y-auto p-4 space-y-3">
              <div>
                <h3 className="font-bold text-slate-800 dark:text-white">{selected.title}</h3>
                <p className="text-sm text-slate-500 dark:text-slate-400">{selected.description}</p>
              </div>

              <div className="flex gap-2">
                <button
                  onClick={() => load(selected)}
                  className="text-xs font-semibold text-indigo-600 dark:text-indigo-400 px-3 py-1.5 rounded-full border border-indigo-200 dark:border-indigo-500/30 hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-1"
                >
                  <Download size={13} /> Load into canvas
                </button>
                <button
                  onClick={() => run(selected)}
                  disabled={running}
                  className="text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-700 px-3 py-1.5 rounded-full flex items-center gap-1 disabled:opacity-50"
                >
                  <Play size={13} /> {running ? 'Running…' : 'Run live'}
                </button>
              </div>

              <DetailBlock title="Input">{pretty(selected.input)}</DetailBlock>
              <DetailBlock title="Mapping (DSL)">{pretty(selected.dsl)}</DetailBlock>

              {selected.output !== null &&
                (selected.throws ? (
                  <div>
                    <div className="text-[10px] font-bold uppercase tracking-wide text-slate-400 mb-1">Raises</div>
                    <div className="flex items-center gap-1.5 text-xs text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-500/10 rounded-md px-2 py-1.5">
                      <AlertTriangle size={13} /> {String(selected.output)}
                    </div>
                  </div>
                ) : (
                  <DetailBlock title="Output">{pretty(selected.output)}</DetailBlock>
                ))}

              {runResult && <DetailBlock title="Live result">{runResult}</DetailBlock>}

              {isUser && (
                <p className="text-[11px] text-slate-400">Saved locally in this browser.</p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
