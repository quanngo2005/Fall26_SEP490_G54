import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  it('renders the API message', () => {
    const fixture = TestBed.configureTestingModule({
      imports: [AppComponent, HttpClientTestingModule],
    }).createComponent(AppComponent);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne((request) => request.url.endsWith('/hello')).flush({ message: 'Hello World' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Hello World');
    http.verify();
  });
});
