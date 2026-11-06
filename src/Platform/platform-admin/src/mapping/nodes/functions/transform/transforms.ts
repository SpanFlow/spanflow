// Single source of truth for the transform functions shown in the designer.
// The engine side lives in UFMapping.Engine (TransformRegistry); the conversion
// functions all go through its ValueConverter.

export interface TransformInfo {
  value: string;
  label: string;
  description: string;
  example: string;
  /** Dropdown section. */
  group: string;
  /** Argument hints, in order; shown as placeholders. Picking the function adds
   *  a blank slot for each (a blank argument means "not given"). */
  params?: string[];
}

export const TRANSFORM_GROUPS = ['Text', 'Number', 'Convert', 'Check', 'Date & epoch', 'UUID', 'Hash & encode', 'Other'];

export const TRANSFORMS: TransformInfo[] = [
  { value: 'concat', label: 'CONCAT', group: 'Text', description: 'Joins text values together.', example: "'Hi ', name → 'Hi Bob'" },
  { value: 'upper', label: 'UPPER', group: 'Text', description: 'Uppercases the first argument.', example: "'bob' → 'BOB'" },
  { value: 'lower', label: 'LOWER', group: 'Text', description: 'Lowercases the first argument.', example: "'BOB' → 'bob'" },
  { value: 'trim', label: 'TRIM', group: 'Text', description: 'Removes surrounding whitespace.', example: "' hi ' → 'hi'" },
  { value: 'replace', label: 'REPLACE', group: 'Text', description: 'replace(text, find, with).', example: "'a-b', '-', '+' → 'a+b'" },
  { value: 'substring', label: 'SUBSTRING', group: 'Text', description: 'substring(text, start, [length]).', example: "'hello', 1, 3 → 'ell'" },
  { value: 'length', label: 'LENGTH', group: 'Text', description: 'Number of characters.', example: "'hello' → 5" },
  { value: 'add', label: 'ADD', group: 'Number', description: 'Sums the numeric arguments.', example: '2, 3 → 5' },
  { value: 'subtract', label: 'SUBTRACT', group: 'Number', description: 'First minus the rest.', example: '10, 3 → 7' },
  { value: 'multiply', label: 'MULTIPLY', group: 'Number', description: 'Multiplies the numeric arguments.', example: '2, 3 → 6' },
  { value: 'divide', label: 'DIVIDE', group: 'Number', description: 'First divided by the rest (÷0 = 0).', example: '12, 3 → 4' },
  { value: 'mod', label: 'MOD', group: 'Number', description: 'Remainder of arg1 ÷ arg2.', example: '7, 3 → 1' },
  { value: 'round', label: 'ROUND', group: 'Number', description: 'round(number, [decimals]).', example: '3.146, 2 → 3.15' },
  { value: 'floor', label: 'FLOOR', group: 'Number', description: 'Rounds down to a whole number.', example: '3.9 → 3' },
  { value: 'ceil', label: 'CEIL', group: 'Number', description: 'Rounds up to a whole number.', example: '3.1 → 4' },
  { value: 'abs', label: 'ABS', group: 'Number', description: 'Absolute value.', example: '-5 → 5' },

  // ---- Convert: null when the value can't be converted (wrap in COALESCE for a fallback)
  {
    value: 'convert', label: 'CONVERT', group: 'Convert',
    description: 'Any type to any other: string, int, long, short, byte, decimal, double, float, bool, datetime, date, time, epoch, epochMs, epochUs, epochNs, ticks, uuid. Null if it cannot convert.',
    example: "'12', 'int' → 12",
    params: ['value', 'type (int, decimal, datetime, uuid…)', 'format (optional)', 'input format (optional)'],
  },
  { value: 'toString', label: 'TO STRING', group: 'Convert', description: 'Any value as text; numbers take a format (N2, #0.00, X).', example: "1234.5, 'N2' → '1,234.50'", params: ['value', 'number format (N2, #0.00, X)'] },
  { value: 'toInt', label: 'TO INT', group: 'Convert', description: 'Whole number (32-bit). Fractions are truncated unless you pick a rounding.', example: "12.5, 'round' → 13", params: ['value', 'rounding (truncate, round, floor, ceil)'] },
  { value: 'toLong', label: 'TO LONG', group: 'Convert', description: 'Whole number (64-bit).', example: "'3000000000' → 3000000000", params: ['value', 'rounding (truncate, round, floor, ceil)'] },
  { value: 'toDecimal', label: 'TO DECIMAL', group: 'Convert', description: 'Exact decimal number (money-safe). Reads text and 0x hex.', example: "'19.99' → 19.99", params: ['value'] },
  { value: 'toDouble', label: 'TO DOUBLE', group: 'Convert', description: 'Floating-point number (64-bit).', example: "'1.5' → 1.5", params: ['value'] },
  { value: 'toFloat', label: 'TO FLOAT', group: 'Convert', description: 'Floating-point number (32-bit).', example: "'1.25' → 1.25", params: ['value'] },
  { value: 'toBool', label: 'TO BOOL', group: 'Convert', description: 'true/false, yes/no, y/n, on/off, 1/0 → boolean.', example: "'yes' → true", params: ['value'] },
  { value: 'toDateTime', label: 'TO DATETIME', group: 'Convert', description: 'Date text or epoch number → ISO 8601 date-time.', example: "'2024-01-15 10:30' → '2024-01-15T10:30:00Z'", params: ['value', 'format (iso)', 'input format (e.g. dd/MM/yyyy)'] },
  { value: 'toDate', label: 'TO DATE', group: 'Convert', description: 'Date text or epoch number → yyyy-MM-dd.', example: "'2024-01-15T10:30:00Z' → '2024-01-15'", params: ['value', 'format (yyyy-MM-dd)', 'input format'] },
  { value: 'toTime', label: 'TO TIME', group: 'Convert', description: 'Date text or epoch number → HH:mm:ss.', example: "'2024-01-15T10:30:00Z' → '10:30:00'", params: ['value', 'format (HH:mm:ss)', 'input format'] },
  { value: 'toUuid', label: 'TO UUID', group: 'Convert', description: 'Normalise a UUID, or write it as n, b, p, urn, base64, upper.', example: "'919108F7…48A8' → '919108f7-52d1-…'", params: ['value', 'layout (d, n, b, urn, base64, upper)', 'input format (base64)'] },

  // ---- Check
  { value: 'isType', label: 'IS TYPE', group: 'Check', description: "Strict check: is the value (or its text) exactly this type? '12' is an int, '12.5' is not.", example: "'12', 'int' → true", params: ['value', 'type (int, decimal, bool, datetime, uuid, string, null, object, array…)'] },
  { value: 'typeOf', label: 'TYPE OF', group: 'Check', description: 'JSON kind: null, string, boolean, integer, number, object, array.', example: "5.5 → 'number'", params: ['value'] },
  { value: 'detectType', label: 'DETECT TYPE', group: 'Check', description: 'Like TYPE OF, but looks inside text: uuid, integer, number, boolean, datetime or string.', example: "'2024-01-15' → 'datetime'", params: ['value'] },
  { value: 'isUuid', label: 'IS UUID', group: 'Check', description: 'Is it a UUID: optionally of a version (1 to 8, nil, max)?', example: "id, 7 → true", params: ['value', 'version (1-8, nil, max; blank = any)'] },

  // ---- Date & epoch
  { value: 'formatDate', label: 'FORMAT DATE', group: 'Date & epoch', description: 'Read a date (text or epoch) and write it in any format, optionally in a time zone.', example: "'15/01/2024', 'iso', 'dd/MM/yyyy' → '2024-01-15T00:00:00Z'", params: ['date', 'format (iso, date, dd/MM/yyyy HH:mm…)', 'input format (a|b for several)', 'time zone (utc, Asia/Karachi, +05:00)'] },
  { value: 'toEpoch', label: 'TO EPOCH', group: 'Date & epoch', description: 'Date → epoch number in s (default), ms, us, ns or .NET ticks.', example: "'2024-01-15T10:30:00Z', 'ms' → 1705314600000", params: ['date', 'unit (s, ms, us, ns, ticks)', 'input format'] },
  { value: 'fromEpoch', label: 'FROM EPOCH', group: 'Date & epoch', description: 'Epoch number → date text. Unit auto-detected unless given.', example: "1705314600, 's', 'date' → '2024-01-15'", params: ['epoch', 'unit (auto, s, ms, us, ns, ticks)', 'format (iso)', 'time zone'] },
  { value: 'convertEpoch', label: 'CONVERT EPOCH', group: 'Date & epoch', description: 'Epoch in one unit → another (s, ms, us, ns, ticks).', example: "1705314600, 's', 'ms' → 1705314600000", params: ['epoch', 'from unit (s, ms, us, ns, ticks, auto)', 'to unit (s, ms, us, ns, ticks)'] },

  // ---- UUID
  { value: 'uuidVersion', label: 'UUID VERSION', group: 'UUID', description: 'Version 1 to 8 (0 nil, 15 max); null if not a UUID.', example: "'017f22e2-79b0-7cc3-…' → 7", params: ['uuid'] },
  { value: 'newUuid', label: 'NEW UUID', group: 'UUID', description: 'Make a UUID: v4 by default, or 1, 3, 5, 6, 7. v3/v5 are name-based (same input → same id).', example: "5, 'dns', 'example.com' → stable id", params: ['version (1, 3, 4, 5, 6, 7)', 'namespace for v3/v5 (dns, url, oid, x500)', 'name for v3/v5', 'layout (d, n, upper…)'] },
  { value: 'uuidTimestamp', label: 'UUID TIMESTAMP', group: 'UUID', description: 'When a v1, v6 or v7 UUID was made; null for other versions.', example: "'017f22e2-79b0-7cc3-…' → '2022-02-22T19:22:22Z'", params: ['uuid', 'format (iso)', 'time zone'] },

  // ---- Hash & encode (E48-T10): keyless and deterministic. Keyed hashing (a pepper), encryption, random values
  // and passwords live in pipeline nodes — a mapping has nowhere safe to keep a key. Null in gives null out.
  { value: 'sha256', label: 'SHA-256', group: 'Hash & encode', description: 'SHA-256 of the value, hex by default. Same input, same hash: a lookup or dedupe key. Not for passwords.', example: "'abc' → 'ba7816bf…'", params: ['value', 'encoding (hex, base64, base64url)'] },
  { value: 'sha384', label: 'SHA-384', group: 'Hash & encode', description: 'SHA-384 of the value, hex by default.', example: "'abc' → 'cb00753f…'", params: ['value', 'encoding (hex, base64, base64url)'] },
  { value: 'sha512', label: 'SHA-512', group: 'Hash & encode', description: 'SHA-512 of the value, hex by default.', example: "'abc' → 'ddaf35a1…'", params: ['value', 'encoding (hex, base64, base64url)'] },
  { value: 'md5Legacy', label: 'MD5 (LEGACY)', group: 'Hash & encode', description: 'MD5 is broken for security: only for matching a checksum a partner already publishes.', example: "'abc' → '90015098…'", params: ['value', 'encoding (hex, base64, base64url)'] },
  { value: 'base64Encode', label: 'BASE64 ENCODE', group: 'Hash & encode', description: 'Text as base64 (UTF-8). Encoding is not security.', example: "'hi' → 'aGk='", params: ['value'] },
  { value: 'base64Decode', label: 'BASE64 DECODE', group: 'Hash & encode', description: 'base64 back to text; null if it is not base64 or not UTF-8 text.', example: "'aGk=' → 'hi'", params: ['value'] },
  { value: 'base64UrlEncode', label: 'BASE64URL ENCODE', group: 'Hash & encode', description: 'URL-safe base64 (- and _, no padding).', example: "'hi?' → 'aGk_'", params: ['value'] },
  { value: 'base64UrlDecode', label: 'BASE64URL DECODE', group: 'Hash & encode', description: 'URL-safe base64 back to text; null if invalid.', example: "'aGk_' → 'hi?'", params: ['value'] },
  { value: 'hexEncode', label: 'HEX ENCODE', group: 'Hash & encode', description: 'Text as lower-case hex (UTF-8 bytes).', example: "'hi' → '6869'", params: ['value'] },
  { value: 'hexDecode', label: 'HEX DECODE', group: 'Hash & encode', description: 'Hex back to text; null if invalid.', example: "'6869' → 'hi'", params: ['value'] },
  { value: 'urlEncode', label: 'URL ENCODE', group: 'Hash & encode', description: 'Escape text for a URL query or path segment.', example: "'a b&c' → 'a%20b%26c'", params: ['value'] },
  { value: 'urlDecode', label: 'URL DECODE', group: 'Hash & encode', description: 'Un-escape URL-encoded text.', example: "'a%20b' → 'a b'", params: ['value'] },

  { value: 'coalesce', label: 'COALESCE', group: 'Other', description: 'First non-null argument.', example: 'null, 5 → 5' },
];

export const transformInfo = (value: string): TransformInfo | undefined =>
  TRANSFORMS.find((t) => t.value === value);
