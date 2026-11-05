import { TestBed } from '@angular/core/testing';
import { ToastService } from '../app/core/ui/toast.service';

/** The keys of the toasts currently shown (D64), oldest first. */
export const toastKeys = (): string[] =>
  TestBed.inject(ToastService)
    .toasts()
    .map((toast) => toast.key);
