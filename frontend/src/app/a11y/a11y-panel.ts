import { Component, inject } from '@angular/core';
import { TranslatePipe } from '../i18n/i18n';
import { A11ySettings, TextSize } from './a11y-prefs';

/**
 * Accessibility button and panel in the top bar. The panel is a native popover, so Esc, a click
 * outside and the return of focus to the button come from the browser.
 */
@Component({
  selector: 'app-a11y-panel',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './a11y-panel.html',
  styleUrl: './a11y-panel.css',
})
export class A11yPanel {
  readonly settings = inject(A11ySettings);

  readonly sizes: { id: TextSize; label: string; sample: string }[] = [
    { id: 'normal', label: 'Normal', sample: '100 %' },
    { id: 'large', label: 'Grande', sample: '115 %' },
    { id: 'xlarge', label: 'Muy grande', sample: '130 %' },
  ];

  readonly shortcuts: { keys: string; action: string }[] = [
    { keys: 'Tab', action: 'Recorrer los controles' },
    { keys: '← → ↑ ↓', action: 'Mover la foto ampliada (con el visor enfocado)' },
    { keys: '+ / −', action: 'Acercar o alejar la foto' },
    { keys: '0', action: 'Restablecer la vista de la foto' },
    { keys: '← →', action: 'Cambiar de pestaña en el panel derecho' },
    { keys: 'Esc', action: 'Cerrar diálogos y este panel' },
  ];
}
