import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { AgentRun } from '../models/analysis.model';
import { TranslatePipe } from '../i18n/i18n';

@Component({
  selector: 'app-agent-run-panel', standalone: true, imports: [DecimalPipe, TranslatePipe],
  templateUrl: './agent-run-panel.html', styleUrl: './agent-run-panel.css',
})
export class AgentRunPanel {
  run = input.required<AgentRun>();
  profile = computed<{ name?: string; version?: string; jurisdictionValidated?: boolean }>(() => {
    try { return JSON.parse(this.run().regionalProfileJson); } catch { return {}; }
  });
  status = computed(() => ({ Pending: 'Pendiente', Running: 'En ejecución', Ready: 'Verificación completada', NeedsReview: 'Requiere revisión', Failed: 'Falló' }[this.run().status] ?? this.run().status));
  label(agent: string) {
    return ({ RegionalContext: 'Contexto regional', GeographicContext: 'Red geográfica', VisualInspector: 'Inspección visual', EngineeringTools: 'Cálculo de ingeniería', DiagnosticEngineer: 'Diagnóstico', MobilityPlanner: 'Movilidad', IndependentReviewer: 'Verificación independiente', Coordinator: 'Coordinación' } as Record<string, string>)[agent] ?? agent;
  }
  evidence(value: string) { try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; } }
}
