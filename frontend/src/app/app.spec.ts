import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the PaveLens wordmark', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.wordmark-name')?.textContent).toContain('PaveLens');
  });

  it('should draw mask holes as extra subpaths of the outline', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const distress = {
      mask: [[0, 0], [0.5, 0], [0.5, 0.5]],
      holes: [[[0.2, 0.1], [0.3, 0.1], [0.3, 0.2]]],
      box: { x: 0, y: 0, width: 0.5, height: 0.5 }
    } as unknown as Parameters<App['maskPath']>[0];
    expect(app.maskPath(distress)).toBe('M0,0 L50,0 L50,50 Z M20,10 L30,10 L30,20 Z');
  });
});
