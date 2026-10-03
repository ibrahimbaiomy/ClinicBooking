import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each(['/', '/specialties', '/specialties/5?tab=a#top', '/a/b/c', '/search?q=%D8%B9'])(
    'keeps the in-app path %j',
    (url) => {
      expect(safeReturnUrl(url)).toBe(url);
    },
  );

  it.each([
    ['an absolute URL', 'https://evil.com'],
    ['a protocol-relative URL', '//evil.com'],
    ['a backslash after the slash', '/\\evil.com'],
    ['a UNC-style path', '\\\\evil.com'],
    ['a javascript: URL', 'javascript:alert(1)'],
    ['a data: URL', 'data:text/html,x'],
    ['a leading space', ' //evil.com'],
    ['an encoded double slash', '/%2F%2Fevil.com'],
    ['an encoded backslash', '/%5Cevil.com'],
    ['a tab after the slash', '/\t/evil.com'],
    ['a newline', '/a\nb'],
    ['a relative path without a slash', 'specialties'],
    ['the login page itself', '/login'],
    ['the login page with a query', '/login?returnUrl=%2F'],
    ['an invalid encoding', '/%E0%A4%A'],
    ['an empty string', ''],
  ])('replaces %s with /', (_name, url) => {
    expect(safeReturnUrl(url)).toBe('/');
  });

  it.each([null, undefined, 5, {}, ['/a']])('replaces a non-string (%j) with /', (value) => {
    expect(safeReturnUrl(value)).toBe('/');
  });

  it('replaces a very long value with /', () => {
    expect(safeReturnUrl('/' + 'a'.repeat(3000))).toBe('/');
  });
});
