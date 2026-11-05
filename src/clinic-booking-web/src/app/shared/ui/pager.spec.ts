import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import ar from '../../../../public/i18n/ar.json';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { Pager } from './pager';

describe('Pager (D64)', () => {
  async function render(page = 2, totalPages = 3, pageSize = 10) {
    TestBed.configureTestingModule({ providers: [provideTestTransloco()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('ar'));
    const fixture = TestBed.createComponent(Pager);
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('totalPages', totalPages);
    fixture.componentRef.setInput('pageSize', pageSize);
    fixture.componentRef.setInput('sizeId', 'things-size');
    await fixture.whenStable();
    return fixture;
  }

  it('puts page navigation and the page size in one row, navigation first', async () => {
    const fixture = await render();
    const host = fixture.nativeElement as HTMLElement;
    const row = host.firstElementChild as HTMLElement;

    expect(row.className).toContain('justify-between');
    expect(row.children[0].tagName).toBe('NAV');
    expect(row.children[1].querySelector('select')?.id).toBe('things-size');
    expect(host.querySelector('label[for=things-size]')?.textContent?.trim()).toBe(ar.pager.page_size);
    expect([...host.querySelectorAll('option')].map((o) => o.value)).toEqual(['10', '20', '50']);
    expect(host.querySelector<HTMLSelectElement>('select')!.value).toBe('10');
  });

  it('emits the chosen page size and the page moves', async () => {
    const fixture = await render();
    const host = fixture.nativeElement as HTMLElement;
    const sizes: number[] = [];
    const pages: number[] = [];
    fixture.componentInstance.pageSizeChange.subscribe((size) => sizes.push(size));
    fixture.componentInstance.pageChange.subscribe((page) => pages.push(page));

    const select = host.querySelector<HTMLSelectElement>('select')!;
    select.value = '50';
    select.dispatchEvent(new Event('change'));
    const [previous, next] = [...host.querySelectorAll<HTMLButtonElement>('nav button')];
    previous.click();
    next.click();

    expect(sizes).toEqual([50]);
    expect(pages).toEqual([1, 3]);
  });
});
