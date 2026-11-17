// E71 libraries playground: a node canvas. Each node is one call to this server, which runs one of the open-source
// libraries; the page only wires the calls together. Plain script, no dependencies, nothing loaded from anywhere else
// (the page's CSP says so, which is also why every position is set through element.style, never a style attribute).
'use strict';

const $ = (id) => document.getElementById(id);
const pretty = (value) => JSON.stringify(value, null, 2);
const uid = () => Math.random().toString(36).slice(2, 10);
const SVG = 'http://www.w3.org/2000/svg';
const store = {
  get(key, fallback) { try { const v = localStorage.getItem(key); return v === null ? fallback : JSON.parse(v); } catch { return fallback; } },
  set(key, value) { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* private window */ } },
};

let info = null;
let hosted = true;

// ============================================================================================== plumbing

/** One call to the server. Throws an Error whose message is the server's own refusal. */
async function call(path, body, method = 'POST') {
  const headers = {};
  const key = store.get('playground-key', '');
  if (key) headers['X-Playground-Key'] = key;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  let response;
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    throw new Error('Could not reach the playground server.');
  }
  const answer = await response.json().catch(() => null);
  if (response.status === 401 && answer?.reason === 'key_required') showKeyBox();
  if (!response.ok) {
    const retry = response.headers.get('Retry-After');
    throw new Error(`${answer?.error || `The server answered ${response.status}.`}${retry && response.status === 429 ? ` Try again in ${retry} s.` : ''}`);
  }
  return answer ?? {};
}

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === 'class') node.className = v;
    else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
    else if (k === 'value') node.value = v;
    else if (v !== undefined && v !== null && v !== false) node.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat()) if (c !== null && c !== undefined && c !== false) node.append(c);
  return node;
}

function svg(tag, attrs = {}) {
  const node = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  return node;
}

function icon(type) {
  const s = svg('svg', { viewBox: '0 0 24 24', 'aria-hidden': 'true' });
  s.append(svg('path', { d: TYPES[type].icon }));
  return el('span', { class: 'icon-tile' }, s);
}

function options(select, values, labels = {}) {
  select.replaceChildren(...values.map((v) => el('option', { value: v }, labels[v] ?? v)));
}

function chip(text, kind) { return el('span', { class: `chip${kind ? ` ${kind}` : ''}` }, text); }

function cellText(value) {
  if (value === null || value === undefined) return '';
  return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

function renderTable(table, columns, rows, max = 200) {
  table.replaceChildren();
  const head = table.createTHead().insertRow();
  for (const c of columns) head.append(el('th', {}, c));
  const body = table.createTBody();
  for (const row of rows.slice(0, max)) {
    const tr = body.insertRow();
    for (const c of columns) tr.insertCell().textContent = cellText(row?.[c]);
  }
}

const kb = (bytes) => (bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(bytes % (1024 * 1024) ? 1 : 0)} MB` : bytes < 1024 ? `${bytes} bytes` : `${Math.round(bytes / 1024)} KB`);
const short = (value, n = 60) => { const t = typeof value === 'string' ? value : JSON.stringify(value) ?? 'null'; return t.length > n ? `${t.slice(0, n - 1)}...` : t; };
const isRows = (v) => Array.isArray(v) && v.length > 0 && v.every((r) => r && typeof r === 'object' && !Array.isArray(r));

function parseJson(text, what) {
  if (!String(text ?? '').trim()) return null;
  try { return JSON.parse(text); } catch (e) { throw new Error(`${what} is not valid JSON: ${e.message}`); }
}

function subtabs(container, onChange) {
  for (const b of container.querySelectorAll('button')) {
    b.addEventListener('click', () => {
      for (const o of container.querySelectorAll('button')) o.classList.toggle('on', o === b);
      onChange(b.dataset.tab);
    });
  }
}

function selectTab(container, tab) {
  for (const b of container.querySelectorAll('button')) b.classList.toggle('on', b.dataset.tab === tab);
}

/** A key/value list editor: [{ key, value, enabled }]. */
function kvEditor(container, rows, onChange) {
  const rowNode = (row) => {
    const node = el('div', { class: 'kv-row' },
      el('input', { type: 'checkbox', checked: row.enabled !== false, 'aria-label': 'On', onchange: (e) => { row.enabled = e.target.checked; onChange(); } }),
      el('input', { class: 'code', value: row.key, placeholder: 'Name', 'aria-label': 'Name', oninput: (e) => { row.key = e.target.value; grow(row); onChange(); } }),
      el('input', { class: 'code', value: row.value, placeholder: 'Value', 'aria-label': 'Value', oninput: (e) => { row.value = e.target.value; grow(row); onChange(); } }),
      el('button', { type: 'button', title: 'Remove', onclick: () => { rows.splice(rows.indexOf(row), 1); node.remove(); if (!rows.length) add(); onChange(); } }, 'x'));
    return node;
  };
  const add = () => { const fresh = { key: '', value: '', enabled: true }; rows.push(fresh); container.append(rowNode(fresh)); };
  const grow = (row) => { if (rows[rows.length - 1] === row && (row.key || row.value)) add(); };
  container.replaceChildren(...rows.map(rowNode));
  if (!rows.length || rows[rows.length - 1].key || rows[rows.length - 1].value) add();
}

// ============================================================================================== node types

const ICONS = {
  data: 'M8 4c-2 0-2 1.5-2 4 0 2-1 3.5-2 4 1 .5 2 2 2 4 0 2.5 0 4 2 4M16 4c2 0 2 1.5 2 4 0 2 1 3.5 2 4-1 .5-2 2-2 4 0 2.5 0 4-2 4',
  expression: 'M5 8h14M5 16h14M14 4l-4 16',
  template: 'M4 6h16M4 12h10M4 18h7M17 15l3 3-3 3',
  filter: 'M4 5h16l-6 8v6l-4-2v-4z',
  http: 'M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M3 12h18M12 3c3.5 3.5 3.5 14.5 0 18M12 3c-3.5 3.5-3.5 14.5 0 18',
  file: 'M7 3h7l5 5v13H7zM14 3v5h5M10 13h6M10 17h6',
  sql: 'M5 6c0-2.5 14-2.5 14 0v12c0 2.5-14 2.5-14 0zM5 6c0 2.5 14 2.5 14 0M5 12c0 2.5 14 2.5 14 0',
};

const AUTH_LABELS = {
  none: 'No auth', apiKey: 'API key', basic: 'Basic', bearer: 'Bearer token', digest: 'Digest', hawk: 'Hawk',
  oauth1: 'OAuth 1.0a', jwtBearer: 'JWT bearer', oauth2ClientCredentials: 'OAuth 2 (client credentials)',
  tokenEndpoint: 'Token endpoint', loginRequest: 'Login request',
};

// Each scheme's fields: [name, label, kind, choices]. kind: text, secret, select, json, number, list.
const AUTH_FIELDS = {
  none: [],
  apiKey: [['in', 'Add to', 'select', ['header', 'query', 'cookie']], ['name', 'Name', 'text'], ['value', 'Value', 'secret']],
  basic: [['username', 'Username', 'text'], ['password', 'Password', 'secret']],
  bearer: [['token', 'Token', 'secret'], ['prefix', 'Prefix', 'text']],
  digest: [['username', 'Username', 'text'], ['password', 'Password', 'secret'], ['algorithm', 'Algorithm', 'select', ['MD5', 'MD5-sess', 'SHA-256', 'SHA-256-sess']]],
  hawk: [['authId', 'Hawk id', 'text'], ['authKey', 'Hawk key', 'secret'], ['algorithm', 'Algorithm', 'select', ['sha256', 'sha1']], ['ext', 'ext', 'text']],
  oauth1: [['consumerKey', 'Consumer key', 'text'], ['consumerSecret', 'Consumer secret', 'secret'], ['token', 'Token', 'text'], ['tokenSecret', 'Token secret', 'secret'],
    ['signatureMethod', 'Signature', 'select', ['HMAC-SHA1', 'HMAC-SHA256', 'HMAC-SHA512', 'PLAINTEXT', 'RSA-SHA1', 'RSA-SHA256']], ['addTo', 'Add to', 'select', ['header', 'query', 'body']]],
  jwtBearer: [['algorithm', 'Algorithm', 'select', ['HS256', 'HS384', 'HS512', 'RS256', 'ES256']], ['secret', 'Secret (HS*)', 'secret'], ['privateKey', 'Private key PEM (RS*, ES*)', 'secret'],
    ['payload', 'Claims (JSON)', 'json'], ['lifetimeSeconds', 'Lifetime (s)', 'number'], ['addTo', 'Add to', 'select', ['header', 'query']]],
  oauth2ClientCredentials: [['tokenUrl', 'Token URL', 'text'], ['clientId', 'Client id', 'text'], ['clientSecret', 'Client secret', 'secret'], ['scopes', 'Scopes (space separated)', 'list']],
  tokenEndpoint: [['tokenUrl', 'Token URL', 'text'], ['method', 'Method', 'select', ['POST', 'PUT', 'GET']], ['bodyTemplate', 'Body (JSON)', 'json'], ['tokenPath', 'Token path', 'text'], ['headerFormat', 'Header format', 'text']],
  loginRequest: [['request', 'Login request (JSON)', 'json'], ['token', 'Where the token is (JSON)', 'json'], ['apply', 'How it is sent (JSON, optional)', 'json']],
};
const TOKEN_SCHEMES = ['oauth2ClientCredentials', 'tokenEndpoint', 'loginRequest'];

function demoAuth(type) {
  const c = info.demoApi.credentials;
  const base = info.demoApi.baseUrl;
  return {
    none: { type },
    apiKey: { type, in: 'header', name: 'X-API-Key', value: c.apiKey },
    basic: { type, username: c.username, password: c.password },
    bearer: { type, token: c.bearerToken },
    digest: { type, username: c.digestUsername, password: c.digestPassword, algorithm: 'SHA-256' },
    hawk: { type, authId: c.hawkId, authKey: c.hawkKey, algorithm: 'sha256' },
    oauth1: { type, consumerKey: c.consumerKey, consumerSecret: c.consumerSecret, token: c.oAuthToken, tokenSecret: c.oAuthTokenSecret, signatureMethod: 'HMAC-SHA1', addTo: 'header' },
    jwtBearer: { type, algorithm: 'HS256', secret: c.jwtSecret, payload: { sub: 'ada', aud: 'demo-api' }, lifetimeSeconds: 300, addTo: 'header' },
    oauth2ClientCredentials: { type, tokenUrl: `${base}/oauth/token`, clientId: c.clientId, clientSecret: c.clientSecret },
    tokenEndpoint: { type, tokenUrl: `${base}/auth/login`, method: 'POST', bodyTemplate: { username: c.username, password: c.password }, tokenPath: 'token' },
    loginRequest: { type, request: { method: 'POST', url: '/auth/login', bodyType: 'json', body: { username: c.username, password: c.password } }, token: { from: 'body', path: 'token' } },
  }[type];
}

const CSV_SAMPLE = `id,name,city,joined
1,Ada Lovelace,London,2024-01-15
2,Grace Hopper,New York,2024-02-03
3,"Turing, Alan",Manchester,2024-03-22
4,Katherine Johnson,Hampton,2024-04-09
5,Tim Berners-Lee,London,2024-05-30`;

/**
 * Every kind of node: what it is, which library runs it, which panel limits it counts against, its first settings,
 * the line it shows on the canvas, how it runs, and the C# it stands for.
 */
const TYPES = {
  data: {
    label: 'JSON data', group: 'Start', pkg: null, panel: null, inputs: false,
    blurb: 'A value to start from: JSON, or plain text such as a CSV.',
    defaults: () => ({ value: pretty({ order: { id: 42, total: 120.5 }, customer: { name: 'Ada Lovelace', vip: true } }) }),
    sub: (c) => short(c.value.replace(/\s+/g, ' '), 40),
  },
  expression: {
    label: 'Expression', group: 'Logic', pkg: 'Expressions', panel: 'expressions',
    blurb: 'One expression over the input: a value, or a condition.',
    defaults: () => ({ expression: 'input' }),
    sub: (c) => c.expression,
  },
  template: {
    label: 'Shape JSON', group: 'Logic', pkg: 'Expressions', panel: 'expressions',
    blurb: 'Build new JSON with {{ }} placeholders.',
    defaults: () => ({ template: pretty({ id: '{{ input.order.id }}', label: 'Order {{ input.order.id }}' }) }),
    sub: (c) => short(c.template.replace(/\s+/g, ' '), 40),
  },
  filter: {
    label: 'Filter rows', group: 'Logic', pkg: 'Expressions', panel: 'expressions',
    blurb: 'Keep the rows of a list where a condition holds.',
    defaults: () => ({ expression: 'input.total > 100' }),
    sub: (c) => c.expression,
  },
  http: {
    label: 'HTTP request', group: 'Connectors', pkg: 'Connectors.Http', panel: 'http',
    blurb: 'Call an API, signed with any of 11 auth schemes.',
    defaults: () => ({ method: 'GET', url: `${info.demoApi.baseUrl}/orders`, headers: [], auth: demoAuth('apiKey'), bodyMode: 'none', body: '' }),
    sub: (c) => `${c.method} ${c.url.replace(/^https?:\/\//, '')}`,
  },
  file: {
    label: 'Convert file', group: 'Connectors', pkg: 'Connectors.Files', panel: 'files',
    blurb: 'Read and write CSV, TSV, NDJSON, JSON and XLSX.',
    defaults: () => ({ source: 'input', from: 'csv', content: '', storeId: '', path: '', to: 'csv', target: 'none', targetStore: '', targetPath: 'export.csv', overwrite: true, options: '' }),
    sub: (c) => `${c.source === 'input' ? 'input' : c.source === 'store' ? c.path || 'a store' : c.from.toUpperCase()} to ${c.to.toUpperCase()}${c.target === 'store' ? ` in ${c.targetPath}` : ''}`,
  },
  sql: {
    label: 'SQL query', group: 'Connectors', pkg: 'Connectors.Database', panel: 'database',
    blurb: 'PostgreSQL, SQL Server, MySQL or SQLite, with safe parameters.',
    defaults: () => ({ connectionId: 'sample', sql: 'SELECT id, name, city\nFROM customers\nWHERE country = {{ params.country }}\nORDER BY name', params: pretty({ country: 'US' }) }),
    sub: (c) => short(c.sql.replace(/\s+/g, ' '), 40),
  },
};
for (const [k, t] of Object.entries(TYPES)) t.icon = ICONS[k];

// ============================================================================================== flows

let flow = null;        // { id, name, nodes: [{ id, type, name, x, y, config }], edges: [{ from, to }], starter? }
let flows = [];         // the visitor's own (self-hosted: on the server; hosted: this browser)
let selected = null;    // { kind: 'node' | 'edge', id }
let runs = {};          // node id -> { status, output, detail, summary, error, ms }
const view = { x: 40, y: 40, k: 1 };

function node(type, name, x, y, config = {}) {
  return { id: uid(), type, name, x, y, config: { ...TYPES[type].defaults(), ...config } };
}

function starters() {
  const demo = info.demoApi.baseUrl;
  const make = (id, name, nodes) => ({ id: `starter-${id}`, name, starter: true, nodes, edges: nodes.slice(1).map((n, i) => ({ from: nodes[i].id, to: n.id })) });
  return [
    make('api-csv', 'Orders API to CSV', [
      node('http', 'Get orders', 0, 40, { method: 'GET', url: `${demo}/orders`, auth: demoAuth('apiKey') }),
      node('expression', 'Pick the list', 300, 40, { expression: 'input.body.orders' }),
      node('filter', 'Big and not cancelled', 600, 40, { expression: 'input.total > 100 && input.status != "cancelled"' }),
      node('file', 'Write a CSV', 900, 40, { source: 'input', to: 'csv' }),
    ]),
    make('db-api', 'Database report to an API', [
      node('sql', 'Revenue per customer', 0, 40, {
        connectionId: 'sample',
        sql: 'SELECT c.name, round(sum(i.quantity * i.unit_price), 2) AS revenue\nFROM orders o\nJOIN customers c ON c.id = o.customer_id\nJOIN order_items i ON i.order_id = o.id\nWHERE o.status <> {{ params.skip }}\nGROUP BY c.name\nORDER BY revenue DESC\nLIMIT 5',
        params: pretty({ skip: 'cancelled' }),
      }),
      node('template', 'Build the payload', 300, 40, { template: pretty({ report: 'Revenue per customer', best: '{{ input[0].name }}', customers: '{{ input.length }}', rows: '{{ input }}' }) }),
      node('http', 'Post it', 600, 40, { method: 'POST', url: `${demo}/echo/report`, auth: demoAuth('bearer'), bodyMode: 'input' }),
    ]),
    make('csv-rules', 'CSV in, rules applied', [
      node('data', 'Customers CSV', 0, 40, { value: CSV_SAMPLE }),
      node('file', 'Read the CSV', 300, 40, { source: 'input', from: 'csv', to: 'json' }),
      node('filter', 'London only', 600, 40, { expression: 'input.city == "London"' }),
      node('template', 'Summary', 900, 40, { template: pretty({ count: '{{ input.length }}', first: 'Hello {{ input[0].name }}' }) }),
    ]),
    make('expressions', 'Expressions and templates', [
      node('data', 'An order', 0, 40),
      node('expression', 'Is it big?', 300, 40, { expression: 'input.order.total > 100 && input.customer.vip' }),
      node('template', 'A message', 600, 40, { template: pretty('Order #{{ vars.anOrder.order.id }} for {{ vars.anOrder.customer.name }}: big = {{ input }}') }),
    ]),
  ];
}

function allFlows() { return [...starters(), ...flows]; }

/** The node's name as an identifier (vars.getOrders); a later node with the same name gets its position added. */
function varName(n) {
  const name = varNameBase(n);
  const first = flow.nodes.find((o) => varNameBase(o) === name);
  return first && first !== n ? `${name}${flow.nodes.indexOf(n) + 1}` : name;
}

function varNameBase(n) {
  const words = n.name.replace(/[^A-Za-z0-9 ]+/g, ' ').trim().split(/\s+/).filter(Boolean);
  let name = words.map((w, i) => (i === 0 ? w.toLowerCase() : w[0].toUpperCase() + w.slice(1).toLowerCase())).join('') || n.type;
  if (/^[0-9]/.test(name)) name = `n${name}`;
  return name;
}

const byId = (id) => flow.nodes.find((n) => n.id === id);
const incoming = (id) => flow.edges.filter((e) => e.to === id).map((e) => e.from);

/** The nodes in an order where every node comes after the ones feeding it. */
function order() {
  const left = new Map(flow.nodes.map((n) => [n.id, incoming(n.id).length]));
  const ready = flow.nodes.filter((n) => left.get(n.id) === 0).sort((a, b) => a.x - b.x || a.y - b.y).map((n) => n.id);
  const out = [];
  while (ready.length) {
    const id = ready.shift();
    out.push(id);
    for (const e of flow.edges.filter((x) => x.from === id)) {
      left.set(e.to, left.get(e.to) - 1);
      if (left.get(e.to) === 0) ready.push(e.to);
    }
  }
  return out;
}

function reaches(from, to) {
  const seen = new Set();
  const stack = [from];
  while (stack.length) {
    const id = stack.pop();
    if (id === to) return true;
    if (seen.has(id)) continue;
    seen.add(id);
    for (const e of flow.edges.filter((x) => x.from === id)) stack.push(e.to);
  }
  return false;
}

function ancestors(id, acc = new Set()) {
  for (const up of incoming(id)) if (!acc.has(up)) { acc.add(up); ancestors(up, acc); }
  return acc;
}

// ---- saving: the visitor's flows in this browser (hosted) or on the server's volume (self-hosted)

let saveTimer = null;

function changed() {
  if (flow.starter) {
    // A starter stays as it is; the first change makes it the visitor's own copy.
    const original = starters().find((s) => s.id === flow.id)?.name;
    flow = { ...flow, id: uid(), starter: false, name: flow.name === original ? `${flow.name} (mine)` : flow.name };
    flows.push(flow);
    drawFlowSelect();
    $('flow-name').value = flow.name;
  }
  $('save-status').textContent = 'Unsaved';
  clearTimeout(saveTimer);
  saveTimer = setTimeout(save, 700);
}

async function save() {
  const plain = flows.map((f) => ({ id: f.id, name: f.name, nodes: f.nodes, edges: f.edges }));
  if (hosted) {
    store.set('playground-flows', plain);
    $('save-status').textContent = 'Saved in this browser';
    return;
  }
  try {
    await call('/api/saved/flows', plain, 'PUT');
    $('save-status').textContent = 'Saved';
  } catch (e) {
    $('save-status').textContent = e.message;
  }
}

async function loadFlows() {
  if (hosted) { flows = store.get('playground-flows', []); return; }
  try { flows = (await call('/api/saved', undefined, 'GET')).flows ?? []; } catch { flows = []; }
}

function drawFlowSelect() {
  const select = $('flow-select');
  select.replaceChildren(
    el('optgroup', { label: 'Starters' }, starters().map((f) => el('option', { value: f.id }, f.name))),
    flows.length ? el('optgroup', { label: hosted ? 'Yours (this browser)' : 'Yours' }, flows.map((f) => el('option', { value: f.id }, f.name))) : null);
  select.value = flow?.id ?? '';
}

function openFlow(id) {
  const found = allFlows().find((f) => f.id === id) ?? starters()[0];
  flow = found.starter ? JSON.parse(JSON.stringify(found)) : found;
  runs = {};
  selected = null;
  store.set('playground-open', flow.id);
  $('flow-name').value = flow.name;
  $('flow-delete').disabled = !!flow.starter;
  $('save-status').textContent = flow.starter ? 'A starter: change it to make it yours' : '';
  drawFlowSelect();
  draw();
  drawInspector();
  requestAnimationFrame(fit);
}

// ============================================================================================== canvas

const canvas = () => $('canvas');

function applyView() {
  $('world').style.transform = `translate(${view.x}px, ${view.y}px) scale(${view.k})`;
  const c = canvas();
  const step = 22 * view.k;
  c.style.backgroundSize = `${step}px ${step}px`;
  c.style.backgroundPosition = `${view.x}px ${view.y}px`;
}

function toWorld(clientX, clientY) {
  const r = canvas().getBoundingClientRect();
  return { x: (clientX - r.left - view.x) / view.k, y: (clientY - r.top - view.y) / view.k };
}

function fit() {
  const c = canvas();
  if (!flow.nodes.length) { view.x = 40; view.y = 40; view.k = 1; applyView(); return; }
  const xs = flow.nodes.map((n) => n.x);
  const ys = flow.nodes.map((n) => n.y);
  const minX = Math.min(...xs) - 40;
  const minY = Math.min(...ys) - 40;
  const w = Math.max(...xs) + 230 + 40 - minX;
  const h = Math.max(...ys) + 130 + 40 - minY;
  view.k = Math.max(c.clientWidth < 600 ? 0.22 : 0.35, Math.min(1.1, c.clientWidth / w, c.clientHeight / h));
  view.x = (c.clientWidth - w * view.k) / 2 - minX * view.k;
  view.y = Math.max(16, (c.clientHeight - h * view.k) / 3) - minY * view.k;
  applyView();
}

function zoomAt(factor, cx, cy) {
  const k = Math.max(0.3, Math.min(2, view.k * factor));
  view.x = cx - ((cx - view.x) * k) / view.k;
  view.y = cy - ((cy - view.y) * k) / view.k;
  view.k = k;
  applyView();
}

function nodeSummary(n) {
  const r = runs[n.id];
  if (!r) return TYPES[n.type].pkg ? `${info.nugetPrefix}.${TYPES[n.type].pkg}` : 'Not run yet';
  if (r.status === 'running') return 'Running...';
  if (r.status === 'skipped') return 'Skipped: an earlier node failed';
  if (r.status === 'error') return r.error;
  return `${r.summary}${r.ms !== undefined ? ` in ${r.ms} ms` : ''}`;
}

function drawNodes() {
  const host = $('nodes');
  host.replaceChildren(...flow.nodes.map((n) => {
    const t = TYPES[n.type];
    const r = runs[n.id];
    const box = el('div', {
      class: `node t-${n.type}${selected?.kind === 'node' && selected.id === n.id ? ' on' : ''}${r ? ` st-${r.status}` : ''}`,
      'data-id': n.id,
    },
    t.inputs === false ? null : el('span', { class: 'port in', title: 'Input' }),
    el('span', { class: 'port out', title: 'Drag to the next node' }),
    el('div', { class: 'node-head' }, icon(n.type), el('div', { class: 'node-title' }, el('div', { class: 'node-name' }, n.name), el('div', { class: 'node-kind' }, t.label))),
    el('div', { class: 'node-sub' }, t.sub(n.config) || ' '),
    el('div', { class: 'node-foot' }, el('span', { class: 'dot' }), el('span', { class: 'say' }, nodeSummary(n))));
    box.style.left = `${n.x}px`;
    box.style.top = `${n.y}px`;
    return box;
  }));
  $('empty').hidden = flow.nodes.length > 0;
}

const PORT_Y = 26;
function wirePath(x1, y1, x2, y2) {
  const dx = Math.max(40, Math.abs(x2 - x1) / 2);
  return `M${x1} ${y1} C${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`;
}

function drawWires() {
  const host = $('wires');
  host.replaceChildren();
  for (const e of flow.edges) {
    const a = byId(e.from);
    const b = byId(e.to);
    if (!a || !b) continue;
    const d = wirePath(a.x + 230, a.y + PORT_Y, b.x, b.y + PORT_Y);
    const on = selected?.kind === 'edge' && selected.id === `${e.from}>${e.to}`;
    const ra = runs[a.id]?.status;
    const rb = runs[b.id]?.status;
    const state = rb === 'running' ? ' live' : ra === 'ok' && rb === 'ok' ? ' done' : '';
    host.append(svg('path', { d, class: `wire${on ? ' on' : ''}${state}` }));
    const hit = svg('path', { d, class: 'wire-hit', 'data-edge': `${e.from}>${e.to}` });
    host.append(hit);
  }
}

function draw() { drawNodes(); drawWires(); }

function select(next) {
  selected = next;
  draw();
  drawInspector();
}

function addNode(type, x, y) {
  const n = node(type, TYPES[type].label, Math.max(0, Math.round(x)), Math.max(0, Math.round(y)));
  if (type === 'http' && !hosted) n.config.auth = { type: 'none' };
  // A fresh node joins the selected one, the way it would be built by hand most of the time.
  const from = selected?.kind === 'node' ? byId(selected.id) : null;
  flow.nodes.push(n);
  if (from && TYPES[type].inputs !== false) flow.edges.push({ from: from.id, to: n.id });
  changed();
  insTab = 'settings';
  select({ kind: 'node', id: n.id });
}

function removeSelected() {
  if (!selected) return;
  if (selected.kind === 'node') {
    flow.nodes = flow.nodes.filter((n) => n.id !== selected.id);
    flow.edges = flow.edges.filter((e) => e.from !== selected.id && e.to !== selected.id);
    delete runs[selected.id];
  } else {
    flow.edges = flow.edges.filter((e) => `${e.from}>${e.to}` !== selected.id);
  }
  changed();
  select(null);
}

function setupCanvas() {
  const c = canvas();
  let gesture = null; // { kind: 'pan' | 'move' | 'wire', ... }

  c.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    const port = e.target.closest('.port.out');
    const box = e.target.closest('.node');
    const hit = e.target.closest('.wire-hit');
    c.focus({ preventScroll: true });
    if (port && box) {
      const from = byId(box.dataset.id);
      const temp = svg('path', { class: 'wire live' });
      $('wires').append(temp);
      gesture = { kind: 'wire', from, temp };
    } else if (box) {
      const n = byId(box.dataset.id);
      if (selected?.id !== n.id) select({ kind: 'node', id: n.id });
      const p = toWorld(e.clientX, e.clientY);
      gesture = { kind: 'move', node: n, dx: p.x - n.x, dy: p.y - n.y, moved: false };
    } else if (hit) {
      select({ kind: 'edge', id: hit.dataset.edge });
      return;
    } else {
      gesture = { kind: 'pan', sx: e.clientX, sy: e.clientY, vx: view.x, vy: view.y, moved: false };
      c.classList.add('panning');
    }
    c.setPointerCapture(e.pointerId);
  });

  c.addEventListener('pointermove', (e) => {
    if (!gesture) return;
    if (gesture.kind === 'pan') {
      view.x = gesture.vx + e.clientX - gesture.sx;
      view.y = gesture.vy + e.clientY - gesture.sy;
      gesture.moved ||= Math.abs(e.clientX - gesture.sx) + Math.abs(e.clientY - gesture.sy) > 3;
      applyView();
    } else if (gesture.kind === 'move') {
      const p = toWorld(e.clientX, e.clientY);
      const n = gesture.node;
      const x = Math.max(0, Math.round(p.x - gesture.dx));
      const y = Math.max(0, Math.round(p.y - gesture.dy));
      if (x === n.x && y === n.y) return;
      n.x = x; n.y = y;
      gesture.moved = true;
      const box = $('nodes').querySelector(`[data-id="${n.id}"]`);
      box.classList.add('dragging');
      box.style.left = `${x}px`;
      box.style.top = `${y}px`;
      drawWires();
    } else if (gesture.kind === 'wire') {
      const p = toWorld(e.clientX, e.clientY);
      const a = gesture.from;
      gesture.temp.setAttribute('d', wirePath(a.x + 230, a.y + PORT_Y, p.x, p.y));
      $('wires').append(gesture.temp);
      const under = document.elementFromPoint(e.clientX, e.clientY)?.closest('.node');
      for (const b of $('nodes').querySelectorAll('.node.target')) if (b !== under) b.classList.remove('target');
      if (under && under.dataset.id !== a.id && TYPES[byId(under.dataset.id).type].inputs !== false) under.classList.add('target');
    }
  });

  const end = (e) => {
    if (!gesture) return;
    c.classList.remove('panning');
    if (gesture.kind === 'pan' && !gesture.moved) select(null);
    if (gesture.kind === 'move' && gesture.moved) { changed(); drawNodes(); }
    if (gesture.kind === 'wire') {
      gesture.temp.remove();
      const under = document.elementFromPoint(e.clientX, e.clientY)?.closest('.node');
      const to = under ? byId(under.dataset.id) : null;
      if (to) connect(gesture.from, to);
      else drawWires();
    }
    gesture = null;
  };
  c.addEventListener('pointerup', end);
  c.addEventListener('pointercancel', end);

  c.addEventListener('wheel', (e) => {
    e.preventDefault();
    const r = c.getBoundingClientRect();
    zoomAt(e.deltaY < 0 ? 1.1 : 1 / 1.1, e.clientX - r.left, e.clientY - r.top);
  }, { passive: false });

  c.addEventListener('keydown', (e) => {
    if ((e.key === 'Delete' || e.key === 'Backspace') && selected) { e.preventDefault(); removeSelected(); }
  });

  // Drag a node in from the palette.
  c.addEventListener('dragover', (e) => { if (e.dataTransfer.types.includes('text/x-node')) { e.preventDefault(); c.classList.add('drop'); } });
  c.addEventListener('dragleave', () => c.classList.remove('drop'));
  c.addEventListener('drop', (e) => {
    c.classList.remove('drop');
    const type = e.dataTransfer.getData('text/x-node');
    if (!TYPES[type]) return;
    e.preventDefault();
    const p = toWorld(e.clientX, e.clientY);
    addNode(type, p.x - 115, p.y - 20);
  });

  $('zoom-in').addEventListener('click', () => zoomAt(1.2, c.clientWidth / 2, c.clientHeight / 2));
  $('zoom-out').addEventListener('click', () => zoomAt(1 / 1.2, c.clientWidth / 2, c.clientHeight / 2));
  $('zoom-fit').addEventListener('click', fit);
  window.addEventListener('resize', () => applyView());
}

function connect(from, to) {
  const say = (text) => log(text, 'bad');
  if (from.id === to.id) return drawWires();
  if (TYPES[to.type].inputs === false) { say(`${TYPES[to.type].label} starts a flow: it takes no input.`); return drawWires(); }
  if (flow.edges.some((e) => e.from === from.id && e.to === to.id)) return drawWires();
  if (reaches(to.id, from.id)) { say('That would make a loop. A flow runs one way.'); return drawWires(); }
  flow.edges.push({ from: from.id, to: to.id });
  changed();
  select({ kind: 'edge', id: `${from.id}>${to.id}` });
}

function setupPalette() {
  const groups = [...new Set(Object.values(TYPES).map((t) => t.group))];
  $('palette').replaceChildren(...groups.flatMap((g) => [
    el('p', { class: 'pal-group' }, g),
    ...Object.entries(TYPES).filter(([, t]) => t.group === g).map(([type, t]) => {
      const item = el('button', {
        class: `pal-item t-${type}`, draggable: 'true', title: t.blurb,
        onclick: () => {
          // Next to the selected node, else right of the last one, else in the middle of what is in view.
          const c = canvas();
          const from = (selected?.kind === 'node' ? byId(selected.id) : null)
            ?? [...flow.nodes].sort((a, b) => b.x - a.x || b.y - a.y)[0];
          const p = toWorld(c.getBoundingClientRect().left + c.clientWidth / 2, c.getBoundingClientRect().top + c.clientHeight / 2);
          let x = from ? from.x + 300 : p.x - 115;
          let y = from ? from.y : p.y - 50;
          while (flow.nodes.some((o) => Math.abs(o.x - x) < 200 && Math.abs(o.y - y) < 110)) y += 140;
          addNode(type, x, y);
          const n = flow.nodes[flow.nodes.length - 1];
          const sx = view.x + n.x * view.k;
          if (sx + 230 * view.k > c.clientWidth || sx < 0) fit();
        },
      }, icon(type), el('span', {}, t.label, el('small', {}, t.blurb)));
      item.addEventListener('dragstart', (e) => { e.dataTransfer.setData('text/x-node', type); e.dataTransfer.effectAllowed = 'copy'; });
      return item;
    }),
  ]));
}

// ============================================================================================== running

function log(text, kind) {
  const entry = el('div', { class: `entry${kind ? ` ${kind}` : ''}` }, text);
  $('runlog').append(entry);
  while ($('runlog').children.length > 3) $('runlog').firstChild.remove();
  setTimeout(() => entry.remove(), 6000);
}

/** {{ }} in a node's own settings, rendered by the expressions library itself (one call) before the node runs. */
async function render(value, input, vars) {
  if (!JSON.stringify(value).includes('{{')) return value;
  const answer = await call('/api/expressions/evaluate', { mode: 'json', template: value, data: { input, vars } });
  return answer.result;
}

function asText(value) { return typeof value === 'string' ? value : JSON.stringify(value); }

const RUN = {
  async data(n) {
    const text = n.config.value;
    let output;
    try { output = JSON.parse(text); } catch { output = text; }
    return { output, summary: typeof output === 'string' ? `text, ${output.length} characters` : Array.isArray(output) ? `${output.length} items` : typeof output };
  },

  async expression(n, input, vars) {
    const a = await call('/api/expressions/evaluate', { mode: 'expression', expression: n.config.expression, data: { input, vars } });
    return { output: a.result, detail: a, summary: `${a.type}: ${short(a.result, 28)}` };
  },

  async template(n, input, vars) {
    const template = parseJson(n.config.template, 'The template');
    const a = await call('/api/expressions/evaluate', { mode: 'json', template, data: { input, vars } });
    return { output: a.result, detail: a, summary: `${a.type}${Array.isArray(a.result) ? `, ${a.result.length} items` : ''}` };
  },

  async filter(n, input, vars) {
    if (!Array.isArray(input)) throw new Error('Filter rows needs a list as its input. Put an Expression before it that picks the list, such as input.body.orders.');
    const a = await call('/api/expressions/evaluate', { mode: 'batch', expression: n.config.expression, rows: input, data: { vars } });
    const output = input.slice(0, a.rowCount).filter((_, i) => a.results[i]?.truthy);
    return { output, detail: a, summary: `${output.length} of ${a.rowCount} rows kept${a.truncated ? `, cut at ${a.maxRows}` : ''}` };
  },

  async http(n, input, vars) {
    const c = n.config;
    const parts = await render({
      url: c.url,
      headers: c.headers.filter((h) => h.key && h.enabled !== false),
      body: c.bodyMode === 'json' ? parseJson(c.body, 'The body') : null,
    }, input, vars);
    const request = { method: c.method, url: String(parts.url).trim(), headers: parts.headers, auth: c.auth };
    if (c.bodyMode === 'input') { request.bodyType = 'json'; request.body = input ?? null; }
    else if (c.bodyMode === 'json') { request.bodyType = 'json'; request.body = parts.body; }
    const a = await call('/api/http/send', request);
    let body = a.body.text ?? (a.body.base64 ? `(binary, ${a.body.bytes} bytes)` : null);
    if (typeof body === 'string') { try { body = JSON.parse(body); } catch { /* text */ } }
    const headers = Object.fromEntries((a.headers ?? []).map((h) => [h.name.toLowerCase(), h.value]));
    return { output: { status: a.status, body, headers }, detail: a, summary: `${a.status} ${a.reason ?? ''}`.trim() };
  },

  async file(n, input) {
    const c = n.config;
    const body = { from: c.from, to: c.to };
    if (c.source === 'input') {
      if (typeof input === 'string') body.content = input;
      else { body.from = 'json'; body.content = JSON.stringify(input ?? []); }
    } else if (c.source === 'paste') {
      body.content = c.content;
    } else {
      body.source = { storeId: c.storeId, path: c.path };
    }
    if (c.target === 'store') body.target = { storeId: c.targetStore, path: c.targetPath, overwrite: !!c.overwrite };
    const opts = parseJson(c.options, 'The options');
    if (opts) body.options = opts;
    const a = await call('/api/files/convert', body);
    return { output: a.rows, detail: a, summary: `${a.rowCount} rows to ${c.to.toUpperCase()}${a.output?.written ? ', written' : ''}` };
  },

  async sql(n, input) {
    const c = n.config;
    const params = { ...(parseJson(c.params, 'Params') ?? {}), input: input ?? null };
    const a = await call('/api/database/query', { connectionId: c.connectionId, sql: c.sql, params });
    const changes = a.rowsAffected > 0 ? `, ${a.rowsAffected} changed${a.committed ? '' : ' (rolled back)'}` : '';
    return { output: a.rows ?? [], detail: a, summary: `${a.rowCount ?? 0} rows${changes}` };
  },
};

let running = false;

async function runFlow(untilId) {
  if (running || !flow.nodes.length) return;
  running = true;
  $('run-flow').disabled = true;
  $('ins-run').disabled = true;
  const ids = order();
  const want = untilId ? new Set([...ancestors(untilId), untilId]) : null;
  const plan = want ? ids.filter((id) => want.has(id)) : ids;
  if (plan.length < (want ? want.size : flow.nodes.length)) log('Part of this flow loops back on itself; those nodes were left out.', 'bad');
  for (const id of plan) delete runs[id];
  const outputs = {};
  const vars = {};
  for (const id of ids) if (!plan.includes(id) && runs[id]?.status === 'ok') { outputs[id] = runs[id].output; vars[varName(byId(id))] = runs[id].output; }
  draw();

  let failed = 0;
  for (const id of plan) {
    const n = byId(id);
    if (!n) continue;
    const ups = incoming(id);
    if (ups.some((u) => runs[u]?.status !== 'ok' && !(u in outputs))) {
      runs[id] = { status: 'skipped' };
      draw();
      continue;
    }
    const input = ups.length === 0 ? null : ups.length === 1 ? outputs[ups[0]] : Object.fromEntries(ups.map((u) => [varName(byId(u)), outputs[u]]));
    runs[id] = { status: 'running', input };
    draw();
    const started = performance.now();
    try {
      const r = await RUN[n.type](n, input, { ...vars });
      runs[id] = { status: 'ok', input, ...r, ms: Math.round(performance.now() - started) };
      outputs[id] = r.output;
      vars[varName(n)] = r.output;
    } catch (e) {
      runs[id] = { status: 'error', input, error: e.message };
      failed++;
    }
    draw();
    if (selected?.kind === 'node' && selected.id === id) drawInspector();
  }

  running = false;
  $('run-flow').disabled = false;
  $('ins-run').disabled = false;
  if (failed) log(`${failed} node${failed > 1 ? 's' : ''} failed. Click one to see why.`, 'bad');
  else log(`Ran ${plan.length} node${plan.length > 1 ? 's' : ''}.`, 'ok');
  drawInspector();
}

// ============================================================================================== inspector

let insTab = 'settings';

function drawInspector() {
  const n = selected?.kind === 'node' ? byId(selected.id) : null;
  $('inspector-empty').hidden = !!n;
  $('inspector-node').hidden = !n;
  if (!n) return;
  const t = TYPES[n.type];
  $('inspector-node').className = `t-${n.type}`;
  $('ins-type').textContent = t.label;
  $('ins-name').value = n.name;
  $('ins-var').replaceChildren('Later nodes read its answer as ', el('code', {}, `vars.${varName(n)}`), ...(t.pkg ? ['. Runs ', el('code', {}, `${info.nugetPrefix}.${t.pkg}`), '.'] : ['.']));
  selectTab($('ins-tabs'), insTab);
  $('ins-settings').hidden = insTab !== 'settings';
  $('ins-output').hidden = insTab !== 'output';
  if (insTab === 'settings') $('ins-settings').replaceChildren(...SETTINGS[n.type](n));
  else $('ins-output').replaceChildren(...outputView(n));
}

/** Re-draws the node's line on the canvas without touching the inspector (so the field being typed in keeps focus). */
function touched() { changed(); drawNodes(); }

function field(labelText, control, hint) {
  return [el('label', {}, labelText, hint ? el('span', { class: 'hint' }, ` ${hint}`) : null), control];
}

function textArea(n, key, rows, extra = {}) {
  return el('textarea', { class: 'code', rows, spellcheck: 'false', value: n.config[key], oninput: (e) => { n.config[key] = e.target.value; touched(); }, ...extra });
}

function textInput(n, key, extra = {}) {
  return el('input', { class: 'code', value: n.config[key] ?? '', oninput: (e) => { n.config[key] = e.target.value; touched(); }, ...extra });
}

function selectFor(n, key, values, labels = {}, after) {
  const s = el('select', { onchange: (e) => { n.config[key] = e.target.value; touched(); after?.(); } });
  options(s, values, labels);
  s.value = n.config[key];
  return s;
}

const ROOTS_HINT = 'input is what the node before sends; vars.<name> is any earlier node.';

const SETTINGS = {
  data: (n) => [
    ...field('Value', textArea(n, 'value', 12), 'JSON, or any text (a CSV, say)'),
    el('p', { class: 'hint' }, 'Nothing is sent anywhere: this node only hands its value to the next.'),
  ],

  expression: (n) => [
    ...field('Expression', textArea(n, 'expression', 4)),
    el('p', { class: 'hint' }, ROOTS_HINT, ' Paths, comparisons, && || !, exists(), .length. It reads data and never runs code.'),
    examples(n, 'expression', ['input.order.total > 100', 'input.body.orders', 'exists(input.customer.email)', 'input.items[0].sku']),
  ],

  template: (n) => [
    ...field('JSON template', textArea(n, 'template', 10), 'every string may hold {{ }}'),
    el('p', { class: 'hint' }, ROOTS_HINT, ' A string that is one whole {{ }} keeps its type (a number stays a number, a list stays a list).'),
  ],

  filter: (n) => [
    ...field('Keep a row when', textArea(n, 'expression', 3)),
    el('p', { class: 'hint' }, 'Runs once per row of the input list, with that row as input.'),
    examples(n, 'expression', ['input.total > 100', 'input.status != "cancelled"', 'input.city == "London"']),
  ],

  http: (n) => {
    const c = n.config;
    const authBox = el('div');
    const drawAuth = () => authBox.replaceChildren(...authFields(n));
    const authSelect = el('select', { onchange: (e) => { c.auth = { type: e.target.value }; if (hosted) c.auth = demoAuth(e.target.value); touched(); drawAuth(); } });
    options(authSelect, info.authTypes, AUTH_LABELS);
    authSelect.value = c.auth?.type ?? 'none';
    drawAuth();
    const kv = el('div', { class: 'kv' });
    kvEditor(kv, c.headers, () => changed());
    const bodyBox = el('div');
    const drawBody = () => bodyBox.replaceChildren(...(c.bodyMode === 'json' ? field('Body (JSON)', textArea(n, 'body', 6), 'may hold {{ }}') : c.bodyMode === 'input' ? [el('p', { class: 'hint' }, 'The body is the input, as JSON.')] : []));
    drawBody();
    return [
      el('div', { class: 'row' }, selectFor(n, 'method', ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD']), textInput(n, 'url', { 'aria-label': 'URL', placeholder: 'https://api.example.com/orders' })),
      hosted ? el('p', { class: 'hint' }, 'Here requests reach only the built-in demo API at ', el('code', {}, info.demoApi.baseUrl), ' (/orders, /orders/42, /echo/..., /status/503). Run it yourself to call any API.') : el('p', { class: 'hint' }, 'The URL may hold {{ }}, for example /orders/{{ input.id }}.'),
      ...field('Auth', authSelect, hosted ? 'filled with the demo API\'s credentials' : null),
      authBox,
      ...field('Headers', kv),
      ...field('Body', selectFor(n, 'bodyMode', ['none', 'input', 'json'], { none: 'No body', input: 'The input, as JSON', json: 'JSON I write' }, drawBody)),
      bodyBox,
    ];
  },

  file: (n) => {
    const c = n.config;
    const srcBox = el('div');
    const fromStore = () => c.source === 'store';
    const drawSource = () => {
      const formats = fromStore() ? info.readFormats : info.readFormats.filter((f) => f !== 'xlsx');
      if (!formats.includes(c.from)) c.from = 'csv';
      srcBox.replaceChildren(
        ...(c.source === 'paste' ? field('File content', textArea(n, 'content', 7)) : []),
        ...(fromStore() ? [...field('Store', storeSelect(n, 'storeId')), ...field('Path', textInput(n, 'path', { placeholder: 'in/customers.csv' }))] : []),
        ...field('Read as', selectFor(n, 'from', formats, Object.fromEntries(formats.map((f) => [f, f.toUpperCase()]))), c.source === 'input' ? 'when the input is text; a list is read as rows' : null));
    };
    const dstBox = el('div');
    const drawTarget = () => dstBox.replaceChildren(...(c.target === 'store'
      ? [...field('Store', storeSelect(n, 'targetStore')), ...field('Path', textInput(n, 'targetPath')), el('label', { class: 'check' }, el('input', { type: 'checkbox', checked: !!c.overwrite, onchange: (e) => { c.overwrite = e.target.checked; changed(); } }), 'Replace a file already there')]
      : []));
    drawSource();
    drawTarget();
    const sources = hosted ? ['input', 'paste'] : ['input', 'paste', 'store'];
    const targets = hosted ? ['none'] : ['none', 'store'];
    return [
      ...field('Read from', selectFor(n, 'source', sources, { input: 'The input', paste: 'Text I paste', store: 'A file in a store' }, drawSource)),
      srcBox,
      ...field('Write as', selectFor(n, 'to', info.writeFormats, Object.fromEntries(info.writeFormats.map((f) => [f, f === 'json' ? 'JSON rows' : f.toUpperCase()])))),
      ...(targets.length > 1 ? field('Save to', selectFor(n, 'target', targets, { none: 'Nowhere (download it from Output)', store: 'A store' }, drawTarget)) : [el('p', { class: 'hint' }, 'The file is in Output, to download. Run it yourself to read and write folders, S3, Azure Blob or FTP.')]),
      dstBox,
      ...field('Options (JSON, optional)', textArea(n, 'options', 3, { placeholder: '{ "csv": { "delimiter": ";" } }' })),
      el('p', { class: 'hint' }, 'The answer passed on is the rows.'),
    ];
  },

  sql: (n) => {
    const c = n.config;
    const conn = connections.find((x) => x.id === c.connectionId);
    const s = selectFor(n, 'connectionId', connections.map((x) => x.id), Object.fromEntries(connections.map((x) => [x.id, `${x.name} (${x.provider})`])), () => drawInspector());
    return [
      ...field('Connection', s),
      el('p', { class: 'hint' }, conn?.id === 'sample'
        ? `A SQLite sample made for each run and gone after it. Tables: ${info.sampleTables.map((t) => t.table).join(', ')}.`
        : conn ? `${conn.display}. ${conn.allowWrites && !hosted ? 'Writes are committed.' : 'Read only: every run is rolled back.'}` : 'Pick a connection.'),
      hosted ? null : el('button', { class: 'small-btn', onclick: () => openDialog('connections') }, 'Manage connections'),
      ...field('SQL', textArea(n, 'sql', 8), '{{ params.x }} becomes a parameter, never SQL'),
      ...field('Params (JSON)', textArea(n, 'params', 3)),
      el('p', { class: 'hint' }, 'The input is there too, as params.input: WHERE id = {{ params.input.id }}.'),
    ];
  },
};

function examples(n, key, list) {
  return el('div', { class: 'facts' }, list.map((x) => el('button', { class: 'small-btn', onclick: () => { n.config[key] = x; touched(); drawInspector(); } }, x)));
}

function authFields(n) {
  const auth = n.config.auth ?? { type: 'none' };
  const fields = AUTH_FIELDS[auth.type] ?? [];
  if (!fields.length) return [];
  const grid = el('div', { class: 'fields' });
  for (const [name, label, kind, choices] of fields) {
    const value = auth[name];
    let control;
    const set = (v) => { if (v === '' || v === undefined) delete auth[name]; else auth[name] = v; changed(); };
    if (kind === 'select') {
      control = el('select', { onchange: (e) => set(e.target.value) });
      options(control, choices);
      control.value = value ?? choices[0];
    } else if (kind === 'json') {
      control = el('textarea', { class: 'code', rows: 3, value: value === undefined ? '' : pretty(value), oninput: (e) => { try { set(e.target.value.trim() ? JSON.parse(e.target.value) : undefined); e.target.setCustomValidity(''); } catch { e.target.setCustomValidity('Not valid JSON'); } } });
    } else if (kind === 'list') {
      control = el('input', { class: 'code', value: (value ?? []).join(' '), oninput: (e) => set(e.target.value.split(/\s+/).filter(Boolean)) });
    } else {
      control = el('input', { class: 'code', type: kind === 'secret' ? 'password' : kind === 'number' ? 'number' : 'text', autocomplete: 'off', value: value ?? '', oninput: (e) => set(kind === 'number' ? (e.target.value ? Number(e.target.value) : undefined) : e.target.value) });
    }
    grid.append(el('label', {}, label), control);
  }
  return [grid];
}

function storeSelect(n, key) {
  const s = el('select', { onchange: (e) => { n.config[key] = e.target.value; touched(); } });
  options(s, ['', ...fileStores.map((x) => x.id)], { '': fileStores.length ? 'Pick a store' : 'No stores yet', ...Object.fromEntries(fileStores.map((x) => [x.id, `${x.name} (${x.store})`])) });
  s.value = n.config[key] ?? '';
  return el('div', { class: 'row' }, s, el('button', { class: 'small-btn', onclick: () => openDialog('stores') }, 'Manage'));
}

function valueView(value) {
  if (isRows(value)) {
    const columns = [...new Set(value.slice(0, 50).flatMap((r) => Object.keys(r)))].slice(0, 30);
    const table = el('table');
    renderTable(table, columns, value);
    return [el('div', { class: 'table-wrap' }, table), el('details', {}, el('summary', { class: 'hint' }, 'As JSON'), el('pre', { class: 'code out' }, pretty(value)))];
  }
  return [el('pre', { class: 'code out' }, typeof value === 'string' ? value : pretty(value))];
}

function outputView(n) {
  const r = runs[n.id];
  if (!r) return [el('p', { class: 'hint' }, 'Not run yet. Press Run to here, or Run flow.')];
  if (r.status === 'running') return [el('p', { class: 'hint' }, 'Running...')];
  if (r.status === 'skipped') return [el('p', { class: 'hint' }, 'Skipped: a node before this one failed.')];
  const parts = [];
  if (r.status === 'error') parts.push(el('div', { class: 'error-box' }, r.error));
  const d = r.detail ?? {};
  if (r.status === 'ok') {
    const facts = el('div', { class: 'facts' }, chip(r.summary, 'yes'), r.ms !== undefined ? chip(`${r.ms} ms`) : null);
    if ('truthy' in d) facts.append(chip(d.truthy ? 'as a condition: true' : 'as a condition: false', d.truthy ? 'yes' : 'no'));
    if (n.type === 'http') facts.append(chip(d.auth === 'none' ? 'no auth' : `signed: ${AUTH_LABELS[d.auth] ?? d.auth}`), ...(d.timeline?.length > 1 ? [chip(d.timeline[0].note ?? 'sent twice', 'warn')] : []));
    parts.push(facts);
    if (n.type === 'file' && d.output) {
      const file = d.output;
      const blob = file.base64 ? new Blob([Uint8Array.from(atob(file.base64), (ch) => ch.charCodeAt(0))], { type: file.contentType }) : new Blob([file.text ?? ''], { type: file.contentType });
      parts.push(el('div', { class: 'row' },
        el('a', { href: URL.createObjectURL(blob), download: file.fileName, class: 'small-btn' }, `Download ${file.fileName} (${kb(file.bytes)})`),
        file.written ? chip(`written to ${file.written.path}`, 'yes') : null));
      if (file.text) parts.push(el('details', {}, el('summary', { class: 'hint' }, `The ${file.format.toUpperCase()} file`), el('pre', { class: 'code out' }, file.text)));
    }
    if (n.type === 'sql') {
      const advice = el('ul', { class: 'advice' }, (d.advice ?? []).map((a) => el('li', { class: a.severity }, `${a.severity}: ${a.message}`)));
      if (!d.advice?.length) advice.append(el('li', { class: 'ok' }, 'The query advisor has nothing to flag.'));
      parts.push(advice, el('details', {}, el('summary', { class: 'hint' }, 'Sent as'), el('pre', { class: 'code out' }, `${d.boundSql}\n\n${(d.parameters ?? []).map((p) => `${p.name} = ${JSON.stringify(p.value)}`).join('\n')}`)));
    }
    if (n.type === 'http' && d.request) parts.push(el('details', {}, el('summary', { class: 'hint' }, 'The request as sent'), el('pre', { class: 'code out' }, d.request.wire ?? pretty(d.request))));
    parts.push(el('label', {}, 'Answer passed on'), ...valueView(r.output));
  }
  if (r.input !== undefined && r.input !== null) parts.push(el('details', {}, el('summary', { class: 'hint' }, 'Input it got'), ...valueView(r.input)));
  return parts;
}

function setupInspector() {
  subtabs($('ins-tabs'), (tab) => { insTab = tab; drawInspector(); });
  $('ins-name').addEventListener('input', (e) => {
    const n = byId(selected.id);
    n.name = e.target.value || TYPES[n.type].label;
    changed();
    drawNodes();
    $('ins-var').querySelector('code').textContent = `vars.${varName(n)}`;
  });
  $('ins-delete').addEventListener('click', removeSelected);
  $('ins-run').addEventListener('click', async () => {
    await runFlow(selected.id);
    insTab = 'output';
    drawInspector();
  });
}

// ============================================================================================== C# code

const KEYWORDS = new Set(['abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char', 'checked', 'class', 'const', 'continue', 'decimal', 'default', 'delegate', 'do', 'double', 'else', 'enum', 'event', 'explicit', 'extern', 'false', 'finally', 'fixed', 'float', 'for', 'foreach', 'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal', 'is', 'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', 'override', 'params', 'private', 'protected', 'public', 'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short', 'sizeof', 'stackalloc', 'static', 'string', 'struct', 'switch', 'this', 'throw', 'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'using', 'virtual', 'void', 'volatile', 'while', 'input', 'vars', 'http', 'databases', 'tokens', 'args']);

const csStr = (s) => JSON.stringify(String(s)).replace(/\\u2028/g, '\\u2028');

/** A C# raw string literal on its own lines, with enough quotes that nothing inside can end it. */
function csRaw(text) {
  const longest = Math.max(2, ...(String(text).match(/"+/g) ?? []).map((m) => m.length));
  const q = '"'.repeat(longest + 1);
  return `${q}\n${text}\n${q}`;
}

const FORMAT_CLASS = { csv: 'CsvFormat', tsv: 'TsvFormat', ndjson: 'NdjsonFormat', json: 'JsonFormat', xlsx: 'XlsxFormat' };

function csharp() {
  const P = info.nugetPrefix;
  const ids = order();
  const names = Object.fromEntries(flow.nodes.map((n) => { const v = varName(n); return [n.id, KEYWORDS.has(v) ? `${v}Value` : v]; }));
  const types = new Set(ids.map((id) => byId(id).type));
  const usings = ['System.Text.Json', 'System.Text.Json.Nodes', `${P}.Expressions`];
  if (types.has('http')) usings.push(`${P}.Connectors.Http`, `${P}.Connectors.Http.Auth`);
  if (types.has('file')) usings.push('System.Text', `${P}.Connectors.Files`);
  if (types.has('sql')) usings.push('System.Data.Common', `${P}.Connectors.Database`);
  const usesTokens = flow.nodes.some((n) => n.type === 'http' && TOKEN_SCHEMES.includes(n.config.auth?.type));

  const out = [];
  out.push(`// "${flow.name}": made in the ${info.productName} libraries playground. Each block is one node, in run order.`);
  out.push(...usings.map((u) => `using ${u};`), '');
  out.push('// Every node\'s answer, by name: what {{ vars.name }} reads.', 'var vars = new JsonObject();');
  if (types.has('http')) out.push('using var http = new HttpClient();');
  if (usesTokens) out.push('// Token schemes fetch a token first and keep it until it expires.', 'var tokens = new TokenCache();');
  if (types.has('sql')) out.push('var databases = new DbProviderRegistry([new PostgresAdapter(), new SqlServerAdapter(), new MySqlAdapter(), new SqliteAdapter()]);');
  out.push('');

  // Every node's answer is a JsonNode?. `ref` reads the input (in Context, is, as); `copy` is a copy to keep.
  const inputOf = (id) => {
    const ups = incoming(id).filter((u) => names[u]);
    if (!ups.length) return { ref: '(JsonNode?)null', copy: 'null' };
    if (ups.length === 1) return { ref: names[ups[0]], copy: `${names[ups[0]]}?.DeepClone()` };
    const merged = `(JsonNode)new JsonObject { ${ups.map((u) => `[${csStr(varName(byId(u)))}] = ${names[u]}?.DeepClone()`).join(', ')} }`;
    return { ref: merged, copy: merged };
  };

  for (const id of ids) {
    const n = byId(id);
    const v = names[id];
    const c = n.config;
    const { ref: input, copy: inputCopy } = inputOf(id);
    out.push(`// ---- ${n.name} (${TYPES[n.type].label})`);
    switch (n.type) {
      case 'data': {
        let json = true;
        try { JSON.parse(c.value); } catch { json = false; }
        out.push(json ? `JsonNode? ${v} = JsonNode.Parse(${csRaw(c.value)});` : `JsonNode? ${v} = JsonValue.Create(${csRaw(c.value)});`);
        break;
      }
      case 'expression':
        out.push(`JsonNode? ${v} = ExpressionEvaluator.Evaluate(${csStr(c.expression)}, Context(${input}, vars));`);
        break;
      case 'template':
        out.push(`JsonNode? ${v} = TemplateRenderer.Render(JsonNode.Parse(${csRaw(c.template)}), Context(${input}, vars));`);
        break;
      case 'filter':
        out.push(`var ${v}Rows = new JsonArray();`,
          `foreach (var row in ${input} as JsonArray ?? new JsonArray())`,
          `    if (ExpressionEvaluator.EvaluateAsBool(${csStr(c.expression)}, Context(row, vars))) ${v}Rows.Add(row?.DeepClone());`,
          `JsonNode? ${v} = ${v}Rows;`);
        break;
      case 'http': {
        const m = /^([a-z]+:\/\/[^/?#]+)(.*)$/i.exec(c.url.trim()) ?? [null, c.url.trim(), '/'];
        const request = { method: c.method, path: m[2] || '/', headers: c.headers.filter((h) => h.key && h.enabled !== false) };
        if (c.bodyMode === 'json') { request.bodyType = 'json'; try { request.body = JSON.parse(c.body); } catch { request.body = c.body; } }
        if (c.bodyMode === 'input') request.bodyType = 'json';
        if (c.url.startsWith(info.demoApi.baseUrl)) out.push(`// The playground's demo API: point this at your own.`);
        out.push(`var ${v}Request = (JsonObject)TemplateRenderer.Render(JsonNode.Parse(${csRaw(pretty(request))}), Context(${input}, vars))!;`);
        if (c.bodyMode === 'input') out.push(`${v}Request["body"] = ${inputCopy};`);
        out.push(`using var ${v}Message = new HttpRequestMessage(new HttpMethod(${csStr(c.method)}), HttpRequestBuilder.BuildUri(${csStr(m[1])}, ${v}Request, HttpRequestSettings.Default));`);
        if (c.bodyMode !== 'none') out.push(`${v}Message.Content = HttpRequestBuilder.BuildContent(${v}Request, null);`);
        out.push(`HttpRequestBuilder.ApplyHeaders(${v}Message, new Dictionary<string, string>(), ${v}Request, HttpRequestSettings.Default);`);
        const auth = c.auth ?? { type: 'none' };
        if (auth.type !== 'none') {
          out.push('// Keep secrets in configuration, not in code.');
          const registry = TOKEN_SCHEMES.includes(auth.type)
            ? `new AuthStrategyRegistry([new ${auth.type === 'oauth2ClientCredentials' ? 'Oauth2ClientCredentialsStrategy' : auth.type === 'tokenEndpoint' ? 'TokenEndpointStrategy' : 'LoginRequestStrategy'}(new PlainHttpClientFactory(), tokens)])`
            : 'AuthStrategyRegistry.CreateStatic()';
          const authJson = { ...auth };
          if (auth.type === 'loginRequest') authJson.baseUrl = m[1];
          out.push(`await ${registry}.ApplyAsync(${v}Message, (JsonObject)JsonNode.Parse(${csRaw(pretty(authJson))})!, CancellationToken.None);`);
        }
        out.push(`using var ${v}Response = await http.SendAsync(${v}Message);`,
          `JsonNode? ${v} = new JsonObject { ["status"] = (int)${v}Response.StatusCode, ["body"] = JsonOrText(await ${v}Response.Content.ReadAsStringAsync()) };`);
        break;
      }
      case 'file': {
        const opts = c.options?.trim() ? c.options : '{}';
        out.push(`var ${v}Options = (JsonObject)JsonNode.Parse(${csRaw(opts)})!;`);
        if (c.source === 'input') {
          out.push('// Text is read in the format below; a list is already rows.',
            `var ${v}Rows = ${input} is JsonValue ${v}Text && ${v}Text.TryGetValue<string>(out var ${v}Content)`,
            `    ? await ReadRows(new ${FORMAT_CLASS[c.from]}(), ${v}Content, ${v}Options)`,
            `    : ${inputCopy} as JsonArray ?? new JsonArray();`);
        } else if (c.source === 'paste') {
          out.push(`var ${v}Rows = await ReadRows(new ${FORMAT_CLASS[c.from]}(), ${csRaw(c.content)}, ${v}Options);`);
        } else {
          const st = fileStores.find((s) => s.id === c.storeId);
          out.push(`// From the store "${st?.name ?? c.storeId}". A folder is a LocalFileStore; S3, Azure Blob and FTP have their own stores.`,
            `var ${v}Store = new LocalFileStore(${csStr(st?.display ?? '/data/files')});`,
            `await using var ${v}Stream = await ${v}Store.OpenReadAsync(${csStr(c.path)}, CancellationToken.None);`,
            `var ${v}Rows = await new ${FORMAT_CLASS[c.from]}().ReadAsync(${v}Stream, ${v}Options, CancellationToken.None) as JsonArray ?? new JsonArray();`);
        }
        const file = c.target === 'store' ? c.targetPath : `${n.name.replace(/[^A-Za-z0-9]+/g, '-').toLowerCase().replace(/^-|-$/g, '') || 'export'}.${c.to}`;
        out.push(`using (var ${v}Out = new MemoryStream())`, '{',
          `    await new ${FORMAT_CLASS[c.to]}().WriteAsync(${v}Out, ${v}Rows, ${v}Options, suppressHeader: false, CancellationToken.None);`,
          `    await File.WriteAllBytesAsync(${csStr(file)}, ${v}Out.ToArray());`, '}',
          `JsonNode? ${v} = ${v}Rows;`);
        break;
      }
      case 'sql': {
        const conn = connections.find((x) => x.id === c.connectionId);
        const provider = conn?.provider ?? 'sqlite';
        const env = `${(conn?.name ?? 'database').replace(/[^A-Za-z0-9]+/g, '_').toUpperCase()}_CONNECTION`;
        out.push(`var ${v}Db = databases.Get(${csStr(provider)});`,
          `var ${v}Params = (JsonObject)JsonNode.Parse(${csRaw(c.params?.trim() ? c.params : '{}')})!;`,
          `${v}Params["input"] = ${inputCopy};`,
          '// Every {{ }} becomes a parameter: a value can never become SQL.',
          `var (${v}Sql, ${v}Bound) = TemplateRenderer.RenderWithParameters(${csRaw(c.sql)}, new ExpressionContext { Params = ${v}Params }, ${v}Db.ParameterPrefix + "p");`,
          conn?.id === 'sample' ? '// The playground\'s sample is SQLite made for each run; here, any SQLite file with those tables.' : null,
          `await using var ${v}Connection = ${v}Db.CreateConnection(Environment.GetEnvironmentVariable(${csStr(env)}) ?? ${csStr(provider === 'sqlite' ? 'Data Source=app.db' : 'set ' + env)});`,
          `await ${v}Connection.OpenAsync();`,
          `await using var ${v}Command = ${v}Connection.CreateCommand();`,
          `${v}Command.CommandText = ${v}Sql;`,
          `foreach (var p in ${v}Bound) ${v}Db.AddTypedParameter(${v}Command, p.Name, p.Value, null);`,
          `var ${v}Rows = new JsonArray();`,
          `await using (var reader = await ${v}Command.ExecuteReaderAsync())`,
          `    while (await reader.ReadAsync()) ${v}Rows.Add(Row(reader));`,
          `JsonNode? ${v} = ${v}Rows;`);
        break;
      }
      default:
        break;
    }
    out.push(`vars[${csStr(varName(n))}] = ${v}?.DeepClone();`, '');
  }

  const last = ids.length ? names[ids[ids.length - 1]] : null;
  if (last) out.push(`Console.WriteLine(${last}?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));`, '');

  out.push('// ---- helpers', '',
    '// What an expression can read: input (the node before) and vars (every node so far).',
    'static ExpressionContext Context(JsonNode? input, JsonObject vars) => new() { Input = input?.DeepClone(), Vars = vars.DeepClone() };');
  if (types.has('http')) {
    out.push('', 'static JsonNode? JsonOrText(string text)', '{',
      '    try { return JsonNode.Parse(text); }', '    catch (JsonException) { return JsonValue.Create(text); }', '}');
  }
  if (types.has('file')) {
    out.push('', 'static async Task<JsonArray> ReadRows(IFileFormat format, string text, JsonObject options)', '{',
      '    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));',
      '    return await format.ReadAsync(stream, options, CancellationToken.None) as JsonArray ?? new JsonArray();', '}');
  }
  if (types.has('sql')) {
    out.push('', 'static JsonObject Row(DbDataReader reader)', '{',
      '    var row = new JsonObject();',
      '    for (var i = 0; i < reader.FieldCount; i++)',
      '        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : JsonSerializer.SerializeToNode(reader.GetValue(i));',
      '    return row;', '}');
  }
  if (usesTokens) {
    out.push('', '// In an ASP.NET app, use the IHttpClientFactory from AddHttpClient() instead.',
      'sealed class PlainHttpClientFactory : IHttpClientFactory', '{', '    public HttpClient CreateClient(string name) => new();', '}');
  }
  return out.filter((l) => l !== null).join('\n');
}

function packagesFor() {
  const types = new Set(flow.nodes.map((n) => n.type));
  const list = [`${info.nugetPrefix}.Expressions`];
  if (types.has('http')) list.push(`${info.nugetPrefix}.Connectors.Http`);
  if (types.has('file')) list.push(`${info.nugetPrefix}.Connectors.Files`);
  if (types.has('sql')) list.push(`${info.nugetPrefix}.Connectors.Database`);
  return list;
}

function projectFile() {
  return `<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
${packagesFor().map((p) => `    <PackageReference Include="${p}" Version="${info.nugetVersion}" />`).join('\n')}
  </ItemGroup>

</Project>`;
}

function cliSteps() {
  const slug = flow.name.replace(/[^A-Za-z0-9]+/g, '') || 'MyFlow';
  return [
    `dotnet new console -n ${slug}`,
    `cd ${slug}`,
    ...packagesFor().map((p) => `dotnet add package ${p} --version ${info.nugetVersion}`),
    '# Replace Program.cs with the code from the first tab, then:',
    'dotnet run',
  ].join('\n');
}

let codeTab = 'program';

function drawCode() {
  const text = codeTab === 'program' ? csharp() : codeTab === 'project' ? projectFile() : cliSteps();
  $('code-view').textContent = text;
  $('code-download').hidden = codeTab === 'cli';
  return text;
}

function setupCode() {
  subtabs($('code-tabs'), (tab) => { codeTab = tab; drawCode(); });
  $('code-open').addEventListener('click', () => {
    $('code-intro').textContent = `The same libraries this playground runs, from NuGet (${info.nugetVersion}): ${packagesFor().join(', ')}. .NET 8, 9 and 10. MIT.`;
    $('code-status').textContent = '';
    drawCode();
    $('code-dialog').showModal();
  });
  $('code-copy').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(drawCode()); $('code-status').textContent = 'Copied.'; } catch { $('code-status').textContent = 'Select the text and copy it.'; }
  });
  $('code-download').addEventListener('click', () => {
    const text = drawCode();
    const a = el('a', { href: URL.createObjectURL(new Blob([text], { type: 'text/plain' })), download: codeTab === 'program' ? 'Program.cs' : `${flow.name.replace(/[^A-Za-z0-9]+/g, '') || 'MyFlow'}.csproj` });
    document.body.append(a);
    a.click();
    a.remove();
  });
}

// ============================================================================================== connections, stores, dialogs

let connections = [];
let fileStores = [];

const STORE_TEMPLATES = {
  local: (roots) => ({ store: 'local', rootPath: roots[0] ?? '/data/files' }),
  s3: () => ({ store: 's3', bucket: 'my-bucket', region: 'eu-west-1', accessKeyId: '', secretAccessKey: '', rootPath: '' }),
  azureBlob: () => ({ store: 'azureBlob', container: 'exports', connectionString: '' }),
  ftp: () => ({ store: 'ftp', host: 'ftp.example.com', port: 21, username: '', password: '', tls: true, rootPath: '' }),
};

async function loadConnections() {
  try { connections = await call('/api/database/connections', undefined, 'GET'); } catch { connections = [{ id: 'sample', name: 'Sample (SQLite, in memory)', provider: 'sqlite', display: '', source: 'built in' }]; }
  $('conn-list').replaceChildren(...connections.map((c) => el('li', {},
    el('span', {}, el('strong', {}, c.name), ' ', el('span', { class: 'code' }, c.display)),
    el('span', { class: 'row' },
      c.source === 'saved' ? el('button', { class: 'small-btn', onclick: async (e) => { const b = e.target; b.disabled = true; try { const a = await call(`/api/database/connections/${encodeURIComponent(c.id)}/test`, {}); b.textContent = a.ok ? 'Connected' : 'Failed'; b.title = a.error ?? a.serverVersion ?? ''; } catch (x) { b.textContent = x.message; } b.disabled = false; } }, 'Test') : null,
      c.source === 'saved' ? el('button', { class: 'small-btn', onclick: async () => { await call(`/api/database/connections/${encodeURIComponent(c.id)}`, undefined, 'DELETE').catch(() => {}); await loadConnections(); drawInspector(); } }, 'Remove') : el('span', { class: 'hint' }, c.source)))));
}

async function loadStores() {
  if (hosted) { fileStores = []; return; }
  try { fileStores = await call('/api/files/stores', undefined, 'GET'); } catch { fileStores = []; }
  $('stores-list').replaceChildren(...fileStores.map((s) => el('li', {},
    el('span', {}, el('strong', {}, s.name), ' ', el('span', { class: 'code' }, s.display)),
    s.source === 'saved' ? el('button', { class: 'small-btn', onclick: async () => { await call(`/api/files/stores/${encodeURIComponent(s.id)}`, undefined, 'DELETE').catch(() => {}); await loadStores(); drawInspector(); } }, 'Remove') : el('span', { class: 'hint' }, 'from config'))));
  if (!fileStores.length) $('stores-list').append(el('li', {}, el('span', { class: 'hint' }, 'No stores yet.')));
}

function selfHostText() {
  const image = 'ghcr.io/spanflow/libraries-playground:latest';
  $('selfhost-run').textContent = `docker run -d --name libraries-playground -p 8080:8080 \\
  -v playground-data:/data \\
  -e Playground__AccessKey=choose-a-long-key \\
  ${image}

# Then open http://your-server:8080/?key=choose-a-long-key once.
# Files you mount under /data/files can be read and written by Convert file nodes.`;
  $('selfhost-compose').textContent = `# playground/docker-compose.yml in the source, or:
services:
  playground:
    image: ${image}
    ports: ["8080:8080"]
    environment:
      Playground__AccessKey: choose-a-long-key
    volumes:
      - playground-data:/data
volumes:
  playground-data:`;
  $('selfhost-nuget').textContent = `git clone ${info.sourceUrl ?? 'the source repository'}
cd ${(info.sourceUrl ?? '').split('/').pop() || 'spanflow'}
docker build -f playground/Dockerfile.nuget \\
  --build-arg LIBRARIES_VERSION=${info.nugetVersion} \\
  -t libraries-playground .`;
  $('selfhost-packages').textContent = ['Expressions', 'Connectors.Http', 'Connectors.Files', 'Connectors.Database']
    .map((p) => `dotnet add package ${info.nugetPrefix}.${p} --version ${info.nugetVersion}`).join('\n');
}

const PANEL_LABELS = { expressions: 'Expressions (Expression, Shape JSON, Filter rows)', http: 'HTTP request', files: 'Convert file', database: 'SQL query' };

function renderLimits() {
  $('limits-text').textContent = hosted
    ? 'Free and anonymous, so each library has fair-use limits per visitor. Past one, the node says when to try again.'
    : 'Each library has limits per caller (the Playground__Limits__... settings).';
  const rows = Object.entries(info.panels).map(([name, l]) => ({
    Node: PANEL_LABELS[name] ?? name, 'A minute': l.perMinute, 'An hour': l.perHour, Request: kb(l.maxBodyBytes), Answer: kb(l.maxOutputBytes), Rows: l.maxRows, Time: `${l.timeoutMs / 1000} s`,
  }));
  renderTable($('limits-table'), Object.keys(rows[0]), rows);
}

function openDialog(which) {
  $('menu').hidden = true;
  $('menu-open').setAttribute('aria-expanded', 'false');
  if (which === 'connections') {
    $('conn-intro').textContent = hosted
      ? 'Here: the SQLite sample and the demo database, both read only. Run it yourself to add your own PostgreSQL, SQL Server, MySQL or SQLite.'
      : 'Saved on this server\'s volume. Passwords are never shown again.';
    $('conn-box').hidden = hosted;
    $('conn-dialog').showModal();
  } else if (which === 'stores') {
    $('stores-dialog').showModal();
  } else if (which === 'selfhost') {
    $('selfhost-dialog').showModal();
  } else if (which === 'limits') {
    $('limits-dialog').showModal();
  }
}

// ============================================================================================== cookie notice

/** The cookie and storage notice: shown until the visitor picks, kept in localStorage ('sf-consent': all | essential). */
function setupConsent() {
  const box = $('consent');
  $('consent-runs').hidden = !hosted;
  let saved = null;
  try { saved = localStorage.getItem('sf-consent'); } catch { /* private window */ }
  box.hidden = saved === 'all' || saved === 'essential';
  for (const b of box.querySelectorAll('[data-consent]')) {
    b.addEventListener('click', () => {
      try { localStorage.setItem('sf-consent', b.dataset.consent); } catch { /* private window */ }
      box.hidden = true;
    });
  }
  $('menu-cookies').addEventListener('click', () => { $('menu').hidden = true; box.hidden = false; box.querySelector('button').focus(); });
}

function setupDialogs() {
  for (const d of document.querySelectorAll('dialog')) {
    d.addEventListener('click', (e) => { if (e.target === d || e.target.closest('[data-close]')) d.close(); });
  }
  $('menu-open').addEventListener('click', () => {
    const open = $('menu').hidden;
    $('menu').hidden = !open;
    $('menu-open').setAttribute('aria-expanded', String(open));
  });
  document.addEventListener('click', (e) => { if (!e.target.closest('.topactions')) { $('menu').hidden = true; $('menu-open').setAttribute('aria-expanded', 'false'); } });
  for (const b of document.querySelectorAll('[data-menu]')) b.addEventListener('click', () => openDialog(b.dataset.menu));
  $('menu-stores').hidden = hosted;

  options($('conn-provider'), info.providers);
  $('conn-save').addEventListener('click', async () => {
    const status = $('conn-status');
    status.textContent = 'Saving...';
    try {
      await call('/api/database/connections', { id: '', name: $('conn-name').value.trim(), provider: $('conn-provider').value, connectionString: $('conn-string').value, allowWrites: $('conn-writes').checked });
      $('conn-string').value = '';
      $('conn-name').value = '';
      status.textContent = 'Saved.';
      await loadConnections();
      drawInspector();
    } catch (e) { status.textContent = e.message; }
  });

  if (!hosted) {
    options($('store-kind'), info.storeKinds, { local: 'Folder on this server', s3: 'Amazon S3', azureBlob: 'Azure Blob Storage', ftp: 'FTP' });
    const fill = () => { $('store-config').value = pretty(STORE_TEMPLATES[$('store-kind').value](info.localFileRoots)); };
    $('store-kind').addEventListener('change', fill);
    fill();
    $('store-save').addEventListener('click', async () => {
      const status = $('store-status');
      try {
        const config = parseJson($('store-config').value, 'The settings');
        await call('/api/files/stores', { id: '', name: $('store-name').value.trim(), config });
        status.textContent = 'Saved.';
        $('store-name').value = '';
        await loadStores();
        drawInspector();
      } catch (e) { status.textContent = e.message; }
    });
  }
}

// ============================================================================================== key, start

function showKeyBox() {
  $('keybox').hidden = false;
  $('key-input').focus();
}

function setupKeyBox() {
  $('keybox').addEventListener('submit', async (e) => {
    e.preventDefault();
    const key = $('key-input').value.trim();
    if (!key) { $('key-status').textContent = 'Enter the key first.'; return; }
    const check = await fetch('/api/database/connections', { headers: { 'X-Playground-Key': key } });
    if (check.status === 401) { $('key-status').textContent = 'That key is not the one this server uses.'; return; }
    store.set('playground-key', key); // kept in this browser only once it works
    location.reload();
  });
  $('key-input').addEventListener('input', () => { $('key-status').textContent = ''; });
}

function setupFlowBar() {
  $('flow-select').addEventListener('change', (e) => openFlow(e.target.value));
  $('flow-name').addEventListener('input', (e) => {
    flow.name = e.target.value || 'Untitled flow';
    changed();
    const option = $('flow-select').querySelector(`option[value="${flow.id}"]`);
    if (option) option.textContent = flow.name;
  });
  $('flow-new').addEventListener('click', () => {
    const fresh = { id: uid(), name: 'Untitled flow', nodes: [], edges: [] };
    flows.push(fresh);
    flow = fresh;
    save();
    openFlow(fresh.id);
    $('flow-name').select();
  });
  $('flow-delete').addEventListener('click', () => {
    if (flow.starter) return;
    if (!confirm(`Delete "${flow.name}"? This cannot be undone.`)) return;
    flows = flows.filter((f) => f.id !== flow.id);
    save();
    openFlow(starters()[0].id);
  });
  $('run-flow').addEventListener('click', () => runFlow());
}

async function start() {
  setupKeyBox();
  const fromUrl = new URLSearchParams(location.search).get('key');
  if (fromUrl) { store.set('playground-key', fromUrl); history.replaceState(null, '', location.pathname + location.hash); }

  try { info = await (await fetch('/api/info')).json(); } catch { info = null; }
  if (!info) {
    $('off-notice').textContent = 'The playground server could not be reached.';
    $('off-notice').hidden = false;
    return;
  }
  hosted = info.mode === 'hosted';

  for (const e of document.querySelectorAll('[data-product]')) e.textContent = info.productName;
  for (const e of document.querySelectorAll('[data-product-link]')) {
    const url = new URL(info.productUrl);
    url.searchParams.set('utm_source', 'libraries-playground');
    e.href = url.toString();
  }
  for (const e of document.querySelectorAll('[data-source-link]')) { if (info.sourceUrl) e.href = info.sourceUrl; else e.hidden = true; }
  document.title = `${info.productName} libraries playground`;
  $('mode-badge').textContent = hosted ? 'Hosted demo' : 'Self-hosted';
  $('mode-badge').className = `badge ${hosted ? 'warn' : 'safe'}`;
  $('intro-text').textContent = hosted
    ? `Build a flow from nodes. Each node runs one of the MIT-licensed ${info.nugetPrefix} NuGet packages on our server, exactly as your own code would. Requests reach only a demo API and queries only demo databases, so nothing real is touched. Run it on your own server (the menu at the top right) to use your APIs, databases and files.`
    : `Build a flow from nodes. Each node runs one of the MIT-licensed ${info.nugetPrefix} NuGet packages on this server: requests go to your APIs, queries to your databases, files to your folders and buckets.`;
  if (!info.enabled) { $('off-notice').textContent = 'This server has the playground switched off. The page loads, but every node answers "switched off".'; $('off-notice').hidden = false; }
  if (info.keyRequired && !store.get('playground-key', '')) showKeyBox();

  setupDialogs();
  setupConsent();
  selfHostText();
  renderLimits();
  setupPalette();
  setupCanvas();
  setupInspector();
  setupCode();
  setupFlowBar();
  applyView();
  await Promise.all([loadFlows(), loadConnections(), loadStores()]);
  // Links from the website name a library (#http, ...); each opens the starter flow that shows it best.
  const fromHash = { expressions: 'starter-expressions', http: 'starter-api-csv', files: 'starter-csv-rules', database: 'starter-db-api' }[location.hash.slice(1)];
  if (fromHash) history.replaceState(null, '', location.pathname);
  openFlow(fromHash ?? store.get('playground-open', starters()[0].id));
}

start();
