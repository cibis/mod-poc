import { Routes } from '@angular/router';
import { DeadLetterListComponent } from './dead-letter-list/dead-letter-list.component';
import { DeadLetterDetailComponent } from './dead-letter-detail/dead-letter-detail.component';

export default [
  { path: '', component: DeadLetterListComponent },
  { path: ':id', component: DeadLetterDetailComponent },
] satisfies Routes;
