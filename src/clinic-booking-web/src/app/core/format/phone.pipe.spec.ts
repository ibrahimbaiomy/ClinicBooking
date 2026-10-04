import { PhonePipe } from './phone.pipe';

describe('PhonePipe', () => {
  const pipe = new PhonePipe();

  it.each([
    ['+201012345678', '010 1234 5678'],
    ['+20223456789', '02 2345 6789'],
    ['+2031234567', '03 123 4567'],
    ['+14155552671', '+14155552671'],
    [null, ''],
    [undefined, ''],
    ['', ''],
  ])('transforms %j into %j', (value, expected) => {
    expect(pipe.transform(value)).toBe(expected);
  });
});
