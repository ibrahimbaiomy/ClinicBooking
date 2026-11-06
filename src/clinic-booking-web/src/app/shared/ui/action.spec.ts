import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Action, ACTION_STYLES, ActionKind } from './action';

@Component({
  selector: 'cb-action-host',
  imports: [Action],
  template: `
    <button type="button" class="ms-2" [cbAction]="kind()" [disabled]="disabled()">Label</button>
    <a href="/x" cbAction="edit">Link label</a>
  `,
})
class Host {
  readonly kind = signal<ActionKind>('add');
  readonly disabled = signal(false);
}

@Component({
  selector: 'cb-danger-host',
  imports: [Action],
  template: `
    <button type="button" cbAction="danger" cbActionIcon="key">Reset</button>
    <button type="button" cbAction="danger">Plain</button>
  `,
})
class DangerHost {}

describe('Action buttons (D65)', () => {
  function render(kind: ActionKind) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.kind.set(kind);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    return { fixture, button: host.querySelector('button')!, link: host.querySelector('a')! };
  }

  const expected: Record<ActionKind, { colour: string; icon: string | null }> = {
    add: { colour: 'green-700', icon: 'plus' },
    edit: { colour: 'blue-700', icon: 'pencil' },
    'save-new': { colour: 'green-700', icon: 'check' },
    'save-existing': { colour: 'blue-700', icon: 'check' },
    delete: { colour: 'red-700', icon: 'trash' },
    danger: { colour: 'red-700', icon: null },
    cancel: { colour: 'slate-600', icon: 'x' },
  };

  for (const [kind, { colour, icon }] of Object.entries(expected) as [ActionKind, { colour: string; icon: string | null }][]) {
    it(`${kind}: ${colour} outline (transparent, filled on hover), focus ring, disabled state, ${icon ?? 'no'} icon`, () => {
      const { button } = render(kind);
      const classes = button.className.split(/\s+/);

      expect(classes).toEqual(expect.arrayContaining([
        'bg-transparent',
        'border',
        `border-${colour}`,
        `text-${colour}`,
        `hover:bg-${colour}`,
        'hover:text-white',
        'focus-visible:outline-2',
        `focus-visible:outline-${colour}`,
        'disabled:opacity-50',
      ]));
      expect(button.getAttribute('data-action')).toBe(kind);
      expect(button.querySelector('cb-icon')?.getAttribute('data-icon') ?? null).toBe(icon);
      // The text stays the accessible name; the icon is hidden from assistive technology.
      expect(button.textContent?.trim()).toBe('Label');
      if (icon !== null) expect(button.querySelector('cb-icon')!.getAttribute('aria-hidden')).toBe('true');
    });
  }

  it('renders a real icon inside the button: an <svg> with at least one <path> (never an empty svg)', () => {
    for (const kind of ['add', 'edit', 'save-new', 'save-existing', 'delete', 'cancel'] as const) {
      const { button } = render(kind);
      const svg = button.querySelector('cb-icon > svg');

      expect(svg, kind).not.toBeNull();
      expect(svg!.querySelectorAll('path').length, kind).toBeGreaterThan(0);
      expect(svg!.querySelector('path')!.getAttribute('d')?.length ?? 0, kind).toBeGreaterThan(10);
      TestBed.resetTestingModule();
    }
  });

  it('danger takes its own icon through cbActionIcon, and has none without it', () => {
    const fixture = TestBed.createComponent(DangerHost);
    fixture.detectChanges();
    const [withIcon, without] = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')];

    expect(withIcon.className).toContain('text-red-700');
    expect(withIcon.querySelector('cb-icon')!.getAttribute('data-icon')).toBe('key');
    expect(withIcon.querySelectorAll('cb-icon > svg path').length).toBeGreaterThan(0);
    expect(without.querySelector('cb-icon')).toBeNull();
  });

  it('Save is green on a create form and blue on an edit form', () => {
    expect(ACTION_STYLES['save-new'].classes).toContain('text-green-700');
    expect(ACTION_STYLES['save-existing'].classes).toContain('text-blue-700');

    const { fixture, button } = render('save-new');
    expect(button.className).toContain('text-green-700');
    fixture.componentInstance.kind.set('save-existing');
    fixture.detectChanges();
    expect(button.className).toContain('text-blue-700');
    expect(button.className).not.toContain('text-green-700');
  });

  it('keeps the page\'s own layout classes, and works on links', () => {
    const { button, link } = render('add');

    expect(button.className).toContain('ms-2');
    expect(link.className).toContain('text-blue-700');
    expect(link.querySelector('cb-icon')!.getAttribute('data-icon')).toBe('pencil');
  });

  it('the icon comes before the text', () => {
    const { button } = render('delete');

    expect(button.firstElementChild?.tagName).toBe('CB-ICON');
  });
});
