import { TestBed } from '@angular/core/testing';
import { A11ySettings } from './a11y-prefs';

describe('A11ySettings', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.removeItem('pavelens.a11y');
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    for (const k of ['text', 'contrast', 'motion', 'palette']) delete root.dataset[k];
    localStorage.removeItem('pavelens.a11y');
  });

  it('reflects the preferences on <html> and saves them', () => {
    const settings = TestBed.inject(A11ySettings);
    settings.update({ textSize: 'xlarge', highContrast: true, colorSafe: true });
    TestBed.tick();

    expect(root.dataset['text']).toBe('xlarge');
    expect(root.dataset['contrast']).toBe('high');
    expect(root.dataset['palette']).toBe('safe');
    expect(root.dataset['motion']).toBeUndefined();
    expect(JSON.parse(localStorage.getItem('pavelens.a11y')!).textSize).toBe('xlarge');

    settings.reset();
    TestBed.tick();
    expect(root.dataset['text']).toBe('normal');
    expect(root.dataset['contrast']).toBeUndefined();
  });

  it('ignores corrupt or unknown stored values', () => {
    localStorage.setItem('pavelens.a11y', JSON.stringify({ textSize: 'huge', reduceMotion: 'yes' }));
    const settings = TestBed.inject(A11ySettings);
    expect(settings.prefs()).toEqual({ textSize: 'normal', highContrast: false, reduceMotion: false, colorSafe: false });
  });
});
