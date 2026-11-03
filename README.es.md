# PaveLens AI 🛣️

> **Meta Global AI Developer Hackathon · Indie Developer Track**
> Diagnóstico de pavimentos a partir de una foto: Meta SAM 3 segmenta cada deterioro, la geometría lo lleva a metros reales y PaveLens calcula PCI (ASTM D6433), cubicaje, presupuesto (APU) y el impacto de cerrar el tramo en el tráfico. Muse Wonder clasifica, explica y planifica la obra.

*Read in English: [README.md](README.md)*

![Foto oblicua de una calle: cada deterioro delineado y medido en metros, con el PCI, la traza del pipeline Meta AI y los metrados](docs/screenshots/inspection.png)

Más capturas (presupuesto, planificador de desvíos y expediente PDF) en el [README en inglés](README.md#screenshots). Están en modo demostración, que la app indica en cada pantalla.

## Documentación

- [tematica.md](tematica.md): el producto, el problema que resuelve y el flujo de usuario.
- [DESAFIO.md](DESAFIO.md): bases del hackathon, rúbrica, textos de postulación y guion del video.
- [PLANIFICACION.md](PLANIFICACION.md): plan y fundamentos (proyección al plano, PCI, cubicaje, APU MTC, BPR + Wardrop).
- [backend/README.md](backend/README.md): ejecución, configuración y API.
- [backend/docs/ARCHITECTURE.md](backend/docs/ARCHITECTURE.md): capas, pipeline, modelos de ingeniería y almacenamiento.
- [backend/docs/ai-contracts.md](backend/docs/ai-contracts.md): contratos con SAM 3 y Muse Wonder.

## Qué hace

1. **Segmentación.** SAM 3 recibe la foto y los conceptos `pothole`, `alligator crack`, `longitudinal crack`, `transverse crack` y `rutting`, y devuelve un polígono por deterioro. Si dos conceptos devuelven la misma máscara, se conserva la de mayor confianza.
2. **Medición en metros.** La foto se sube o se toma con la cámara del dispositivo. Con una foto cenital basta el ancho visible de la calzada. Para una foto oblicua se marcan en pantalla 4 esquinas de un rectángulo conocido y se calcula una homografía; una cuadrícula de 1 m proyectada sobre la foto permite verificar la calibración. Antes de analizar, la app estima la resolución en mm/px, la fisura más fina medible y la superficie evaluada. Sobre el polígono proyectado se miden área (Green), perímetro, largo × ancho orientados y excentricidad. Las pruebas verifican que cada máscara mide el área real dibujada con un error menor al 1 %.
3. **Diagnóstico.** Muse Wonder recibe la foto y las medidas ya calculadas, y asigna código, tipo, severidad, hallazgo e intervención, más un protocolo de obra. El modelo **no** calcula áreas, cantidades ni costos: eso lo hace el código, para que una alucinación no infle un presupuesto.
4. **PCI (ASTM D6433).** Valores deducidos por densidad, número máximo de deducidos *m* y CDV iterativo. Si la foto cubre menos de 135 m², el PCI se marca como parcial (cota superior).
5. **Cubicaje y APU.** Corte rectangular con margen, volumen y toneladas de mezcla. Partidas MTC con precios en soles (PEN), gastos generales, utilidad e IGV, y condiciones de obra para Puno (3 827 m s. n. m.). La profundidad medida en campo reemplaza a la supuesta y recalcula todo.
6. **Planificación de desvíos.** Cierre de carriles en una red de demostración del centro de Puno, sobre un mapa con flujo por sentido. Asignación de equilibrio de Wardrop (Frank-Wolfe + BPR) por franja (hora punta, valle, noche) y duración de la obra. Devuelve las veh·h perdidas, los tramos que absorben el desvío y el costo social en soles (Δveh·h × horas × valor del tiempo, configurable en `Traffic:*`), y recomienda la franja más barata. Muse Wonder convierte el resultado en un plan de manejo de tránsito: rutas por tipo de vehículo, señalización, fases, avisos y riesgos.
7. **Expediente (PDF).** Con un clic se imprime un expediente A4 que una municipalidad puede archivar o adjuntar a una orden de trabajo. Incluye la foto con los deterioros delineados, el PCI, el registro de deterioros con metrados, el presupuesto (APU), el protocolo con casillas y líneas de firma. Los resultados de demostración quedan marcados en la hoja.
8. **Probar sin foto.** El diálogo de nueva inspección ofrece las tres escenas de demostración con la escala ya configurada, para recorrer todo el flujo en pocos clics. En el celular, el visor admite pellizcar para hacer zoom y arrastrar con un dedo.
9. **Español e inglés.** La interfaz cambia entre ES y EN en cualquier momento. Cada inspección y cada plan de tránsito guardan el idioma en que se redacta el informe: Muse Wonder lo recibe en el prompt y los escenarios de demostración vienen en ambos idiomas.

## Estructura

```
meta/
├── backend/                      .NET 10, Clean Architecture
│   ├── PaveLens.slnx
│   ├── src/
│   │   ├── PaveLens.Domain/          Inspección, deterioros, geometría validada
│   │   ├── PaveLens.Application/     Casos de uso, proyección, PCI, cubicaje, APU, tráfico
│   │   ├── PaveLens.Infrastructure/  EF Core (SQLite/PostgreSQL), blobs, cola + worker, adaptadores IA
│   │   └── PaveLens.Api/             Minimal API /api/v1, ProblemDetails, rate limiting
│   ├── tests/                    Pruebas unitarias e integración (xUnit v3)
│   └── samples/                  Escenas de demostración y su generador
├── frontend/                     Angular 22 (standalone, signals), ES/EN
└── start.ps1                     Arranca API y web
```

## Inicio rápido

```powershell
.\start.ps1
```

O por separado:

```bash
dotnet run --project backend/src/PaveLens.Api --launch-profile http
```

```bash
npm --prefix frontend start
```

- Web: http://localhost:4200 (redirige `/api` al backend)
- API: http://localhost:5000, documentación interactiva en `/scalar`

En desarrollo el backend usa SQLite, aplica las migraciones y carga tres inspecciones de demostración: Jr. Lima (foto oblicua calibrada), Av. El Sol (bache) y Jr. Arequipa (piel de cocodrilo).

## Modelos de Meta

Los modelos se consumen por API: no hace falta GPU ni servidor propio.

| Paso | Modelo | Servicio | Clave |
|---|---|---|---|
| Segmentación | Meta SAM 3 | API serverless de Roboflow (`/sam3/concept_segment`) | `Ai:Meta:Segmentation:ApiKey` |
| Diagnóstico, asistente y plan de tránsito | Meta Muse Wonder (`muse-wonder-1.0`) | `https://api.meta.ai/v1`, compatible con OpenAI | `Ai:Meta:Reasoning:ApiKey` |

```bash
cd backend/src/PaveLens.Api
dotnet user-secrets set "Ai:Provider" "Meta"
dotnet user-secrets set "Ai:Meta:Segmentation:ApiKey" "<clave Roboflow>"
dotnet user-secrets set "Ai:Meta:Reasoning:ApiKey" "<clave Meta>"
```

Sin claves, `Ai:Provider` vale `Simulation`: los resultados vienen de escenarios fijos identificados por el hash de la imagen, quedan marcados con `engine: "Simulation"` y la interfaz muestra la insignia *Modo demostración*. Las claves van en `dotnet user-secrets` o en variables de entorno, nunca en `appsettings.json`. Detalles en [backend/README.md](backend/README.md#secrets).

## Hoja de ruta

Lo que sigue **no está implementado**; está pensado para crecer sin tocar el núcleo de cálculo:

- Inspección por video con SAM 3.1 (seguimiento de máscaras entre cuadros) y agregación por unidad de muestreo.
- Mapa georreferenciado de inspecciones a partir del GPS de la foto.
- Red vial real desde OpenStreetMap en lugar de la red de demostración.
- Validación de la segmentación contra Crack500 y RDD2022.

## Pruebas

```bash
dotnet test --solution backend/PaveLens.slnx
```

```bash
npm --prefix frontend test -- --watch=false
```
