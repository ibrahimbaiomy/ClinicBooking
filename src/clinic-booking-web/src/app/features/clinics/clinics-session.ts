import { Injectable } from '@angular/core';
import { FeatureSession } from '../../shared/feature/feature-session';

/** The Clinics screens' memory: last list query and a one-time status message (D53, D56, D62). */
@Injectable({ providedIn: 'root' })
export class ClinicsSession extends FeatureSession {}
