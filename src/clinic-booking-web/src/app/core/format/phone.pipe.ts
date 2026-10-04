import { Pipe, PipeTransform } from '@angular/core';
import { formatPhone } from './phone';

/**
 * `{{ clinic.phone | phone }}`: the display form of a stored E.164 number (D56). Pure, because the
 * result does not depend on the language. Render it inside an element with `dir="ltr"` so the digits
 * keep their order in the Arabic UI.
 */
@Pipe({ name: 'phone' })
export class PhonePipe implements PipeTransform {
  transform(value: unknown): string {
    return formatPhone(value);
  }
}
