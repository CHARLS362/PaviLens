import { Injectable, Pipe, PipeTransform, inject, signal } from '@angular/core';
import { EN, EN_PATTERNS } from './en';

export type Lang = 'es' | 'en';

/** Placeholder values; null renders as an empty string, as interpolation does. */
type Params = Record<string, string | number | null | undefined>;

const STORAGE_KEY = 'pavelens.lang';

/**
 * Spanish is the source language: every UI string is written in Spanish and used as its own key.
 * English comes from the EN dictionary; a missing entry falls back to the Spanish text.
 */
@Injectable({ providedIn: 'root' })
export class I18n {
  readonly lang = signal<Lang>(I18n.initial());

  constructor() {
    this.apply();
  }

  set(lang: Lang) {
    this.lang.set(lang);
    this.apply();
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      // Private mode: the choice lasts for this page only.
    }
  }

  /** Translates a Spanish UI string; `{name}` placeholders are filled from `params`. */
  t(es: string, params?: Params): string {
    const text = this.lang() === 'en' ? EN[es] ?? I18n.pattern(es) ?? es : es;
    return params ? text.replace(/\{(\w+)\}/g, (m, k: string) => (k in params ? String(params[k] ?? '') : m)) : text;
  }

  /** Server-computed Spanish text (PCI ratings, budget items, notes): translated when known, shown as is otherwise. */
  data(text: string | null | undefined): string {
    return text == null ? '' : this.t(text);
  }

  private apply() {
    document.documentElement.lang = this.lang();
    document.title = this.t('PaveLens · Diagnóstico de pavimentos');
  }

  private static pattern(es: string): string | undefined {
    for (const [re, en] of EN_PATTERNS) {
      if (re.test(es)) return es.replace(re, en);
    }
    return undefined;
  }

  private static initial(): Lang {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved === 'es' || saved === 'en') return saved;
    } catch {
      // Storage unavailable: fall back to the browser language.
    }
    return navigator.language?.toLowerCase().startsWith('es') ? 'es' : 'en';
  }
}

/** `{{ 'Texto' | t }}` or `{{ '{n} baches' | t: { n: count } }}`; impure so it follows the language signal. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(es: string | null | undefined, params?: Params): string {
    return es == null ? '' : this.i18n.t(es, params);
  }
}
