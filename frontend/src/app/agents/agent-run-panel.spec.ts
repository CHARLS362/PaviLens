import { TestBed } from '@angular/core/testing';
import { AgentRunPanel } from './agent-run-panel';
import { AgentRun } from '../models/analysis.model';

function run(overrides: Partial<AgentRun>): AgentRun {
  return {
    runId: 'r', engine: 'Meta', coordinatorModel: 'muse-wonder-1.0', status: 'Running', regionalProfileJson: '{}',
    actions: [], warnings: [], modelCalls: 0, inputTokens: 0, outputTokens: 0, ...overrides,
  };
}

function render(value: AgentRun) {
  const fixture = TestBed.createComponent(AgentRunPanel);
  fixture.componentRef.setInput('run', value);
  fixture.detectChanges();
  return fixture;
}

describe('AgentRunPanel', () => {
  it('shows what each step found and marks the next one as running', () => {
    const fixture = render(run({
      actions: [
        { agent: 'Contexto regional', tool: 'load_region', model: 'deterministic', status: 'Succeeded', durationMs: 4, evidence: '{}', summary: 'Perfil Lima' },
        { agent: 'Inspección visual', tool: 'segment_image', model: 'sam-3.1', status: 'Succeeded', durationMs: 3200, evidence: '{}', summary: '8 zonas dañadas encontradas' },
      ],
    }));
    const steps = fixture.componentInstance.steps();
    expect(steps.map(s => s.state).slice(0, 4)).toEqual(['done', 'running', 'done', 'pending']);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('8 zonas dañadas encontradas');
    expect(text).toContain('Meta · sam-3.1');
    expect(text).toContain('3.2 s');
  });

  it('keeps reviewer findings apart from the method assumptions', () => {
    const fixture = render(run({
      status: 'NeedsReview', reviewIssues: ['La severidad no coincide con la medida.'], warnings: ['Profundidad asumida.'],
      modelCalls: 5, inputTokens: 1000, outputTokens: 200,
    }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.review')?.textContent).toContain('La severidad no coincide');
    expect(el.querySelector('.assumptions')?.textContent).toContain('Profundidad asumida.');
    expect(el.querySelector('.usage')?.textContent).toContain('1,200 tokens');
  });

  it('marks a step that a rejected attempt preceded as corrected', () => {
    const fixture = render(run({
      status: 'Ready',
      actions: [
        { agent: 'Medición', tool: 'measure_segments', model: 'deterministic', status: 'Rejected', durationMs: 1, evidence: 'Primero segment_image', summary: 'Primero segment_image' },
        { agent: 'Medición', tool: 'measure_segments', model: 'deterministic', status: 'Succeeded', durationMs: 2, evidence: '{}', summary: '3 m² dañados' },
      ],
    }));
    expect(fixture.nativeElement.querySelector('.correction')?.textContent).toContain('1');
  });
});
