import type { Edge, Node } from '@xyflow/react';
import type { MappingDSL } from '../engine/dslBuilder';

/**
 * A worked example: a real input, the mapping that transforms it, and the
 * resulting output. These power both the in-app Examples gallery and the
 * generated docs/cookbook.md.
 */
export interface Recipe {
  id: string;
  title: string;
  category: string;
  description: string;
  input: unknown;
  dsl: MappingDSL;
  output: unknown;
  /** When true, `output` is the error message this mapping is meant to raise. */
  throws?: boolean;
  /** For a "using a function" example: the example function to seed, and the
   *  mapping graph that calls it (built with the seeded function's id). */
  functionName?: string;
  buildMapping?: (functionId: string) => { nodes: Node[]; edges: Edge[] };
}

export const RECIPES: Recipe[] = [
  // ---- Basics ----------------------------------------------------------
  {
    id: 'rename',
    title: 'Rename fields',
    category: 'Basics',
    description: 'Copy values across to differently named target fields.',
    input: { id: 101, fullName: 'Alice Smith', salary: 75000 },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'employeeId', type: 'fieldCopy', path: 'input.id' },
          { target: 'name', type: 'fieldCopy', path: 'input.fullName' },
          { target: 'annualSalary', type: 'fieldCopy', path: 'input.salary' },
        ],
      },
    },
    output: { employeeId: 101, name: 'Alice Smith', annualSalary: 75000 },
  },
  {
    id: 'nested-object',
    title: 'Reshape nested objects',
    category: 'Basics',
    description: 'Read from a nested source path and write to a nested target path.',
    input: { user: { name: 'Bob', age: 30 }, meta: { active: true } },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'profile.displayName', type: 'fieldCopy', path: 'input.user.name' },
          { target: 'profile.isActive', type: 'fieldCopy', path: 'input.meta.active' },
        ],
      },
    },
    output: { profile: { displayName: 'Bob', isActive: true } },
  },
  {
    id: 'constant',
    title: 'Add a constant value',
    category: 'Basics',
    description: 'Emit a fixed value that is not in the source (e.g. a version tag).',
    input: { id: 1 },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'id', type: 'fieldCopy', path: 'input.id' },
          { target: 'schemaVersion', type: 'fieldCopy', path: "'v1'" },
        ],
      },
    },
    output: { id: 1, schemaVersion: 'v1' },
  },

  // ---- Transforms ------------------------------------------------------
  {
    id: 'concat',
    title: 'Concatenate strings',
    category: 'Transforms',
    description: 'Build one string from several fields and a literal separator.',
    input: { firstName: 'Ada', lastName: 'Lovelace' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'fullName', type: 'transform', function: 'concat', args: ['input.firstName', "' '", 'input.lastName'] },
        ],
      },
    },
    output: { fullName: 'Ada Lovelace' },
  },
  {
    id: 'value-map',
    title: 'Look up a value (value-map)',
    category: 'Transforms',
    description: 'Translate codes to labels, with a default for anything unmatched.',
    input: { status: 'C' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'statusLabel',
            type: 'valueMap',
            input: 'input.status',
            table: { C: 'Completed', P: 'Pending', X: 'Cancelled' },
            default: 'Unknown',
          },
        ],
      },
    },
    output: { statusLabel: 'Completed' },
  },
  {
    id: 'condition',
    title: 'If / else grade',
    category: 'Transforms',
    description: 'Choose an output based on a comparison.',
    input: { score: 82 },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'grade', type: 'condition', if: { op: 'gte', input: 'input.score', value: 80 }, then: "'Pass'", else: "'Fail'" },
        ],
      },
    },
    output: { grade: 'Pass' },
  },

  // ---- Aggregates ------------------------------------------------------
  {
    id: 'aggregate-sum',
    title: 'Sum a list',
    category: 'Aggregates',
    description: 'Total a numeric field across an array.',
    input: { orders: [{ amount: 100 }, { amount: 250 }, { amount: 50 }] },
    dsl: {
      target: {
        type: 'object',
        mappings: [{ target: 'total', type: 'aggregate', function: 'sum', from: 'input.orders[]', select: 'amount' }],
      },
    },
    output: { total: 400 },
  },
  {
    id: 'aggregate-stats',
    title: 'Several stats at once',
    category: 'Aggregates',
    description: 'Count, average, min and max over the same list.',
    input: { orders: [{ amount: 100 }, { amount: 250 }, { amount: 50 }] },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'count', type: 'aggregate', function: 'count', from: 'input.orders[]' },
          { target: 'average', type: 'aggregate', function: 'avg', from: 'input.orders[]', select: 'amount' },
          { target: 'min', type: 'aggregate', function: 'min', from: 'input.orders[]', select: 'amount' },
          { target: 'max', type: 'aggregate', function: 'max', from: 'input.orders[]', select: 'amount' },
        ],
      },
    },
    output: { count: 3, average: 133.33333333333334, min: 50, max: 250 },
  },

  // ---- Lists -----------------------------------------------------------
  {
    id: 'filter',
    title: 'Filter a list',
    category: 'Lists',
    description: 'Keep only the items that match a condition.',
    input: { orders: [{ id: 1, status: 'Completed' }, { id: 2, status: 'Pending' }, { id: 3, status: 'Completed' }] },
    dsl: {
      target: {
        type: 'object',
        mappings: [{ target: 'completed', type: 'filter', from: 'input.orders', where: { field: 'status', op: 'eq', value: 'Completed' } }],
      },
    },
    output: { completed: [{ id: 1, status: 'Completed' }, { id: 3, status: 'Completed' }] },
  },
  {
    id: 'sort',
    title: 'Sort a list',
    category: 'Lists',
    description: 'Order a list by a key (descending here).',
    input: { orders: [{ id: 1, amount: 100 }, { id: 2, amount: 250 }, { id: 3, amount: 50 }] },
    dsl: {
      target: {
        type: 'object',
        mappings: [{ target: 'byAmountDesc', type: 'sort', from: 'input.orders[]', keys: [{ by: 'amount', order: 'desc' }] }],
      },
    },
    output: { byAmountDesc: [{ id: 2, amount: 250 }, { id: 1, amount: 100 }, { id: 3, amount: 50 }] },
  },
  {
    id: 'list-rename',
    title: 'Array → array (rename per item)',
    category: 'Lists',
    description: 'Iterate a source list and rename each item’s fields.',
    input: { orders: [{ id: 1, amount: 100 }, { id: 2, amount: 250 }] },
    dsl: {
      target: {
        type: 'object',
        mappings: [{ target: 'lines', type: 'foreach', from: 'input.orders[]', map: { orderId: 'id', value: 'amount' } }],
      },
    },
    output: { lines: [{ orderId: 1, value: 100 }, { orderId: 2, value: 250 }] },
  },
  {
    id: 'list-function',
    title: 'Array → array with a function per item',
    category: 'Lists',
    description: 'Compute a new field for every element (here, a concat).',
    input: { person: [{ fullName: 'Alice Smith', department: 'Engineering' }, { fullName: 'Bob Johnson', department: 'Marketing' }] },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'people',
            type: 'foreach',
            from: 'input.person[]',
            map: { displayName: { function: 'concat', args: ['fullName', "' - '", 'department'] } },
          },
        ],
      },
    },
    output: { people: [{ displayName: 'Alice Smith - Engineering' }, { displayName: 'Bob Johnson - Marketing' }] },
  },

  // ---- Array-root outputs ---------------------------------------------
  {
    id: 'array-root',
    title: 'Output a top-level array',
    category: 'Array output',
    description: 'The whole result is an array, one object per source element.',
    input: { person: [{ id: 101, fullName: 'Alice Smith', salary: 75000 }, { id: 102, fullName: 'Bob Johnson', salary: 92000 }] },
    dsl: {
      target: {
        type: 'array',
        from: 'input.person[]',
        map: { employeeId: 'id', name: 'fullName', annualSalary: 'salary' },
      },
    },
    output: [
      { employeeId: 101, name: 'Alice Smith', annualSalary: 75000 },
      { employeeId: 102, name: 'Bob Johnson', annualSalary: 92000 },
    ],
  },
  {
    id: 'array-root-source',
    title: 'Top-level array in AND out',
    category: 'Array output',
    description: 'Source is itself an array; iterate it with input[] and reshape each item.',
    input: [
      { id: 1, fullName: 'Alice Smith', department: 'Engineering' },
      { id: 2, fullName: 'Bob Johnson', department: 'Marketing' },
    ],
    dsl: {
      target: {
        type: 'array',
        from: 'input[]',
        map: { employeeId: 'id', label: { function: 'concat', args: ['fullName', "' - '", 'department'] } },
      },
    },
    output: [
      { employeeId: 1, label: 'Alice Smith - Engineering' },
      { employeeId: 2, label: 'Bob Johnson - Marketing' },
    ],
  },

  // ---- Grouping & joining ---------------------------------------------
  {
    id: 'group-by',
    title: 'Group by a key',
    category: 'Grouping & joining',
    description: 'Group rows by a field, then per group collect the items and count them.',
    input: {
      records: [
        { id: 1, name: 'Alice', date: '2021-01-01' },
        { id: 2, name: 'Bob', date: '2021-02-01' },
        { id: 3, name: 'Alice', date: '2021-03-01' },
      ],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'groups',
            type: 'groupBy',
            from: 'input.records[]',
            key: 'name',
            map: {
              name: 'key',
              count: { aggregate: { function: 'count' } },
              ids: { aggregate: { function: 'distinct', select: 'id' } },
              dates: { aggregate: { function: 'distinct', select: 'date' } },
            },
          },
        ],
      },
    },
    output: {
      groups: [
        { name: 'Alice', count: 2, ids: [1, 3], dates: ['2021-01-01', '2021-03-01'] },
        { name: 'Bob', count: 1, ids: [2], dates: ['2021-02-01'] },
      ],
    },
  },
  {
    id: 'group-by-items',
    title: 'Keep the grouped rows',
    category: 'Grouping & joining',
    description:
      "Aggregates collapse a group to a scalar. To keep the rows themselves, use the `items` aggregate: with `select` for one field, without it for the whole objects. Inside a groupBy map, `group[]` names the group's own rows, so another node (foreach, filter, sort, …) can transform them.",
    input: {
      records: [
        { id: 1, name: 'Alice', date: '2021-01-01' },
        { id: 2, name: 'Bob', date: '2021-02-01' },
        { id: 3, name: 'Alice', date: '2021-03-01' },
      ],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'groups',
            type: 'groupBy',
            from: 'input.records[]',
            key: 'name',
            map: {
              name: 'key',
              count: { aggregate: { function: 'count' } },
              records: { aggregate: { function: 'items' } },
              ids: { aggregate: { function: 'items', select: 'id' } },
              entries: { foreach: 'group[]', map: { recordId: 'id', on: 'date' } },
              recent: { filter: { from: 'group[]', where: { field: 'id', op: 'gte', value: 3 } } },
            },
          },
        ],
      },
    },
    output: {
      groups: [
        {
          name: 'Alice',
          count: 2,
          records: [
            { id: 1, name: 'Alice', date: '2021-01-01' },
            { id: 3, name: 'Alice', date: '2021-03-01' },
          ],
          ids: [1, 3],
          entries: [
            { recordId: 1, on: '2021-01-01' },
            { recordId: 3, on: '2021-03-01' },
          ],
          recent: [{ id: 3, name: 'Alice', date: '2021-03-01' }],
        },
        {
          name: 'Bob',
          count: 1,
          records: [{ id: 2, name: 'Bob', date: '2021-02-01' }],
          ids: [2],
          entries: [{ recordId: 2, on: '2021-02-01' }],
          recent: [],
        },
      ],
    },
  },
  {
    id: 'group-by-where-having',
    title: 'Group with a condition (where / having)',
    category: 'Grouping & joining',
    description:
      'Both filters are optional. `where` drops rows BEFORE grouping, so the key and the aggregates only see the survivors. Here the cancelled order is excluded, so C1 totals 120, not 1119. `having` then drops whole groups by their computed values: C2 totals only 50 and is dropped. Rules combine with `all` (AND) or `any` (OR). In the designer, wire a Where node into the Group By node\'s `where` or `having` port; leave a port empty and that filter simply does not apply.',
    input: {
      orders: [
        { customerId: 'C1', status: 'Completed', amount: 100 },
        { customerId: 'C1', status: 'Cancelled', amount: 999 },
        { customerId: 'C1', status: 'Completed', amount: 20 },
        { customerId: 'C2', status: 'Completed', amount: 50 },
      ],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'customers',
            type: 'groupBy',
            from: 'input.orders[]',
            key: 'customerId',
            where: { field: 'status', op: 'eq', value: 'Completed' },
            having: { field: 'total', op: 'gt', value: 100 },
            map: {
              customerId: 'key',
              total: { aggregate: { function: 'sum', select: 'amount' } },
              orderCount: { aggregate: { function: 'count' } },
            },
          },
        ],
      },
    },
    output: { customers: [{ customerId: 'C1', total: 120, orderCount: 2 }] },
  },
  {
    id: 'join',
    title: 'Join two lists',
    category: 'Grouping & joining',
    description: 'Match rows from two collections on a key.',
    input: {
      orders: [{ orderId: 1, customerId: 'c1', amount: 100 }, { orderId: 2, customerId: 'c2', amount: 250 }],
      customers: [{ custId: 'c1', name: 'Alice' }, { custId: 'c2', name: 'Bob' }],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'rows', type: 'join', left: 'input.orders[]', right: 'input.customers[]', leftKey: 'customerId', rightKey: 'custId' },
        ],
      },
    },
    output: {
      rows: [
        { orderId: 1, customerId: 'c1', amount: 100, custId: 'c1', name: 'Alice' },
        { orderId: 2, customerId: 'c2', amount: 250, custId: 'c2', name: 'Bob' },
      ],
    },
  },

  // ---- Validation ------------------------------------------------------
  {
    id: 'exception',
    title: 'Raise a validation error',
    category: 'Validation',
    description: 'Fail with a clear message when a value breaks a rule.',
    input: { amount: -5 },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'checkedAmount', type: 'exception', from: 'input.amount', op: 'lt', value: 0, message: 'Amount cannot be negative' },
        ],
      },
    },
    output: 'Amount cannot be negative',
    throws: true,
  },

  // ---- Conversions -----------------------------------------------------
  // All of these run through the engine's ValueConverter. A value that can't be
  // converted gives null (wrap it in coalesce for a fallback).
  {
    id: 'convert-numbers',
    title: 'Text to int / decimal / bool',
    category: 'Conversions',
    description: 'Turn text fields from a CSV or form into real numbers and booleans, rounding and formatting on the way.',
    input: { qty: '3', price: '19.999', discount: '2.5', active: 'yes' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'quantity', type: 'transform', function: 'toInt', args: ['input.qty'] },
          { target: 'price', type: 'transform', function: 'toDecimal', args: ['input.price'] },
          { target: 'priceText', type: 'transform', function: 'toString', args: [{ transform: { function: 'toDecimal', args: ['input.price'] } }, "'N2'"] },
          { target: 'roundedDiscount', type: 'transform', function: 'toInt', args: ['input.discount', "'round'"] },
          { target: 'active', type: 'transform', function: 'toBool', args: ['input.active'] },
        ],
      },
    },
    output: { quantity: 3, price: 19.999, priceText: '20.00', roundedDiscount: 3, active: true },
  },
  {
    id: 'convert-dates',
    title: 'Reformat a date (and time zone)',
    category: 'Conversions',
    description: 'Read a dd/MM/yyyy date and write it as ISO 8601, as a US date, and as local time in Karachi.',
    input: { placedAt: '15/01/2024 10:30' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'iso', type: 'transform', function: 'formatDate', args: ['input.placedAt', "'iso'", "'dd/MM/yyyy HH:mm'"] },
          { target: 'usDate', type: 'transform', function: 'formatDate', args: ['input.placedAt', "'MM/dd/yyyy'", "'dd/MM/yyyy HH:mm'"] },
          { target: 'karachi', type: 'transform', function: 'formatDate', args: ['input.placedAt', "'yyyy-MM-dd HH:mm'", "'dd/MM/yyyy HH:mm'", "'Asia/Karachi'"] },
        ],
      },
    },
    output: { iso: '2024-01-15T10:30:00Z', usDate: '01/15/2024', karachi: '2024-01-15 15:30' },
  },
  {
    id: 'convert-epochs',
    title: 'Dates ⇄ epoch (s / ms)',
    category: 'Conversions',
    description: 'Date to Unix seconds and milliseconds, a millisecond epoch back to a date, and ms to s.',
    input: { createdAt: '2024-01-15T10:30:00Z', shippedTs: 1705400000000 },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'createdSeconds', type: 'transform', function: 'toEpoch', args: ['input.createdAt'] },
          { target: 'createdMs', type: 'transform', function: 'toEpoch', args: ['input.createdAt', "'ms'"] },
          { target: 'shippedAt', type: 'transform', function: 'fromEpoch', args: ['input.shippedTs', "'ms'"] },
          { target: 'shippedSeconds', type: 'transform', function: 'convertEpoch', args: ['input.shippedTs', "'ms'", "'s'"] },
        ],
      },
    },
    output: { createdSeconds: 1705314600, createdMs: 1705314600000, shippedAt: '2024-01-16T10:13:20Z', shippedSeconds: 1705400000 },
  },
  {
    id: 'convert-uuids',
    title: 'Check and normalise UUIDs',
    category: 'Conversions',
    description: 'Normalise a UUID, check its version, read the time out of a v7, and make a stable v5 id from a name.',
    input: { id: '919108F752D143209BACF847DB4148A8', ref: '017f22e2-79b0-7cc3-98c4-dc0c0c07398f', code: 'ABC-1' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'id', type: 'transform', function: 'toUuid', args: ['input.id'] },
          { target: 'idIsV4', type: 'transform', function: 'isUuid', args: ['input.id', 4] },
          { target: 'codeIsUuid', type: 'transform', function: 'isUuid', args: ['input.code'] },
          { target: 'refVersion', type: 'transform', function: 'uuidVersion', args: ['input.ref'] },
          { target: 'refCreated', type: 'transform', function: 'uuidTimestamp', args: ['input.ref'] },
          { target: 'stableId', type: 'transform', function: 'newUuid', args: [5, "'dns'", "'orders.example.com'"] },
        ],
      },
    },
    output: {
      id: '919108f7-52d1-4320-9bac-f847db4148a8',
      idIsV4: true,
      codeIsUuid: false,
      refVersion: 7,
      refCreated: '2022-02-22T19:22:22Z',
      stableId: '341c1770-d2ab-56be-98c6-765ed848c864',
    },
  },
  {
    id: 'convert-hash-encode',
    title: 'Hash a lookup key and encode text',
    category: 'Conversions',
    description: 'sha256 turns an email into a stable lookup key (the same address, the same key); base64 and URL encoding shape text for an API. Keyless only: a peppered hash or encryption belongs in a pipeline node.',
    input: { email: 'ada@example.com', name: 'Ada Lovelace', company: 'Ada & Co / London' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'emailKey', type: 'transform', function: 'sha256', args: ['input.email'] },
          { target: 'nameBase64', type: 'transform', function: 'base64Encode', args: ['input.name'] },
          { target: 'companyParam', type: 'transform', function: 'urlEncode', args: ['input.company'] },
        ],
      },
    },
    output: {
      emailKey: 'b5fc85e55755f9e0d030a10ab4429b6b2944855f9a0d60077fe832becbc41d72',
      nameBase64: 'QWRhIExvdmVsYWNl',
      companyParam: 'Ada%20%26%20Co%20%2F%20London',
    },
  },
  {
    id: 'convert-type-check',
    title: 'Check what type a value is',
    category: 'Conversions',
    description: 'isType answers yes/no for one type (strictly: 4.2 is not an int); detectType names the type inside text.',
    input: { a: '42', b: '4.2', c: '2024-01-15', d: 'hello' },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          { target: 'aIsInt', type: 'transform', function: 'isType', args: ['input.a', "'int'"] },
          { target: 'bIsInt', type: 'transform', function: 'isType', args: ['input.b', "'int'"] },
          { target: 'aType', type: 'transform', function: 'detectType', args: ['input.a'] },
          { target: 'bType', type: 'transform', function: 'detectType', args: ['input.b'] },
          { target: 'cType', type: 'transform', function: 'detectType', args: ['input.c'] },
          { target: 'dType', type: 'transform', function: 'detectType', args: ['input.d'] },
        ],
      },
    },
    output: { aIsInt: true, bIsInt: false, aType: 'integer', bType: 'number', cType: 'datetime', dType: 'string' },
  },

  // ---- Real data: NZ Annual Enterprise Survey ---------------------------
  // Rows are "long format": one row per (year, industry, size band, variable),
  // and EVERY value is a string, straight from the CSV. The engine coerces
  // numeric text, so sum/gte/sort work on "8866" without a conversion step.
  {
    id: 'survey-where',
    title: 'Survey: income by industry (where)',
    category: 'Real data: enterprise survey',
    description:
      'The survey CSV is one row per metric, so a plain sum over `value` would add incomes to headcounts and expenditure. `where` fixes that: it keeps only the rows you mean BEFORE grouping (here Total income for 2011), so the group only ever sees comparable rows. Two rules combined with `all` (AND). Note the values are strings ("8866"); the engine reads numeric text as a number, so no conversion node is needed.',
    input: {
      rows: [
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Activity unit', value: '46134', unit: 'COUNT' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Total income', value: '8866', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Total expenditure', value: '7618', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'b_1-5', variable: 'Total income', value: '13771', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Activity unit', value: '8382', unit: 'COUNT' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Total income', value: '2004', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'b_1-5', variable: 'Total income', value: '3683', unit: 'DOLLARS(millions)' },
        { year: '2012', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Total income', value: '9615', unit: 'DOLLARS(millions)' },
      ],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'incomeByIndustry',
            type: 'groupBy',
            from: 'input.rows[]',
            key: 'industry_code_ANZSIC',
            where: {
              all: [
                { field: 'variable', op: 'eq', value: 'Total income' },
                { field: 'year', op: 'eq', value: '2011' },
              ],
            },
            map: {
              code: 'key',
              industry: { aggregate: { function: 'first', select: 'industry_name_ANZSIC' } },
              totalIncome: { aggregate: { function: 'sum', select: 'value' } },
              sizeBands: { aggregate: { function: 'items', select: 'rme_size_grp' } },
            },
          },
        ],
      },
    },
    output: {
      incomeByIndustry: [
        { code: 'A', industry: 'Agriculture, Forestry and Fishing', totalIncome: 22637, sizeBands: ['a_0', 'b_1-5'] },
        { code: 'C', industry: 'Manufacturing', totalIncome: 5687, sizeBands: ['a_0', 'b_1-5'] },
      ],
    },
  },
  {
    id: 'survey-having',
    title: 'Survey: only the big industries (having)',
    category: 'Real data: enterprise survey',
    description:
      'The same survey, already extracted down to Total income rows, so no `where` is needed. `having` works on the OTHER side of the grouping: every row is grouped and summed first, then whole groups are dropped by what the aggregate came out to. Here only industries with income of at least 20,000M survive: Manufacturing totals 11,516 across both years and disappears entirely. Note the clause names `totalIncome`, an OUTPUT field of the map, not a field of the input rows.',
    input: {
      rows: [
        { year: '2011', industry_code_ANZSIC: 'A', rme_size_grp: 'a_0', variable: 'Total income', value: '8866' },
        { year: '2011', industry_code_ANZSIC: 'A', rme_size_grp: 'b_1-5', variable: 'Total income', value: '13771' },
        { year: '2011', industry_code_ANZSIC: 'C', rme_size_grp: 'a_0', variable: 'Total income', value: '2004' },
        { year: '2011', industry_code_ANZSIC: 'C', rme_size_grp: 'b_1-5', variable: 'Total income', value: '3683' },
        { year: '2012', industry_code_ANZSIC: 'A', rme_size_grp: 'a_0', variable: 'Total income', value: '9615' },
        { year: '2012', industry_code_ANZSIC: 'A', rme_size_grp: 'b_1-5', variable: 'Total income', value: '14462' },
        { year: '2012', industry_code_ANZSIC: 'C', rme_size_grp: 'a_0', variable: 'Total income', value: '1984' },
        { year: '2012', industry_code_ANZSIC: 'C', rme_size_grp: 'b_1-5', variable: 'Total income', value: '3845' },
      ],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'bigIndustries',
            type: 'groupBy',
            from: 'input.rows[]',
            key: 'industry_code_ANZSIC',
            having: { field: 'totalIncome', op: 'gte', value: 20000 },
            map: {
              code: 'key',
              totalIncome: { aggregate: { function: 'sum', select: 'value' } },
              rows: { aggregate: { function: 'count' } },
            },
          },
        ],
      },
    },
    output: { bigIndustries: [{ code: 'A', totalIncome: 46714, rows: 4 }] },
  },
  {
    id: 'survey-pipeline',
    title: 'Survey: a full report (many nodes)',
    category: 'Real data: enterprise survey',
    description:
      'One pass over the raw survey producing three different shapes, to show how nodes compose. (1) grandTotalIncome nests a Filter inside an Aggregate: filter to income rows, then sum them. (2) profitBandsRanked chains Filter → Sort: profit rows, ranked highest first (the engine sorts "1247" above "770" numerically, not as text). (3) byIndustry uses BOTH group filters at once: `where` keeps only dollar rows, then per-group aggregates split income from expenditure with their own inner conditions, `group[]` re-projects the group\'s own rows as line items, and `having` finally drops any industry under 20,000M, which is why Manufacturing (7,671) is absent from the result.',
    input: {
      rows: [
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Activity unit', value: '46134', unit: 'COUNT' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Total income', value: '8866', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Total expenditure', value: '7618', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Operating profit before tax', value: '770', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'b_1-5', variable: 'Total income', value: '13771', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'b_1-5', variable: 'Total expenditure', value: '12316', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'b_1-5', variable: 'Operating profit before tax', value: '1247', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Activity unit', value: '8382', unit: 'COUNT' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Total income', value: '2004', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Total expenditure', value: '1796', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Operating profit before tax', value: '204', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'b_1-5', variable: 'Total income', value: '3683', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'b_1-5', variable: 'Total expenditure', value: '3458', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'b_1-5', variable: 'Operating profit before tax', value: '241', unit: 'DOLLARS(millions)' },
        { year: '2012', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Total income', value: '9615', unit: 'DOLLARS(millions)' },
        { year: '2012', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Total income', value: '1984', unit: 'DOLLARS(millions)' },
      ],
    },
    dsl: {
      target: {
        type: 'object',
        mappings: [
          {
            target: 'grandTotalIncome',
            type: 'aggregate',
            function: 'sum',
            select: 'value',
            input: {
              filter: { from: 'input.rows[]', where: { field: 'variable', op: 'eq', value: 'Total income' } },
            },
          },
          {
            target: 'profitBandsRanked',
            type: 'sort',
            input: {
              filter: {
                from: 'input.rows[]',
                where: { field: 'variable', op: 'eq', value: 'Operating profit before tax' },
              },
            },
            keys: [{ by: 'value', order: 'desc' }],
          },
          {
            target: 'byIndustry',
            type: 'groupBy',
            from: 'input.rows[]',
            key: 'industry_code_ANZSIC',
            where: { field: 'unit', op: 'eq', value: 'DOLLARS(millions)' },
            having: { field: 'income', op: 'gt', value: 20000 },
            map: {
              code: 'key',
              industry: { aggregate: { function: 'first', select: 'industry_name_ANZSIC' } },
              income: {
                aggregate: {
                  function: 'sum',
                  select: 'value',
                  where: { field: 'variable', op: 'eq', value: 'Total income' },
                },
              },
              spend: {
                aggregate: {
                  function: 'sum',
                  select: 'value',
                  where: { field: 'variable', op: 'eq', value: 'Total expenditure' },
                },
              },
              years: { aggregate: { function: 'distinct', select: 'year' } },
              incomeLines: {
                foreach: 'group[]',
                map: { year: 'year', band: 'rme_size_grp', metric: 'variable', amount: 'value' },
              },
            },
          },
        ],
      },
    },
    output: {
      grandTotalIncome: 39923,
      profitBandsRanked: [
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'b_1-5', variable: 'Operating profit before tax', value: '1247', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'A', industry_name_ANZSIC: 'Agriculture, Forestry and Fishing', rme_size_grp: 'a_0', variable: 'Operating profit before tax', value: '770', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'b_1-5', variable: 'Operating profit before tax', value: '241', unit: 'DOLLARS(millions)' },
        { year: '2011', industry_code_ANZSIC: 'C', industry_name_ANZSIC: 'Manufacturing', rme_size_grp: 'a_0', variable: 'Operating profit before tax', value: '204', unit: 'DOLLARS(millions)' },
      ],
      byIndustry: [
        {
          code: 'A',
          industry: 'Agriculture, Forestry and Fishing',
          income: 32252,
          spend: 19934,
          years: ['2011', '2012'],
          incomeLines: [
            { year: '2011', band: 'a_0', metric: 'Total income', amount: '8866' },
            { year: '2011', band: 'a_0', metric: 'Total expenditure', amount: '7618' },
            { year: '2011', band: 'a_0', metric: 'Operating profit before tax', amount: '770' },
            { year: '2011', band: 'b_1-5', metric: 'Total income', amount: '13771' },
            { year: '2011', band: 'b_1-5', metric: 'Total expenditure', amount: '12316' },
            { year: '2011', band: 'b_1-5', metric: 'Operating profit before tax', amount: '1247' },
            { year: '2012', band: 'a_0', metric: 'Total income', amount: '9615' },
          ],
        },
      ],
    },
  },
];

export const RECIPE_CATEGORIES = [...new Set(RECIPES.map((r) => r.category))];
