import { formatPhone } from './phone';

describe('formatPhone', () => {
  it.each([
    // The cases the owner pinned.
    ['Cairo/Giza landline (9 digits after +20, starting 2)', '+20223456789', '02 2345 6789'],
    ['Alexandria landline (8 digits after +20, starting 3)', '+2031234567', '03 123 4567'],
    ['Egyptian mobile', '+201012345678', '010 1234 5678'],
    ['a foreign number is shown unchanged', '+14155552671', '+14155552671'],
  ])('%s: %s -> %s', (_name, stored, expected) => {
    expect(formatPhone(stored)).toBe(expected);
  });

  it.each([
    ['+201012345678', '010 1234 5678'],
    ['+201112345678', '011 1234 5678'],
    ['+201212345678', '012 1234 5678'],
    ['+201512345678', '015 1234 5678'],
  ])('groups the mobile prefixes: %s', (stored, expected) => {
    expect(formatPhone(stored)).toBe(expected);
  });

  it.each([
    // 9 digits after +20, starting 2 or 3: a 2-digit area code and 8 digits (4 + 4)
    ['+20223456789', '02 2345 6789'],
    ['+20233334444', '02 3333 4444'],
    ['+20312345678', '03 1234 5678'],
    // 8 digits after +20 (starting 2-9): a 2-digit area code and 7 digits (3 + 4)
    ['+2031234567', '03 123 4567'],
    ['+2021234567', '02 123 4567'],
    ['+2091234567', '09 123 4567'],
    // 9 digits after +20, starting 4-9: a 3-digit area code and 7 digits (3 + 3 + 4)
    ['+20403123456', '040 312 3456'],
    ['+20453123456', '045 312 3456'],
    ['+20993123456', '099 312 3456'],
  ])('groups the landline %s as %s', (stored, expected) => {
    expect(formatPhone(stored)).toBe(expected);
  });

  it.each([
    ['+14155552671'],
    ['+442079460958'],
    ['+12345678'],
    ['+123456789012345'],
    ['+966501234567'],
  ])('shows the foreign number %s exactly as stored', (stored) => {
    expect(formatPhone(stored)).toBe(stored);
  });

  it.each([
    ['an 8-digit remainder starting with 1 is neither mobile nor landline', '+2012345678'],
    ['+20 with a leading 0', '+200123456789'],
    ['+20 with a 1[0125] prefix but the wrong length', '+20101234567'],
    ['+20 mobile prefix 013 (not a mobile prefix)', '+201312345678'],
    ['+20 landline with too many digits', '+202234567890'],
    ['+20 with only 6 digits', '+20123456'],
  ])('%s is returned exactly as stored', (_name, stored) => {
    expect(formatPhone(stored)).toBe(stored);
  });

  it.each([[null], [undefined], ['']])('gives an empty string for %j', (value) => {
    expect(formatPhone(value)).toBe('');
  });

  it.each([[42], [{}], [[]], [true], [Symbol('x')], [() => 1]])('gives an empty string for the non-string %s', (value) => {
    expect(() => formatPhone(value)).not.toThrow();
    expect(formatPhone(value)).toBe('');
  });

  it.each([
    ['01012345678'],
    ['010 1234 5678'],
    ['abc'],
    ['+'],
    ['  +201012345678'],
    ['+201012345678 '],
    ['٠١٠١٢٣٤٥٦٧٨'],
    ['+20١٠١٢٣٤٥٦٧٨'],
    ['<script>alert(1)</script>'],
    ['+2010123456789012345678901234567890'],
  ])('returns unexpected text %j unchanged and never throws', (value) => {
    expect(() => formatPhone(value)).not.toThrow();
    expect(formatPhone(value)).toBe(value);
  });

  it('is idempotent on its own output (a displayed number is returned as is)', () => {
    for (const stored of ['+201012345678', '+20223456789', '+2031234567', '+20403123456', '+14155552671']) {
      const shown = formatPhone(stored);

      expect(formatPhone(shown)).toBe(shown);
    }
  });

  it('never changes the stored value: it returns a new string and leaves its input alone', () => {
    const stored = '+201012345678';

    formatPhone(stored);

    expect(stored).toBe('+201012345678');
  });
});
