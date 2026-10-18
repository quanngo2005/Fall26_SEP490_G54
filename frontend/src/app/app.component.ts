import { Component, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { IdleTimeoutService } from './core/idle-timeout.service';
import { LockScreenComponent } from './shared/lock-screen/lock-screen.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, LockScreenComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  private readonly idleService = inject(IdleTimeoutService);

  ngOnInit(): void {
    this.idleService.init();
  }
}
