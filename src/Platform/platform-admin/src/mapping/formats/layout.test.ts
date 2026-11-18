import { describe, it, expect } from 'vitest';
import {
  addField,
  changeKind,
  changeSize,
  firstPositions,
  guessLayout,
  measure,
  moveField,
  removeField,
  setField,
  summarise,
  switchMode,
  type Layout,
} from './layout';

// The epic's PK example: country 2, account 13, phone 11, then an LL-prefixed reference.
const PK = 'PK0001112223344' + '03235544789' + '05' + 'AB123';
const pkLayout: Layout = {
  fields: [
    { name: 'country', length: 2, type: 'alpha' },
    { name: 'account', length: 13, type: 'numeric' },
    { name: 'phone', length: 11, type: 'numeric' },
    { name: 'reference', prefix: 2, type: 'alphanumeric' },
  ],
};

describe('measure (the ruler)', () => {
  it('places fixed and length-prefixed fields on the PK string', () => {
    const ruler = measure(pkLayout, PK);
    expect(ruler.problems).toEqual([]);
    const plain = ruler.spans.map((s) => [s.label, s.kind, s.start, s.end]);
    expect(plain).toEqual([
      ['country', 'value', 0, 2],
      ['account', 'value', 2, 15],
      ['phone', 'value', 15, 26],
      ['reference length (5)', 'prefix', 26, 28],
      ['reference', 'value', 28, 33],
    ]);
    const pos = firstPositions(ruler);
    expect(pos.get('-:3')).toEqual(expect.objectContaining({ start: 26, end: 33 }));
  });

  it('an empty layout places nothing and reports nothing', () => {
    expect(measure({ fields: [] }, PK)).toEqual({ spans: [], problems: [], lineRecords: [] });
  });

  it('reports characters left over and a short numeric field', () => {
    expect(measure(pkLayout, PK + 'XYZ').problems[0].message).toBe('3 characters left after the last field.');
    const short = measure({ fields: [{ name: 'a', length: 4, type: 'numeric' }] }, '12');
    expect(short.problems[0].message).toBe('a: needs 4 characters but only 2 are left.');
  });

  it('reads a short text field at the end as padded, like the codec', () => {
    const ruler = measure({ fields: [{ name: 'id', length: 2 }, { name: 'memo', length: 10, type: 'text' }] }, '01hello');
    expect(ruler.problems).toEqual([]);
    expect(ruler.spans[1]).toEqual(expect.objectContaining({ start: 2, end: 7 }));
  });

  it('refuses a prefix that is not digits or says more than is left', () => {
    expect(measure({ fields: [{ name: 'r', prefix: 2 }] }, 'AB12').problems[0].message).toContain('needs a 2-digit length first');
    expect(measure({ fields: [{ name: 'r', prefix: 2 }] }, '09ABC').problems[0].message).toContain('says it is 9 long but only 3');
    expect(measure({ fields: [{ name: 'r', prefix: 2, max: 3 }] }, '05ABCDE').problems[0].message).toContain('more than its max of 3');
  });

  it('repeats a group by an earlier count field, all in the group colour', () => {
    const layout: Layout = {
      fields: [
        { name: 'count', length: 1, type: 'integer' },
        { name: 'items', repeat: 'count', fields: [{ name: 'sku', length: 2 }, { name: 'qty', length: 1, type: 'integer' }] },
      ],
    };
    const ruler = measure(layout, '2AA1BB2');
    expect(ruler.problems).toEqual([]);
    expect(ruler.spans.map((s) => [s.label, s.start, s.colour])).toEqual([
      ['count', 0, 0],
      ['sku', 1, 1],
      ['qty', 3, 1],
      ['sku', 4, 1],
      ['qty', 6, 1],
    ]);
  });

  it('chooses a record per line by its code and flags lines no record matches', () => {
    const layout: Layout = {
      records: [
        { name: 'header', when: { start: 0, equals: '01' }, fields: [{ const: '01', length: 2 }, { name: 'bank', length: 3 }] },
        { name: 'details', when: { start: 0, equals: '02' }, many: true, fields: [{ const: '02', length: 2 }, { name: 'amt', length: 3, type: 'numeric' }] },
      ],
    };
    const ruler = measure(layout, '01ABC\n02100\n02200\n99XXX\n');
    expect(ruler.lineRecords).toEqual([0, 1, 1, null, null]);
    expect(ruler.problems).toEqual([{ line: 3, offset: 0, message: 'No record in the layout matches this line.' }]);
    expect(ruler.spans.filter((s) => s.line === 2).map((s) => s.kind)).toEqual(['const', 'value']);
  });

  it('flags a constant that does not match', () => {
    const ruler = measure({ fields: [{ const: 'HD', length: 2 }, { name: 'x', rest: true }] }, 'XXabc');
    expect(ruler.problems[0].message).toBe('const "HD": expected "HD", found "XX".');
  });
});

describe('measure with a bitmap (E73-T9)', () => {
  const members = [
    { bit: 2, name: 'pan', prefix: 2, type: 'numeric' },
    { bit: 3, name: 'processingCode', length: 6, type: 'numeric' },
    { bit: 70, name: 'networkManagementCode', length: 3, type: 'numeric' },
  ];
  const layout: Layout = { fields: [{ name: 'mti', length: 4, type: 'numeric' }, { bitmap: 'hex', fields: members }] };

  it('reads only the fields whose bits are set, after the bitmap', () => {
    const ruler = measure(layout, '0200' + '6000000000000000' + '04' + '1234' + '000000');
    expect(ruler.problems).toEqual([]);
    expect(ruler.spans.map((s) => [s.label, s.kind, s.start, s.end])).toEqual([
      ['mti', 'value', 0, 4],
      ['bitmap primary', 'bitmap', 4, 20],
      ['pan length (4)', 'prefix', 20, 22],
      ['pan', 'value', 22, 26],
      ['processingCode', 'value', 26, 32],
    ]);
    expect(ruler.spans[3].loc).toEqual({ record: null, path: [1, 0] });
  });

  it('reads a secondary bitmap when bit 1 is set', () => {
    const ruler = measure(layout, '0800' + '8000000000000000' + '0400000000000000' + '301');
    expect(ruler.problems).toEqual([]);
    expect(ruler.spans.map((s) => s.label)).toEqual(['mti', 'bitmap primary', 'bitmap secondary', 'networkManagementCode']);
  });

  it('names a set bit with no field, and a bitmap that is not hex', () => {
    expect(measure(layout, '0200' + '1000000000000000' + 'x').problems[0].message).toBe('bitmap: has bit 4 set but no field for it.');
    expect(measure(layout, '0200' + 'ZZ00000000000000').problems[0].message).toContain('bitmap must be 16 hex characters');
  });

  it('adds a field to a bitmap with the next bit', () => {
    const next = addField(layout, null, undefined, { record: null, path: [1] });
    expect(next.fields![1].fields!.at(-1)).toEqual(expect.objectContaining({ bit: 71 }));
  });
});

describe('guessLayout', () => {
  it('splits a single line where digits, letters and padding change', () => {
    const layout = guessLayout('PK0001112223344JOHN    ');
    expect(layout.fields).toEqual([
      { name: 'field1', length: 2, type: 'alpha' },
      { name: 'field2', length: 13, type: 'numeric' },
      { name: 'field3', length: 8, type: 'alpha' },
    ]);
    expect(measure(layout, 'PK0001112223344JOHN    ').problems).toEqual([]);
  });

  it('makes header, details and trailer records from a bank file', () => {
    const file = ['01BANKA20261118', '02ACC1 0000100', '02ACC2 0000250', '0300002'].join('\n');
    const layout = guessLayout(file);
    expect(layout.records!.map((r) => [r.name, r.when, !!r.many])).toEqual([
      ['header', { start: 0, equals: '01' }, false],
      ['details', { start: 0, equals: '02' }, true],
      ['trailer', { start: 0, equals: '03' }, false],
    ]);
    expect(layout.records![0].fields[0]).toEqual({ const: '01', length: 2 });
    // Every line of the sample reads cleanly with the guess.
    expect(measure(layout, file).problems).toEqual([]);
  });

  it('keeps a split most lines agree on', () => {
    const layout = guessLayout('AB12\nCD34\nEF56');
    expect(layout.records).toEqual([
      { name: 'rows', many: true, fields: [{ name: 'field1', length: 2, type: 'alpha' }, { name: 'field2', length: 2, type: 'numeric' }] },
    ]);
  });
});

describe('editing', () => {
  it('adds, moves, changes and removes fields without touching the original', () => {
    let layout: Layout = { fields: [{ name: 'a', length: 2 }] };
    const original = JSON.stringify(layout);
    layout = addField(layout, null);
    expect(layout.fields!.map((f) => f.name)).toEqual(['a', 'field1']);
    layout = moveField(layout, { record: null, path: [1] }, -1);
    expect(layout.fields!.map((f) => f.name)).toEqual(['field1', 'a']);
    layout = setField(layout, { record: null, path: [1] }, changeSize(layout.fields![1], 'prefix'));
    expect(layout.fields![1]).toEqual({ name: 'a', prefix: 2 });
    layout = setField(layout, { record: null, path: [0] }, changeKind(layout.fields![0], 'group'));
    layout = addField(layout, null, undefined, { record: null, path: [0] });
    expect(layout.fields![0].fields).toHaveLength(2);
    layout = removeField(layout, { record: null, path: [0, 0] });
    expect(layout.fields![0].fields).toHaveLength(1);
    expect(JSON.stringify({ fields: [{ name: 'a', length: 2 }] })).toBe(original);
  });

  it('keeps an emptied constant a constant while it is typed', () => {
    const layout = setField({ fields: [{ const: 'H', length: 1 }] }, { record: null, path: [0] }, { const: '', length: 1 });
    expect(layout.fields![0]).toEqual({ const: '', length: 1 });
  });

  it('switches between one record and line records', () => {
    const records = switchMode(pkLayout, true);
    expect(records.records![0]).toEqual({ name: 'rows', many: true, fields: pkLayout.fields });
    expect(switchMode(records, false)).toEqual(pkLayout);
    expect(summarise(pkLayout)).toBe('4 fields');
    expect(summarise(records)).toBe('1 record, 4 fields');
  });
});
