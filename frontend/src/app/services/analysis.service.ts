import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  Answer,
  Budget,
  ConversationMessage,
  Distress,
  InspectionDetail,
  InspectionSummary,
  NewInspection,
  PagedResult,
  Problem,
  RepairStep,
  RoadNetwork,
  ReportLanguage,
  TrafficPlan,
  TrafficSimulation,
  WorkPeriod,
  AiStatus
} from '../models/analysis.model';

/** Client for the PaveLens API v1. Requests are relative and reach the backend through the dev proxy. */
@Injectable({ providedIn: 'root' })
export class AnalysisService {
  private http = inject(HttpClient);
  private baseUrl = '/api/v1/inspections';

  list(page = 1, pageSize = 50): Observable<PagedResult<InspectionSummary>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<InspectionSummary>>(this.baseUrl, { params });
  }

  get(id: string): Observable<InspectionDetail> {
    return this.http.get<InspectionDetail>(`${this.baseUrl}/${id}`);
  }

  create(request: NewInspection): Observable<InspectionDetail> {
    const form = new FormData();
    form.append('title', request.title);
    if (request.notes) form.append('notes', request.notes);
    if (request.location) form.append('location', request.location);
    if (request.roadSegmentId) form.append('roadSegmentId', request.roadSegmentId);
    if (request.referenceWidthM) form.append('referenceWidthM', String(request.referenceWidthM));
    if (request.calibration) form.append('calibration', JSON.stringify(request.calibration));
    if (request.language) form.append('language', request.language);
    form.append('image', request.image, request.fileName);
    return this.http.post<InspectionDetail>(this.baseUrl, form);
  }

  retry(id: string): Observable<InspectionDetail> {
    return this.http.post<InspectionDetail>(`${this.baseUrl}/${id}/retry`, null);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  budget(id: string): Observable<Budget> {
    return this.http.get<Budget>(`${this.baseUrl}/${id}/budget`);
  }

  setDepth(id: string, distressId: string, depthM: number): Observable<Distress> {
    return this.http.put<Distress>(`${this.baseUrl}/${id}/distresses/${distressId}/depth`, { depthM });
  }

  setStepCompletion(id: string, stepId: string, completed: boolean): Observable<RepairStep> {
    return this.http.put<RepairStep>(`${this.baseUrl}/${id}/steps/${stepId}/completion`, { completed });
  }

  conversation(id: string, distressId?: string): Observable<ConversationMessage[]> {
    const params = distressId ? new HttpParams().set('distressId', distressId) : undefined;
    return this.http.get<ConversationMessage[]>(`${this.baseUrl}/${id}/conversation`, { params });
  }

  ask(id: string, question: string, distressId?: string): Observable<Answer> {
    return this.http.post<Answer>(`${this.baseUrl}/${id}/conversation`, { question, distressId });
  }

  network(): Observable<RoadNetwork> {
    return this.http.get<RoadNetwork>('/api/v1/network');
  }

  simulate(segmentId: string, lanesClosed: number, period: WorkPeriod = 'Peak', durationH = 8): Observable<TrafficSimulation> {
    return this.http.post<TrafficSimulation>('/api/v1/traffic/simulate', { segmentId, lanesClosed, period, durationH });
  }

  /** Traffic management plan by Muse Wonder through the API key held by the backend. */
  plan(segmentId: string, lanesClosed: number, period: WorkPeriod, durationH: number, language: ReportLanguage): Observable<TrafficPlan> {
    return this.http.post<TrafficPlan>('/api/v1/traffic/plan', { segmentId, lanesClosed, period, durationH, language });
  }

  aiStatus(): Observable<AiStatus> {
    return this.http.get<AiStatus>('/api/v1/ai/status');
  }

  /** Human-readable message from a problem+json error, falling back to a generic text. */
  static describe(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) return 'No hay conexión con la API.';
      if (error.status === 429) return 'Demasiadas solicitudes al modelo; espera un minuto.';
      const problem = error.error as Problem | null;
      const firstError = problem?.errors ? Object.values(problem.errors).flat()[0] : undefined;
      return firstError ?? problem?.detail ?? problem?.title ?? fallback;
    }
    return fallback;
  }
}
