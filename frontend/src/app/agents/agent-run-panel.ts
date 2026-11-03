import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { AgentAction, AgentRun } from '../models/analysis.model';
import { TranslatePipe } from '../i18n/i18n';

/** The steps the coordinator must complete, in the order a person reads them. */
const STEPS: { tool: string; label: string; what: string }[] = [
  { tool: 'load_region', label: 'Contexto regional', what: 'Normas, precios y clima de la región' },
  { tool: 'load_city_network', label: 'Red vial', what: 'Calles reales alrededor de la foto' },
  { tool: 'segment_image', label: 'Ver', what: 'SAM delimita cada zona dañada' },
  { tool: 'measure_segments', label: 'Medir', what: 'Cada máscara se mide en metros' },
  { tool: 'diagnose_distresses', label: 'Diagnosticar', what: 'Tipo, severidad y causa de cada deterioro' },
  { tool: 'compute_report', label: 'Calcular', what: 'PCI, metrado y presupuesto por fórmulas' },
  { tool: 'assess_mobility', label: 'Tránsito', what: 'Plan de desvío durante la obra' },
  { tool: 'verify_report', label: 'Revisar', what: 'Comprobación por código y revisor independiente' },
];

export interface StepView {
  tool: string; label: string; what: string;
  state: 'done' | 'running' | 'pending' | 'failed';
  action?: AgentAction; corrections: AgentAction[];
}

@Component({
  selector: 'app-agent-run-panel', standalone: true, imports: [DecimalPipe, TranslatePipe],
  templateUrl: './agent-run-panel.html', styleUrl: './agent-run-panel.css',
})
export class AgentRunPanel {
  run = input.required<AgentRun>();
  /** Steps only, for the processing card. */
  compact = input(false);

  isRunning = computed(() => ['Pending', 'Running'].includes(this.run().status));

  steps = computed<StepView[]>(() => {
    const actions = this.run().actions;
    let waiting = false;
    return STEPS.map(step => {
      const mine = actions.filter(a => a.tool === step.tool);
      const done = [...mine].reverse().find(a => a.status === 'Succeeded');
      const corrections = mine.filter(a => a.status !== 'Succeeded');
      let state: StepView['state'] = 'done';
      if (!done) {
        state = this.run().status === 'Failed' ? (waiting ? 'pending' : 'failed') : waiting ? 'pending' : this.isRunning() ? 'running' : 'pending';
        waiting = true;
      }
      return { ...step, state, action: done, corrections };
    });
  });

  status = computed(() => ({
    Pending: 'En cola', Running: 'Trabajando…', Ready: 'Listo', NeedsReview: 'Revisar observaciones', Failed: 'Falló',
  } as Record<string, string>)[this.run().status] ?? this.run().status);

  statusClass = computed(() => ({ Ready: 'ok', NeedsReview: 'warn', Failed: 'bad' } as Record<string, string>)[this.run().status] ?? 'busy');

  /** The reviewer's issues; older runs mixed them into the warnings, so those show everything there. */
  reviewIssues = computed(() => this.run().reviewIssues ?? []);
  tokens = computed(() => this.run().inputTokens + this.run().outputTokens);

  /** "código", "simulación" or the Meta model name. */
  engine(model: string): { label: string; meta: boolean } {
    if (!model || model.startsWith('deterministic')) return { label: 'código', meta: false };
    if (model.startsWith('simulation')) return { label: 'simulación', meta: false };
    return { label: model, meta: true };
  }

  seconds(ms: number) { return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(ms < 10000 ? 1 : 0)} s`; }

  evidence(value: string) { try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; } }
}
