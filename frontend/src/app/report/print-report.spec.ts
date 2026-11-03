import { TestBed } from '@angular/core/testing';
import { PrintReport } from './print-report';
import { I18n } from '../i18n/i18n';
import { Budget, InspectionDetail } from '../models/analysis.model';

const inspection = {
  id: '01a19f1bc0ed77f49633f0c6a3bfa543',
  title: 'Av. El Sol · bache',
  status: 'Completed',
  engine: 'Simulation',
  language: 'Es',
  referenceWidthM: 3,
  surveyedAreaM2: 9.19,
  attempts: 1,
  createdAt: '2026-11-03T01:52:38Z',
  image: { id: 'img', url: '/api/v1/images/img', width: 1280, height: 960, contentType: 'image/jpeg', sizeBytes: 1 },
  pci: { pci: 33.6, rating: 'Fallado', treatment: 'Reconstrucción de base granular y carpeta', surveyedAreaM2: 9.19, sampleUnitAreaM2: 225, isPartial: true, maxAllowedDeducts: 4.27, deducts: [], iterations: [] },
  distresses: [
    {
      id: 'd1', code: 'B1', type: 'Pothole', severity: 'High', confidence: 0.97,
      box: { x: 0.3, y: 0.3, width: 0.3, height: 0.3 }, mask: [[0.3, 0.3], [0.6, 0.3], [0.6, 0.6]], maskVertexCountRaw: 3,
      measurement: { areaM2: 0.57, perimeterM: 2.8, lengthM: 0.9, widthM: 0.8, eccentricity: 0.4, orientationDeg: 10 },
      quantities: { depthM: 0.075, depthSource: 'Assumed', cutAreaM2: 1.32, volumeM3: 0.099, massT: 0.24 }
    },
    {
      id: 'd2', code: 'FL1', type: 'LongitudinalCrack', severity: 'Low', confidence: 0.9,
      box: { x: 0.7, y: 0, width: 0.05, height: 1 }, mask: [[0.7, 0], [0.75, 0], [0.75, 1]], maskVertexCountRaw: 3,
      measurement: { areaM2: 0.03, perimeterM: 5, lengthM: 2.53, widthM: 0.01, eccentricity: 0.99, orientationDeg: 88 }
    }
  ],
  repairSteps: [{ id: 's1', order: 1, title: 'Señalizar', instructions: 'Conos a 50 m.', tools: ['conos'], risk: 'Low', targetCode: 'B1' }]
} as unknown as InspectionDetail;

const budget = {
  inspectionId: inspection.id, currency: 'PEN', lines: [{ item: '01', description: 'Sellado de fisuras', unit: 'm', quantity: 2.526, unitPrice: 9, amount: 22.73 }],
  directCost: 22.73, overheadPct: 10, overhead: 2.27, profitPct: 5, profit: 1.14, subtotal: 26.14, igvPct: 18, igv: 4.71, total: 30.85,
  assumedDepthCount: 1, region: { name: 'Puno (altiplano)', altitudeM: 3827, recommendedBinder: 'PG 64-28', notes: [] }, notes: []
} as Budget;

describe('PrintReport', () => {
  function render(lang: 'es' | 'en') {
    TestBed.inject(I18n).set(lang);
    const fixture = TestBed.createComponent(PrintReport);
    fixture.componentRef.setInput('inspection', inspection);
    fixture.componentRef.setInput('budget', budget);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('carries the photo masks, the distress log, the budget total and the protocol', () => {
    const el = render('es');
    expect(el.querySelectorAll('svg polygon').length).toBe(2);
    expect(el.textContent).toContain('B1');
    expect(el.textContent).toContain('Sellado por metro lineal');
    expect(el.querySelector('.r-grand')?.textContent).toContain('PEN 30.85');
    expect(el.textContent).toContain('Conos a 50 m.');
    expect(el.querySelector('.r-warning')).not.toBeNull();
  });

  it('labels the fixed texts in the interface language', () => {
    const el = render('en');
    expect(el.textContent).toContain('Pavement inspection file');
    expect(el.textContent).toContain('Failed');
    expect(el.textContent).toContain('Crack sealing');
  });
});
