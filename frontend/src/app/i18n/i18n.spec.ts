import { TestBed } from '@angular/core/testing';
import { I18n } from './i18n';

describe('I18n', () => {
  let i18n: I18n;

  beforeEach(() => {
    localStorage.removeItem('pavelens.lang');
    i18n = TestBed.inject(I18n);
  });

  it('returns the Spanish source text in Spanish', () => {
    i18n.set('es');
    expect(i18n.t('Presupuesto')).toBe('Presupuesto');
    expect(i18n.t('{n} de 4', { n: 2 })).toBe('2 de 4');
  });

  it('translates dictionary entries, placeholders and server patterns into English', () => {
    i18n.set('en');
    expect(i18n.t('Presupuesto')).toBe('Budget');
    expect(i18n.t('{done} de {total}', { done: 3, total: 7 })).toBe('3 of 7');
    expect(i18n.t('Faltan 2 esquinas por marcar.')).toBe('2 corners left to mark.');
    expect(i18n.t('Desvío hacia el norte')).toBe('Detour heading north');
  });

  it('falls back to Spanish for unknown text and renders null placeholders as empty', () => {
    i18n.set('en');
    expect(i18n.t('Texto libre del modelo')).toBe('Texto libre del modelo');
    expect(i18n.t('confianza {c} %', { c: null })).toBe('confidence  %');
  });

  it('remembers the choice and tags the document', () => {
    i18n.set('en');
    expect(localStorage.getItem('pavelens.lang')).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });
});
