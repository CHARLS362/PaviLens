import { DeductEntry, DistressType } from './models/analysis.model';

/** Spanish source labels for API enums; the I18n service translates them. */
export const TYPE_LABELS: Record<DistressType, string> = {
  Pothole: 'Bache',
  AlligatorCrack: 'Piel de cocodrilo',
  LongitudinalCrack: 'Fisura longitudinal',
  TransverseCrack: 'Fisura transversal',
  Rutting: 'Ahuellamiento',
  Other: 'Otro'
};

export const DEDUCT_LABELS: Record<DeductEntry['distress'], string> = {
  AlligatorCracking: 'Piel de cocodrilo',
  LongitudinalTransverseCracking: 'Fisuras long./transv.',
  Pothole: 'Baches',
  Rutting: 'Ahuellamiento'
};

/** Cracks are sealed per metre; everything else is patched by area. */
export function isCrack(type: DistressType): boolean {
  return type === 'LongitudinalCrack' || type === 'TransverseCrack';
}
