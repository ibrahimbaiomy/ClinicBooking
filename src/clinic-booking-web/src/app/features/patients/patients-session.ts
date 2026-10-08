import { Injectable } from '@angular/core';
import { FeatureSession } from '../../shared/feature/feature-session';

/** The Patients screens' memory: last list query and a one-time status message (D63). */
@Injectable({ providedIn: 'root' })
export class PatientsSession extends FeatureSession {}
