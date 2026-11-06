import { TestBed } from '@angular/core/testing';
import { Icon } from './icon';
import { ICON_REGISTRY, ICONS } from './icons';

describe('Icon (D65)', () => {
  function render(name: string, registry?: Record<string, { svg: string; directional: boolean }>): HTMLElement {
    TestBed.configureTestingModule({
      providers: registry === undefined ? [] : [{ provide: ICON_REGISTRY, useValue: registry }],
    });
    const fixture = TestBed.createComponent(Icon);
    fixture.componentRef.setInput('name', name);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('the .svg text loader works under Vitest too: the registry holds real Bootstrap markup', () => {
    for (const icon of Object.values(ICONS)) {
      expect(icon.svg.startsWith('<svg')).toBe(true);
      expect(icon.svg).toContain('viewBox="0 0 16 16"');
    }
  });

  it('renders the icon as decorative SVG: aria-hidden on the host and the svg, never focusable', () => {
    const host = render('trash');

    expect(host.getAttribute('aria-hidden')).toBe('true');
    const svg = host.querySelector('svg')!;
    expect(svg).not.toBeNull();
    expect(svg.getAttribute('aria-hidden')).toBe('true');
    expect(svg.getAttribute('focusable')).toBe('false');
    expect(svg.querySelectorAll('path').length).toBeGreaterThan(0);
    expect(host.getAttribute('data-icon')).toBe('trash');
  });

  it('plus, pencil, trash, check, x and the toast check are not mirrored in RTL', () => {
    for (const name of Object.keys(ICONS)) {
      TestBed.resetTestingModule();
      expect(render(name).className).not.toContain('rtl:-scale-x-100');
    }
  });

  it('a directional icon is mirrored in RTL with rtl:-scale-x-100 (D25)', () => {
    const host = render('arrow', { arrow: { svg: '<svg viewBox="0 0 16 16"><path d="M0 0"/></svg>', directional: true } });

    expect(host.className).toContain('rtl:-scale-x-100');
  });
});
