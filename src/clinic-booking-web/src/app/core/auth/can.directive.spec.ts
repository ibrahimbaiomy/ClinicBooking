import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { CanDirective } from './can.directive';

@Component({
  selector: 'cb-can-host',
  imports: [CanDirective],
  template: '<button *cbCan="\'specialties.manage\'">x</button>',
})
class Host {}

describe('CanDirective', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: provideAuthTesting() }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const hasButton = (element: HTMLElement) => element.querySelector('button') !== null;

  it('hides the element when the user lacks the permission and shows it when they have it', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(hasButton(fixture.nativeElement)).toBe(false);

    await signIn(['specialties.manage']);
    await fixture.whenStable();
    expect(hasButton(fixture.nativeElement)).toBe(true);
  });

  it('keeps the element hidden for a user with other permissions only', async () => {
    await signIn(['users.manage']);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    expect(hasButton(fixture.nativeElement)).toBe(false);
  });
});
