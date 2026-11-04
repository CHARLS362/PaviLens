# Guion y plan de producción · PaveLens AI (v3, versión de venta)

Meta Global AI Developer Hackathon · Indie track. **Duración objetivo: 2:00.** Revisa en el portal de envío que el límite oficial no sea menor.

Rúbrica: Impacto 30 %, Demo y video 25 %, Meta AI Tooling 25 %, Profundidad técnica 20 %.

## Qué cambia respecto a la v2

| v2 | v3 |
|---|---|
| Voz por bloques, con silencios entre tramos | **Una sola toma continua** de voz, de principio a fin, y música bajo toda la pieza |
| Sin música ni efectos | Música original instrumental con *ducking* bajo la voz, efectos en cada transición y en los clics |
| Tomas de la app a pantalla completa, sin cámara | **Zoom de cámara** nítido sobre lo que dice la voz: máscaras, cifras, revisor, presupuesto y franja de obra |
| Guion explicativo | **Guion de venta**: gancho, dolor, solución, prueba, confianza, impacto y llamada a la acción |
| Sin plano de contexto | Un plano de contexto de una ciudad andina, **rotulado "AI-generated illustration"** |

## Estructura de venta

| # | Tramo | Tiempo | Objetivo | Rúbrica |
|---|---|---|---|---|
| 1 | Gancho | 0:00–0:09 | El resultado primero: la foto real con las 9 máscaras y las cifras | Demo |
| 2 | Dolor | 0:09–0:24 | Puno: altitud, heladas, wincha y presupuestos que no se pueden auditar | Impacto |
| 3 | Solución | 0:24–0:31 | "Una foto, una escala, un clic" | Demo |
| 4 | SAM 3.1 | 0:31–0:46 | Palabras simples → contornos reales en metros | Meta AI Tooling |
| 5 | Muse Wonder | 0:46–1:04 | Coordinador de 9 herramientas, trazado y revisor independiente | Meta AI Tooling + Profundidad |
| 6 | Confianza | 1:04–1:20 | La IA no inventa números: PCI, metrados y presupuesto por código | Profundidad |
| 7 | Tránsito | 1:20–1:35 | Equilibrio de Wardrop: de noche el costo baja de S/ 456 a S/ 73 | Impacto + Profundidad |
| 8 | Impacto | 1:35–1:49 | Análisis de 90 s, consistente y auditable, para ciudades con poco presupuesto | Impacto |
| 9 | Cierre | 1:49–2:00 | Lema, stack, "Open source on GitHub" y URL | Demo |

## Voz en off (EN), una sola toma

Ritmo: unas 2,3 palabras por segundo. Tono seguro y cálido, de lanzamiento de producto.

1. *PaveLens outlined nine defects in this intersection, measured each one in metres, and priced the repair. From a single photo.*
2. *In Puno, Peru, nearly four thousand metres up, nightly frost and extreme sun tear roads apart. Yet inspections are still done on foot, with a tape and a notebook. Severity is a judgement call, budgets can't be audited, and every repair closure pushes traffic onto streets with no room to spare.*
3. *Meet PaveLens AI. One photo. One scale. One click.*
4. *Meta SAM 3.1 takes plain words, like pothole or crack, and returns the true outline of every distress. Each outline is projected onto the road plane, so areas come out in real square metres.*
5. *Then Muse Wonder takes charge as the coordinator. It calls nine specialised tools, classifies each distress by ASTM severity, and writes the repair protocol. Every call is traced: model, time and tokens. And an independent reviewer flags what an engineer should check.*
6. *Here's what makes it trustworthy: the AI never invents a number. The condition index, the quantities and the budget in soles all come from tested engineering code. Enter a depth measured on site, and everything recalculates.*
7. *Need to close the street? PaveLens solves a Wardrop traffic equilibrium on a demo network of Puno, and finds the cheapest time to work. Here, working at night cuts the cost to drivers from four hundred and fifty-six soles to seventy-three.*
8. *The result: a survey that starts with a ninety-second analysis, consistent from one inspector to the next, and auditable down to the last square metre. Built for cities with a phone and a small budget.*
9. *PaveLens AI. Meta models see the damage. Engineering measures it, prices it, and plans the work. Open source, on GitHub.*

### Traducción (ES)

1. PaveLens delineó nueve daños en esta intersección, midió cada uno en metros y valorizó la reparación. Con una sola foto.
2. En Puno, a casi cuatro mil metros, las heladas nocturnas y el sol extremo destrozan las pistas. Aun así, las inspecciones se hacen a pie, con wincha y libreta. La severidad depende del criterio, los presupuestos no se pueden auditar y cada cierre por reparación manda el tránsito a calles sin capacidad.
3. Conoce PaveLens AI. Una foto. Una escala. Un clic.
4. Meta SAM 3.1 recibe palabras simples, como bache o fisura, y devuelve el contorno real de cada daño. Cada contorno se proyecta al plano de la vía, así que las áreas salen en metros cuadrados reales.
5. Luego Muse Wonder toma el mando como coordinador. Llama a nueve herramientas especializadas, clasifica cada daño por severidad ASTM y escribe el protocolo de reparación. Cada llamada queda registrada: modelo, tiempo y tokens. Y un revisor independiente señala lo que un ingeniero debe verificar.
6. Esto es lo que lo hace confiable: la IA nunca inventa un número. El índice de condición, los metrados y el presupuesto en soles salen de código de ingeniería probado. Ingresa la profundidad medida en obra y todo se recalcula.
7. ¿Hay que cerrar la calle? PaveLens resuelve un equilibrio de tránsito de Wardrop sobre una red de demostración de Puno y encuentra el horario más barato para trabajar. Aquí, trabajar de noche baja el costo para los conductores de 456 a 73 soles.
8. El resultado: una inspección que empieza con un análisis de noventa segundos, consistente entre inspectores y auditable hasta el último metro cuadrado. Pensado para ciudades con un celular y poco presupuesto.
9. PaveLens AI. Los modelos de Meta ven el daño. La ingeniería lo mide, lo valoriza y planifica la obra. Código abierto, en GitHub.

## Plan visual por tramo

| # | Imagen | Cámara y gráficos | Sonido |
|---|---|---|---|
| 1 | Foto real; las 9 máscaras de SAM se dibujan una por una | Ken Burns lento y tarjetas PCI 10 · 9 · 1.14 m² · S/ 557 | Golpe grave al inicio y "pop" suave por cada tarjeta |
| 2 | Plano de contexto de una ciudad andina (IA, rotulado), luego la tarjeta del problema | Chips y renglones que entran al ritmo de la voz | *Whoosh* de entrada |
| 3 | Logo de PaveLens y diálogo "New inspection" | Corte rápido a la app | *Whoosh* y clics de interfaz |
| 4 | Clip 1: subida, escala y análisis real (89 s a 18×, rotulado) | Zoom a las máscaras al terminar; rótulo SAM 3.1 | Clics y subida de energía |
| 5 | Clip 2: How the AI worked y Reviewer findings | Zoom a la línea de tiempo y al revisor; rótulo Muse | — |
| 6 | Clip 3: PCI, Budget y profundidad con Apply | Zoom a la tabla del PCI, al total en soles y al campo de profundidad | Clic en Apply |
| 7 | Clip 4: Detour planning | Zoom a las rutas y al costo S/ 456 → S/ 73; rótulo "synthetic demo network" | *Whoosh* |
| 8 | Tarjeta de impacto animada: 90 s · 9 daños · 100 % de cifras por código · S/ 73 de noche | Cifras que cuentan hacia arriba | Clímax de la música |
| 9 | Cierre: lema, stack, 174 pruebas, licencia MIT y URL | Entrada escalonada | Resolución de la música |

## Herramientas (MCP y skills)

| Pieza | Herramienta |
|---|---|
| Voz en off continua | MCP ElevenLabs · `eleven_v4`, la misma voz de la v2 |
| Música original (2:05, instrumental, 110 BPM) | MCP ElevenLabs · `eleven_music_v2_5` |
| Efectos: golpe, *whoosh*, clic y "pop" | MCP ElevenLabs · `eleven_text_to_sound_v2` |
| Plano de contexto generado por IA | Descartado: la imagen y el video de ElevenLabs y Higgsfield piden plan de pago, así que el video no usa imágenes generadas por IA |
| Subtítulos y control de calidad | MCP ElevenLabs · `eleven_scribe_v1`: transcribe la voz final con tiempos por palabra |
| Tomas de la app | Playwright sobre Chromium a 1920×1080, con zoom de cámara hecho en el navegador para que el texto quede nítido |
| Gráficos animados | HTML + CSS renderizado cuadro por cuadro a 30 fps: revelado por palabra, escaneo SAM con callouts, contadores, y rótulos y wipes animados como secuencias PNG con alfa |
| Montaje | ffmpeg: voz nivelada a −16 LUFS, música unos 15 dB por debajo con *ducking* (`sidechaincompress`), limitador, master a −14 LUFS con pico real ≤ −1.5 dBTP, H.264 1080p30 |

## Reglas de honestidad (no negociables)

- La red vial de Puno se dice y se rotula como sintética de demostración.
- El análisis real se acelera y se rotula: "Real run · 89 s / shown at 22× speed".
- Cualquier plano generado por IA lleva el rótulo "AI-generated illustration" y no muestra datos.
- Las cifras son las de pantalla de una misma inspección real: PCI 10, 9 daños, 1.14 m², S/ 557, 16 llamadas Meta y 57k tokens.
- No se afirma nada que el producto no haga: inspección por video, conteos reales de tránsito o validación contra Crack500 o RDD2022.
- Solo se usan fotos con derecho de uso.

## Archivos

- Corte final (v4, 1:52): `video/PaveLens_AI_demo_v4.mp4` (ignorado por git) y `video/PaveLens_AI_demo_v4.srt`.
- Scripts de producción: `C:\Users\ASUS\pavelens-video\rec`. Regenera todo con `node render4.mjs` y `node make4.mjs`.
