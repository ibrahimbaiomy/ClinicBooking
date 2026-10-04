/**
 * Display form of a stored phone number (D56). The stored value is E.164 (`+201012345678`, D55) and is
 * never changed: this function only builds text for the screen. It never throws.
 *
 * The local Egyptian form is used only when the whole value matches an Egyptian shape. After `+20`:
 *
 *  - **mobile**: `1[0125]` plus 8 digits (10 in all)        `+201012345678`  ->  `010 1234 5678`
 *  - **landline, 9 digits starting 2 or 3** (Cairo/Giza 02, and 03 with 8 subscriber digits):
 *    2-digit area code, then 4 and 4                         `+20223456789`   ->  `02 2345 6789`
 *  - **landline, 8 digits starting 2-9** (Alexandria 03 and the like): 2-digit area code, then 3 and 4
 *                                                            `+2031234567`    ->  `03 123 4567`
 *  - **landline, 9 digits starting 4-9**: 3-digit area code, then 3 and 4
 *                                                            `+20403123456`   ->  `040 312 3456`
 *
 * The area-code length is not in the stored value, so the landline grouping is a heuristic; a wrong
 * grouping is cosmetic and never alters data. Anything else is returned exactly as stored: a foreign
 * number (`+14155552671`), a `+20` number of another shape, or text that is not E.164 at all.
 * `null`, `undefined`, an empty string and non-strings give `''`.
 */
export function formatPhone(value: unknown): string {
  if (typeof value !== 'string' || value === '') {
    return '';
  }

  const mobile = /^\+20(1[0125]\d{8})$/.exec(value);
  if (mobile !== null) {
    const national = '0' + mobile[1];
    return `${national.slice(0, 3)} ${national.slice(3, 7)} ${national.slice(7)}`;
  }

  const landline = /^\+20([2-9]\d{7,8})$/.exec(value);
  if (landline !== null) {
    const remainder = landline[1];
    const national = '0' + remainder;

    if (remainder.length === 9) {
      return remainder[0] === '2' || remainder[0] === '3'
        ? `${national.slice(0, 2)} ${national.slice(2, 6)} ${national.slice(6)}`
        : `${national.slice(0, 3)} ${national.slice(3, 6)} ${national.slice(6)}`;
    }

    return `${national.slice(0, 2)} ${national.slice(2, 5)} ${national.slice(5)}`;
  }

  return value;
}
