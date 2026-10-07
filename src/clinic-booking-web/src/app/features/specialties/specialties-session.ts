import { Injectable } from '@angular/core';
import { FeatureSession } from '../../shared/feature/feature-session';

/** The Specialties screens' memory: last list query and a one-time status message (D53, D62). */
@Injectable({ providedIn: 'root' })
export class SpecialtiesSession extends FeatureSession {}
