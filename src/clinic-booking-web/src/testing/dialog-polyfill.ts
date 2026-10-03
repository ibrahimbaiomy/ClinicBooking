/**
 * jsdom has `<dialog>` but not `showModal()` / `close()`. This stand-in toggles `open` and fires
 * `close`, which is all the component relies on; focus trapping and Esc are the browser's job.
 */
export function installDialogPolyfill(): void {
  const prototype = HTMLDialogElement.prototype;

  prototype.showModal = function showModal(this: HTMLDialogElement): void {
    this.setAttribute('open', '');
  };

  prototype.close = function close(this: HTMLDialogElement): void {
    if (this.hasAttribute('open')) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    }
  };
}
