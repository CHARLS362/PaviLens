import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** Map settings from our API. OpenStreetMap is the default and needs no key; Google is optional. */
export interface MapsConfig {
  mapProvider: 'osm' | 'google'; googleAvailable: boolean; tileUrl: string; tileAttribution: string;
  googleRouting: boolean; usedToday: number; dailyLimit: number;
}
interface MapsConfigResponse extends MapsConfig { browserKey?: string | null; }

/* eslint-disable @typescript-eslint/no-explicit-any */
export type GoogleNs = any;

const OSM_FALLBACK: MapsConfig = {
  mapProvider: 'osm', googleAvailable: false, tileUrl: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  tileAttribution: '© OpenStreetMap contributors · ODbL', googleRouting: false, usedToday: 0, dailyLimit: 0,
};

/**
 * Reads the map settings once and, only if Google is chosen, loads the Maps JavaScript API. The browser key is
 * never in the source code or the bundle: it is asked from our own API at runtime (which only hands it to this
 * app's pages), kept in a private field only until the script is requested and then dropped — not in any signal,
 * storage or log. Being a browser key it is still visible in the page's network traffic, so its real protection
 * is the restriction by website set in Google Cloud. With OpenStreetMap nothing is sent to Google at all.
 */
@Injectable({ providedIn: 'root' })
export class GoogleMapsLoader {
  private http = inject(HttpClient);
  private configLoading?: Promise<MapsConfig>;
  private googleLoading?: Promise<GoogleNs | null>;
  private browserKey: string | null = null;

  config(): Promise<MapsConfig> {
    return (this.configLoading ??= this.fetchConfig());
  }

  /** Loads Google only when asked (the user picked Google and the server allows it). */
  async google(): Promise<GoogleNs | null> {
    const config = await this.config();
    if (!config.googleAvailable) return null;
    return (this.googleLoading ??= this.loadScript());
  }

  private async fetchConfig(): Promise<MapsConfig> {
    try {
      const { browserKey, ...config } = await firstValueFrom(this.http.get<MapsConfigResponse>('/api/v1/maps/config'));
      this.browserKey = browserKey ?? null;
      return { ...OSM_FALLBACK, ...config, googleAvailable: !!config.googleAvailable && !!browserKey };
    } catch {
      return OSM_FALLBACK;
    }
  }

  private loadScript(): Promise<GoogleNs | null> {
    const w = window as any;
    if (w.google?.maps?.Map) return Promise.resolve(w.google);
    const key = this.browserKey;
    this.browserKey = null;
    if (!key) return Promise.resolve(null);
    return new Promise(resolve => {
      const callback = `__pl${Math.random().toString(36).slice(2)}`;
      w[callback] = () => { delete w[callback]; resolve(w.google ?? null); };
      const script = document.createElement('script');
      script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(key)}&v=weekly&loading=async&language=es&region=PE&callback=${callback}`;
      script.async = true;
      script.referrerPolicy = 'strict-origin-when-cross-origin'; // Google checks the website restriction with it.
      script.onerror = () => { delete w[callback]; resolve(null); };
      document.head.appendChild(script);
    });
  }
}

/** Decodes an encoded polyline (precision 5: Google Routes and OSRM) into lat/lng pairs. */
export function decodePolyline(encoded: string): { lat: number; lng: number }[] {
  const points: { lat: number; lng: number }[] = [];
  let index = 0, lat = 0, lng = 0;
  while (index < encoded.length) {
    for (const axis of [0, 1]) {
      let result = 0, shift = 0, b: number;
      do {
        b = encoded.charCodeAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20 && index < encoded.length);
      const delta = result & 1 ? ~(result >> 1) : result >> 1;
      if (axis === 0) lat += delta; else lng += delta;
    }
    points.push({ lat: lat / 1e5, lng: lng / 1e5 });
  }
  return points;
}
