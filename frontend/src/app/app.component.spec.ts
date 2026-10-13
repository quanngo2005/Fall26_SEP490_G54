import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  it('renders the routed page outlet', () => {
    const fixture = TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([])],
    }).createComponent(AppComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
  });
});
