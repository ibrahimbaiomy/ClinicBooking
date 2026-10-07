import { Injectable } from '@angular/core';
import { FeatureSession } from '../../shared/feature/feature-session';

/** The Users screens' memory: last list query and a one-time status message (D59, D62). */
@Injectable({ providedIn: 'root' })
export class UsersSession extends FeatureSession {}
