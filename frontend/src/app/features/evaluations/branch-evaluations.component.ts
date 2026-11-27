import { Component } from '@angular/core';
import { EvaluationsComponent } from './evaluations.component';

@Component({
  selector: 'app-branch-evaluations',
  standalone: true,
  imports: [EvaluationsComponent],
  template: `<app-evaluations [isBranchMode]="true" />`,
  styles: [
    `
      :host {
        display: block;
        width: 100%;
        height: 100%;
      }
    `,
  ],
})
export class BranchEvaluationsComponent {}
