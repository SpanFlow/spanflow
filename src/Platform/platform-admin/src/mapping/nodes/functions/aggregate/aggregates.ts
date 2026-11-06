// Single source of truth for the aggregate functions shown in the designer:
// the dropdown options and the inline description/example for each.

export interface AggregateInfo {
  value: string;
  label: string;
  description: string;
  example: string;
}

export const AGGREGATES: AggregateInfo[] = [
  { value: 'sum', label: 'SUM', description: 'Adds all values.', example: '[100, 50] → 150' },
  { value: 'count', label: 'COUNT', description: 'Counts the items.', example: '[a, b, c] → 3' },
  { value: 'avg', label: 'AVG', description: 'Average (mean) of values.', example: '[2, 4] → 3' },
  { value: 'min', label: 'MIN', description: 'Smallest value.', example: '[3, 1, 2] → 1' },
  { value: 'max', label: 'MAX', description: 'Largest value.', example: '[3, 1, 2] → 3' },
  { value: 'first', label: 'FIRST', description: 'First value in the list.', example: '[a, b] → a' },
  { value: 'last', label: 'LAST', description: 'Last value in the list.', example: '[a, b] → b' },
  { value: 'distinct', label: 'DISTINCT', description: 'Unique values, as a list.', example: '[a, a, b] → [a, b]' },
  { value: 'product', label: 'PRODUCT', description: 'Multiplies all values.', example: '[2, 3, 4] → 24' },
  { value: 'countDistinct', label: 'COUNT DISTINCT', description: 'How many unique values.', example: '[a, a, b] → 2' },
  { value: 'median', label: 'MEDIAN', description: 'Middle value when sorted.', example: '[1, 2, 6] → 2' },
  { value: 'range', label: 'RANGE', description: 'Max minus min.', example: '[50, 150] → 100' },
  { value: 'mode', label: 'MODE', description: 'Most frequent value.', example: '[1, 2, 2] → 2' },
  { value: 'any', label: 'ANY', description: 'True if any value is truthy (not 0/false/empty).', example: '[false, true] → true' },
  { value: 'all', label: 'ALL', description: 'True if every value is truthy.', example: '[true, false] → false' },
  { value: 'none', label: 'NONE', description: 'True if no value is truthy.', example: '[false, false] → true' },
  { value: 'variance', label: 'VARIANCE', description: 'Population variance (spread).', example: '[2,4,4,4,5,5,7,9] → 4' },
  { value: 'stddev', label: 'STDDEV', description: 'Population standard deviation.', example: '[2,4,4,4,5,5,7,9] → 2' },
  { value: 'joinString', label: 'JOIN STRING', description: 'Joins values into one string with a separator.', example: '[a, b, c] sep "-" → "a-b-c"' },
];

export const aggregateInfo = (value: string): AggregateInfo | undefined =>
  AGGREGATES.find((a) => a.value === value);
