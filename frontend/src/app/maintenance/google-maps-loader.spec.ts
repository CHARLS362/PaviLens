import { decodePolyline } from './google-maps-loader';
import { pciColor } from './maintenance-map';

describe('decodePolyline', () => {
  it('decodes the reference example of the Google polyline format', () => {
    const points = decodePolyline('_p~iF~ps|U_ulLnnqC_mqNvxq`@');
    expect(points).toEqual([
      { lat: 38.5, lng: -120.2 },
      { lat: 40.7, lng: -120.95 },
      { lat: 43.252, lng: -126.453 },
    ]);
  });

  it('returns nothing for an empty string', () => {
    expect(decodePolyline('')).toEqual([]);
  });
});

describe('pciColor', () => {
  it('uses the PCI bands of the rest of the app', () => {
    expect(pciColor(20)).toBe('#c62828');
    expect(pciColor(60)).toBe('#f2b705');
    expect(pciColor(90)).toBe('#2e7d32');
    expect(pciColor(null)).toBe('#8a929c');
  });
});

describe('isFullClosure', () => {
  it('recognises a full closure in the planner wording, in both languages', async () => {
    const { isFullClosure } = await import('./maintenance-map');
    expect(isFullClosure('Cierre total con desvío')).toBe(true);
    expect(isFullClosure('Full closure with detour')).toBe(true);
    expect(isFullClosure('Cierre de un carril con paso alterno')).toBe(false);
    expect(isFullClosure(null)).toBe(false);
  });
});

describe('alongPath', () => {
  it('walks the path by length and points the crew arrow along the street', async () => {
    const { alongPath } = await import('./map-adapter');
    const path = [{ lat: 0, lng: 0 }, { lat: 0, lng: 1 }, { lat: 0, lng: 3 }];
    expect(alongPath(path, 0).point).toEqual({ lat: 0, lng: 0 });
    expect(alongPath(path, 0.5).point.lng).toBeCloseTo(1.5);
    expect(alongPath(path, 0.5).bearing).toBeCloseTo(90); // heading east
    expect(alongPath(path, 1).point.lng).toBeCloseTo(3);
  });
});
