import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { I18nService } from '../../../core/i18n/i18n.service';
import { RightDrawerComponent } from './right-drawer.component';

@Component({
  standalone: true,
  imports: [RightDrawerComponent],
  template: `
    <app-right-drawer [open]="isOpen" titleText="KPI Huy động vốn" (closed)="onClosed()">
  <p class="body-marker">body content</p>
  <button type="button" drawerFooter class="footer-marker">Lưu</button>
</app-right-drawer>
  `,
})
class HostComponent {
  isOpen = true;
  onClosed = jasmine.createSpy('onClosed');
}

describe('RightDrawerComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', [], {
            localization: new I18nService().localization,
          }),
        },
      ],
    });
  });

  it('renders nothing when closed', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.isOpen = false;
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.right-drawer')).toBeNull();
  });

  it('projects body and footer content, and shows the title', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h2')?.textContent).toBe('KPI Huy động vốn');
    expect(element.querySelector('.body-marker')).not.toBeNull();
    expect(element.querySelector('.footer-marker')).not.toBeNull();
  });

  it('emits closed when the close button is clicked', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.drawer-close') as HTMLButtonElement).click();
    expect(fixture.componentInstance.onClosed).toHaveBeenCalled();
  });
});
