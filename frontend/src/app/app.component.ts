import { Component, inject, signal } from '@angular/core';
import { HelloService } from './core/hello.service';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  private readonly helloService = inject(HelloService);

  protected readonly message = signal('Connecting to API...');
  protected readonly connected = signal(false);

  constructor() {
    this.helloService.getMessage().subscribe({
      next: ({ message }) => {
        this.message.set(message);
        this.connected.set(true);
      },
      error: () => this.message.set('API is not available. Run ./run.ps1 to start development.'),
    });
  }
}
