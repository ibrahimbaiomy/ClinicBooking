import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { clinicGrant } from '../../../testing/users-testing';
import { CanDirective } from './can.directive';

@Component({
  selector: 'cb-can-clinic-host',
  imports: [CanDirective],
  template: `
    <button id="five" *cbCan="'doctors.manage'; clinic: 5">in 5</button>
    <button id="six" *cbCan="'doctors.manage'; clinic: '6'">in 6</button>
    <button id="global" *cbCan="'doctors.manage'">global</button>
  `,
})
class Host {}

describe('CanDirective with a clinic (D59)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: provideAuthTesting() }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const ids = (element: HTMLElement) => [...element.querySelectorAll('button')].map((button) => button.id);

  it('shows the element only in the clinic that holds the permission (number or string ids)', async () => {
    await signIn([], 'token-1', {
      clinicPermissions: [clinicGrant(5, 'أ', 'A', ['doctors.manage']), clinicGrant('6', 'ب', 'B', ['doctors.other'])],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    expect(ids(fixture.nativeElement)).toEqual(['five']);
  });

  it('without a clinic it asks the global permission, which a clinic grant does not satisfy', async () => {
    await signIn([], 'token-1', { clinicPermissions: [clinicGrant(5, 'أ', 'A', ['doctors.manage'])] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    expect(ids(fixture.nativeElement)).not.toContain('global');
  });

  it('a global permission of the same name does not open the clinic variant', async () => {
    await signIn(['doctors.manage']);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    expect(ids(fixture.nativeElement)).toEqual(['global']);
  });

  it('follows the session when the grants change', async () => {
    await signIn([]);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(ids(fixture.nativeElement)).toEqual([]);

    await signIn([], 'token-2', { clinicPermissions: [clinicGrant(6, 'ب', 'B', ['doctors.manage'])] });
    await fixture.whenStable();

    expect(ids(fixture.nativeElement)).toEqual(['six']);
  });
});
