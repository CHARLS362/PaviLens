import { Injectable, effect, signal } from '@angular/core';

export type TextSize = 'normal' | 'large' | 'xlarge';

export interface A11yPrefs {
  textSize: TextSize;
  highContrast: boolean;
  reduceMotion: boolean;
  /** Severity and PCI colors distinguishable with color-vision deficiency, plus dash patterns on the masks. */
  colorSafe: boolean;
}

const STORAGE_KEY = 'pavelens.a11y';
const DEFAULTS: A11yPrefs = { textSize: 'normal', highContrast: false, reduceMotion: false, colorSafe: false };

/**
 * Display preferences the user sets in the Accessibility panel. They become data attributes on <html>,
 * which styles.css reads; the operating-system settings (prefers-contrast, prefers-reduced-motion)
 * apply on their own, so these only add to them.
 */
@Injectable({ providedIn: 'root' })
export class A11ySettings {
  readonly prefs = signal<A11yPrefs>(A11ySettings.initial());

  constructor() {
    effect(() => {
      const p = this.prefs();
      const root = document.documentElement;
      root.dataset['text'] = p.textSize;
      A11ySettings.flag(root, 'contrast', p.highContrast ? 'high' : null);
      A11ySettings.flag(root, 'motion', p.reduceMotion ? 'reduce' : null);
      A11ySettings.flag(root, 'palette', p.colorSafe ? 'safe' : null);
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(p));
      } catch {
        // Private mode: the choice lasts for this page only.
      }
    });
  }

  update(change: Partial<A11yPrefs>) {
    this.prefs.update(p => ({ ...p, ...change }));
  }

  reset() {
    this.prefs.set(DEFAULTS);
  }

  private static flag(root: HTMLElement, name: string, value: string | null) {
    if (value) root.dataset[name] = value;
    else delete root.dataset[name];
  }

  private static initial(): A11yPrefs {
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as Partial<A11yPrefs> | null;
      if (saved && typeof saved === 'object') {
        return {
          textSize: saved.textSize === 'large' || saved.textSize === 'xlarge' ? saved.textSize : 'normal',
          highContrast: saved.highContrast === true,
          reduceMotion: saved.reduceMotion === true,
          colorSafe: saved.colorSafe === true,
        };
      }
    } catch {
      // Storage unavailable or corrupt: start from the defaults.
    }
    return DEFAULTS;
  }
}
