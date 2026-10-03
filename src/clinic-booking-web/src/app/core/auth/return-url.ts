const MAX_LENGTH = 2048;
const CONTROL_CHARACTERS = /[\u0000-\u001f\u007f]/;
const LOGIN_PATH = /^\/login(?:[/?#]|$)/;

/**
 * The only way a returnUrl may be used (D52): same-origin, in-app and relative, otherwise "/".
 * It is checked as given and once more after decoding, so `/%2F%2Fevil.com` cannot become
 * `//evil.com`. The result is only ever passed to the router, never to window.location.
 */
export function safeReturnUrl(value: unknown): string {
  if (typeof value !== 'string' || value.length === 0 || value.length > MAX_LENGTH) {
    return '/';
  }

  let candidate = value;
  for (let pass = 0; pass < 2; pass++) {
    if (!isSafeRelative(candidate)) {
      return '/';
    }

    try {
      candidate = decodeURIComponent(candidate);
    } catch {
      return '/';
    }
  }

  return value;
}

function isSafeRelative(url: string): boolean {
  return (
    url.startsWith('/') &&
    !url.startsWith('//') &&
    !url.includes('\\') &&
    !CONTROL_CHARACTERS.test(url) &&
    !LOGIN_PATH.test(url)
  );
}
