import { Component, computed, inject, input } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Budget, DeductEntry, Distress, InspectionDetail, RepairStep } from '../models/analysis.model';
import { I18n, TranslatePipe } from '../i18n/i18n';
import { DEDUCT_LABELS, RISK_LABELS, SEVERITY_LABELS, TYPE_LABELS, isCrack } from '../labels';

/**
 * The inspection file a municipality keeps: photo with the outlined distresses, PCI, quantities, unit-price
 * budget and repair protocol on A4. Hidden on screen; the browser's print dialog saves it as PDF.
 */
@Component({
  selector: 'app-print-report',
  standalone: true,
  imports: [DatePipe, DecimalPipe, TranslatePipe],
  templateUrl: './print-report.html',
  styleUrl: './print-report.css'
})
export class PrintReport {
  inspection = input.required<InspectionDetail>();
  budget = input<Budget | null>(null);
  segmentName = input<string | null>(null);

  private i18n = inject(I18n);
  readonly printedAt = new Date();

  steps = computed(() => [...this.inspection().repairSteps].sort((a, b) => a.order - b.order));
  massT = computed(() => this.inspection().distresses.reduce((sum, d) => sum + (d.quantities?.massT ?? 0), 0));
  affectedM2 = computed(() =>
    this.inspection().distresses.filter(d => !isCrack(d.type)).reduce((sum, d) => sum + d.measurement.areaM2, 0)
  );
  potholes = computed(() => this.inspection().distresses.filter(d => d.type === 'Pothole').length);

  isCrack = isCrack;

  points(d: Distress): string {
    const pts = d.mask?.length >= 3
      ? d.mask
      : [[d.box.x, d.box.y], [d.box.x + d.box.width, d.box.y], [d.box.x + d.box.width, d.box.y + d.box.height], [d.box.x, d.box.y + d.box.height]];
    return pts.map(([x, y]) => `${x * 100},${y * 100}`).join(' ');
  }

  type(d: Distress): string {
    return this.i18n.t(TYPE_LABELS[d.type] ?? d.type);
  }

  severity(d: Distress): string {
    return this.i18n.t(SEVERITY_LABELS[d.severity]);
  }

  deduct(d: DeductEntry): string {
    return this.i18n.t(DEDUCT_LABELS[d.distress] ?? d.distress);
  }

  unit(d: DeductEntry): string {
    return d.unit === 'SquareMetres' ? 'm²' : d.unit === 'Metres' ? 'm' : this.i18n.t('und');
  }

  risk(s: RepairStep): string {
    return this.i18n.t(RISK_LABELS[s.risk]);
  }

  /** Same bands as the API's PciCalculator.Classify. */
  pciClass(pci: number): string {
    return pci >= 85 ? 'pci-a' : pci >= 70 ? 'pci-b' : pci >= 55 ? 'pci-c' : pci >= 40 ? 'pci-d' : 'pci-e';
  }
}
