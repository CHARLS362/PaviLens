# 🏆 DESAFÍO: Meta Global AI Developer Hackathon

> **Documento Estratégico y Técnico de Participación**  
> **Proyecto:** PaveLens AI 🛣️⚡  
> **Categoría:** Indie Developer Track  
> **Organizan:** Meta & Cerebral Valley  
> **Bolsa de Premios:** $1,000,000 USD en total ($400,000 USD en Indie Track)  
> **Fecha Límite de Entrega:** **4 de Noviembre de 2026 a las 9:00 PM ET**  
> **Portal de Envío:** [https://cerebralvalley.ai/e/meta-ai-developer-hackathon/hackathon/submit](https://cerebralvalley.ai/e/meta-ai-developer-hackathon/hackathon/submit)

---

## 1. Información General del Desafío

El **Meta Global AI Developer Hackathon** es una competencia global diseñada para construir la próxima generación de productos de IA basados en la suite de modelos de Meta: **SAM 3 / 3.1 (Segment Anything)**, **Muse Wonder**, **Muse Spark**, **Muse Image** y modelos de pesos abiertos (*open-weights*).

### Fechas Clave del Calendario:
* **26 de Octubre:** Kickoff oficial y apertura de desarrollo.
* **27 de Octubre – 4 de Noviembre:** Período de construcción y soporte técnico en Discord.
* **4 de Noviembre (9:00 PM ET):** **Cierre definitivo de envíos en `/submit`**.
* **4 – 8 de Noviembre:** Período de evaluación de los jueces.
* **9 de Noviembre:** Ceremonia de clausura, finales en vivo y anuncio de ganadores.

---

## 2. Categorías y Premios en Efectivo

La competencia cuenta con dos categorías independientes. Estamos registrados y compitiendo en el **Indie Developer Track**:

### 🎯 Indie Developer Track ($400,000 USD en Premios)
Dirigido a desarrolladores individuales, estudiantes y equipos de hasta 4 miembros que construyen proyectos funcionales con alta ambición técnica y creatividad.

* 🥇 **1er Lugar:** **$150,000 USD**
* 🥈 **2do Lugar:** **$100,000 USD**
* 🥉 **3er Lugar:** **$75,000 USD**
* 🏅 **4to Lugar:** **$50,000 USD**
* 🎖️ **5to Lugar:** **$25,000 USD**
* 🎁 **Premios Bonus (Hasta $50,000 USD):** Voto de la comunidad, proyecto más viral, etc.

*(Pagos realizados en USD directamente por Cerebral Valley mediante transferencia bancaria internacional con Tremendous en los 30 días posteriores al evento).*

---

## 3. Rúbrica Oficial de Evaluación (Judging Criteria)

Los jueces calificarán las entregas en una escala estricta basada en **4 pilares fundamentales**:

```
┌────────────────────────────────────────────────────────┐
│               RÚBRICA DE EVALUACIÓN OFICIAL            │
├─────────────────────────┬─────────────┬────────────────┤
│ Criterio                │ Ponderación │ Enfoque        │
├─────────────────────────┼─────────────┼────────────────┤
│ 1. Impact               │     30%     │ Utilidad real  │
│ 2. Demo & Video         │     25%     │ Funcionalidad  │
│ 3. Meta AI Tooling      │     25%     │ Uso de modelos │
│ 4. Technical Depth      │     20%     │ Arquitectura   │
└─────────────────────────┴─────────────┴────────────────┘
```

---

## 4. Alineación Estratégica de PaveLens AI

**PaveLens AI** maximiza cada uno de los 4 criterios de la rúbrica oficial de Meta:

| Requisito del Jurado | Cómo lo Cumple PaveLens AI |
| :--- | :--- |
| **Impacto (30%)** | Una municipalidad con presupuesto pequeño obtiene, desde una foto de celular, el inventario de deterioros, un PCI comparable para priorizar tramos, el metrado y presupuesto auditables, y el costo del desvío en tráfico. Pensado para Puno (altiplano, heladas, radiación UV), con normas y precios configurables por región. |
| **Herramientas de Meta (25%)** | **Meta SAM 3** segmenta por concepto de texto (*pothole*, *alligator crack*…) y entrega el contorno no convexo de cada deterioro, sin entrenar un modelo propio. **Muse Wonder** (`muse-wonder-1.0`, API de Meta) lee la foto junto con las medidas en metros y actúa como ingeniero de conservación: clasifica, asigna severidad ASTM D6433, redacta el protocolo de obra y responde preguntas de la cuadrilla. Ambos se consumen por API. |
| **Profundidad Técnica (20%)** | **Clean Architecture en .NET 10** con 133 pruebas automatizadas de backend y 10 de frontend, interfaz e informes en español e inglés, EF Core (SQLite / PostgreSQL) y frontend **Angular 22** con signals. Homografía DLT para fotos oblicuas, área y momentos por teorema de Green (error < 1 % verificado contra la verdad de terreno), PCI ASTM D6433, APU MTC en PEN y equilibrio de Wardrop (Frank-Wolfe + BPR). Los números nunca salen del LLM: el modelo razona, el código calcula. |
| **Demo en Vivo (25%)** | Foto con los contornos de cada deterioro, zoom hasta 600 % y cursor que muestra la posición en metros sobre la calzada. Panel con PCI, m², toneladas y presupuesto en soles que se recalcula al ingresar la profundidad medida. Captura con la cámara y calibración de 4 puntos con cuadrícula proyectada. Planificador de desvíos con mapa animado, costo social en soles y la franja de obra recomendada. |

---

## 5. Material Listo para el Formulario de Entrega (`/hackathon/submit`)

### 📌 Datos Generales:
* **Project Name:** `PaveLens AI`
* **Tagline:** `From one road photo to a measured, costed and traffic-aware pavement repair plan, powered by Meta SAM 3 & Muse Wonder.`
* **Track:** `Indie Developer Track`
* **Theme:** `Build for Your World / Push the Frontier`

### 📝 Problem Description (El Problema):
> Pavement condition surveys are still done on foot, with a tape measure and a notebook. Severity ratings vary between inspectors, patch quantities are estimated by eye, and the resulting budgets cannot be audited against the actual damage. When a crew then closes a lane to repair it, the detour is rarely modelled, so congestion simply moves to streets without spare capacity. Municipalities in places like Puno, Peru (3,800 m, nightly frost, extreme UV) face this with very small maintenance budgets.

### 💡 Solution & How it Works (La Solución):
> PaveLens AI turns a smartphone or drone photo of the road into a repair file:
> 1. **Concept segmentation (Meta SAM 3):** text prompts such as "pothole" or "alligator crack" return the exact non-convex outline of every distress, not a bounding box.
> 2. **Measurement in metres:** a homography from four known ground points (or the road width for top-down shots) projects every outline onto the road plane. Area, perimeter, oriented length × width and eccentricity are computed with Green's theorem; tests check every mask against the true drawn area within 1 %.
> 3. **Engineering diagnosis (Meta Muse Wonder):** the model sees the photo plus the measured geometry, classifies each distress, rates its ASTM D6433 severity and writes the step-by-step repair protocol. It never produces numbers: PCI, patch quantities and the unit-price budget (Peruvian MTC items, in PEN) are computed deterministically, and recalculated when the inspector enters the measured depth.
> 4. **Work-zone traffic impact:** closing lanes on the road network is solved as a Wardrop user equilibrium (Frank-Wolfe with BPR delay), reporting vehicle-hours lost, which streets absorb the detour, and the social cost in soles of each work window (peak, off-peak, night) so the cheapest one can be scheduled.

### 🤖 Use of Meta AI Tooling (Modelos de Meta Utilizados):
> - **Meta SAM 3:** zero-shot, text-prompted segmentation of five pavement distress concepts. Overlapping answers from different prompts are de-duplicated by mask IoU, and each connected part becomes its own distress so the PCI pothole count is right.
> - **Meta Muse Wonder (`muse-wonder-1.0`):** multimodal reasoning over the photo and the measured geometry. It assigns type, ASTM D6433 severity, diagnosis and the repair protocol, and powers the crew assistant. Its JSON output is validated against the segments, with one self-repair retry on error.

---

## 6. Guion Estratégico para el Video Demo (2 Minutos)

> Las cifras son las que muestra la escena *Av. El Sol*. Si se graba con modelos reales (no en modo demostración), se leen de la pantalla.

* **0:00 – 0:20 (El problema):**
  * *"En Puno, como en casi todo el Perú, los baches se miden a pie, con wincha. La severidad depende de quién mire, el metrado se calcula a ojo y nadie modela el tráfico que provoca cerrar la calle."*
* **0:20 – 0:50 (SAM 3 y medición):**
  * *"Subo una foto. Meta SAM 3 recibe conceptos en texto, 'pothole', 'alligator crack', y devuelve el contorno real de cada daño, no una caja. Con el ancho de la calzada, o con cuatro puntos conocidos en una foto oblicua, cada vértice se proyecta al plano de la vía: este bache mide 0.57 m²."* (Zoom sobre B1 y cursor mostrando metros).
* **0:50 – 1:25 (Muse Wonder e ingeniería):**
  * *"Muse Wonder ve la foto y las medidas, clasifica cada daño con severidad ASTM D6433 y escribe el protocolo de obra. Pero los números no los inventa el modelo: el PCI, 33.6 y parcial porque la foto no cubre una unidad completa, el corte de 1.32 m², 0.24 t de mezcla y S/ 193.40 de presupuesto salen de cálculos verificables. Si el inspector mide la profundidad, todo se recalcula."* (Ingresar profundidad y mostrar el cambio).
* **1:25 – 1:50 (Tráfico durante la obra):**
  * *"Al cerrar un carril, PaveLens resuelve el equilibrio de tráfico con Frank-Wolfe y la función BPR: cuántas horas-vehículo se pierden y qué calles absorben el desvío."* (Planificador de desvíos: cerrar Jr. Lima, ver los desvíos numerados y comparar S/ 456 en hora punta contra la noche).
* **1:50 – 2:00 (Cierre):**
  * *"PaveLens AI: los modelos de Meta ven el daño; la ingeniería lo mide, lo valoriza y planifica la obra. .NET 10, Angular 22 y más de 140 pruebas automatizadas."*

---

## 7. Checklist Final de Entrega

- [x] Backend compilado con Clean Architecture (.NET 10), 133 pruebas de backend y 10 de frontend y API REST documentada en Scalar (`/scalar`).
- [x] README en inglés para el jurado global; interfaz ES/EN e informes de Muse Wonder en el idioma elegido.
- [x] Escenas de muestra en el diálogo de captura y gestos táctiles en el visor.
- [x] Adaptadores de modelos Meta por API: SAM 3 (Roboflow serverless) y Muse Wonder (`api.meta.ai`).
- [ ] Obtener las claves de Roboflow y de la API de Meta y probar una foto real de punta a punta.
- [ ] Escenas con fotos reales (Wikimedia, CC BY-SA 4.0) con atribución visible.
- [x] Inicializar git y agregar `LICENSE` (MIT).
- [x] Base de datos auto-migrada y poblada con escenas calibradas de demostración (SQLite / PostgreSQL).
- [x] Modelado físico-matemático documentado en `PLANIFICACION.md`.
- [x] Conceptualización completa del producto documentada en `tematica.md`.
- [x] Subir el repositorio a GitHub: https://github.com/CHARLS362/PaviLens
- [ ] Grabar el video de demostración de 2 minutos siguiendo el guion.
- [ ] Completar y enviar el formulario antes del **4 de Noviembre, 9:00 PM ET**.
