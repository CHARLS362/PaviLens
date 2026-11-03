import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CityMap } from './city-map';

describe('CityMap', () => {
  beforeEach(() => {
    localStorage.setItem('pavelens.lang', 'es');
    TestBed.configureTestingModule({ imports: [CityMap], providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  it('cancels an area request when the user changes city', () => {
    const fixture = TestBed.createComponent(CityMap);
    const map = fixture.componentInstance;
    map.load();
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/geography/network');
    map.place({lat:4.5981,lon:-74.076});
    expect(request.cancelled).toBe(true);
    expect(map.network()).toBeNull(); expect(map.busy()).toBe(false);
    expect(map.latitude()).toBe(4.5981);
    TestBed.inject(HttpTestingController).verify();
    fixture.destroy();
  });

  it('requires street selection and sends the selected edge to the closure endpoint', async () => {
    const fixture = TestBed.createComponent(CityMap);
    const map = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    map.load();
    http.expectOne(r => r.url === '/api/v1/geography/network').flush({
      id:'snapshot', retrievedAtUtc:'2026-11-02T00:00:00Z', latitude:0,longitude:0,radiusM:600,
      nodes:[{id:1,latitude:0,longitude:0},{id:2,latitude:0,longitude:.001}],
      edges:[{id:'edge',wayId:7,from:1,to:2,name:'Main Street',lengthM:111,speedKmh:30,speedAssumed:true,forward:true,backward:false}], warnings:[]
    });
    expect(map.selected()).toBe('');
    map.closeRoad(); http.expectNone(r => r.method === 'POST');
    map.select('edge'); map.closeRoad();
    const request = http.expectOne('/api/v1/geography/networks/snapshot/closure');
    expect(request.request.body).toEqual({edgeId:'edge'});
    request.flush({closedEdgeId:'edge', routes:[],warnings:[]});
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay recorrido alternativo en el área importada.');
    http.verify(); fixture.destroy();
  });
});
