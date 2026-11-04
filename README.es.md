# PaveLens AI 🛣️

> **Meta Global AI Developer Hackathon · Indie Developer Track**
> De una foto de la pista a una reparación medida, presupuestada y planificada. Meta SAM 3 delinea cada deterioro, la geometría lo lleva a metros reales, el código calcula el Índice de Condición del Pavimento (PCI, ASTM D6433), el material y el presupuesto, y Meta Muse Wonder diagnostica, explica y planifica la obra: el desvío mientras se cierra un carril y la ruta de la cuadrilla en la jornada.

*Read in English: [README.md](README.md)*

![Foto oblicua de una calle: cada deterioro delineado y medido en metros, con el PCI, la traza del pipeline Meta AI y los metrados](docs/screenshots/inspection.png)

**Contenido:** [Qué es](#qué-es) · [Cómo funciona](#cómo-funciona) · [Inicio rápido](#inicio-rápido) · [Configuración](#configuración-env) · [Solución de problemas](#solución-de-problemas) · [Estructura](#estructura) · [Pruebas](#pruebas) · [Documentación](#documentación)

## Video de demostración

[![Demo de PaveLens AI. Haz clic para verla (1:52)](docs/media/demo-thumbnail.jpg)](docs/media/PaveLens_AI_demo.mp4)

**[▶ Ver la demo (1:52, MP4)](docs/media/PaveLens_AI_demo.mp4)** · [YouTube](https://youtu.be/bFq20ClnQ1o) · [Subtítulos en inglés (.srt)](docs/media/PaveLens_AI_demo.en.srt)

El video está narrado en inglés y muestra una ejecución real con los modelos de Meta. El análisis de 89 segundos aparece acelerado y rotulado como tal.

## Qué es

La inspección de pistas todavía se hace a pie, con wincha y libreta. La gravedad cambia de un inspector a otro, el material del parchado se calcula a ojo y nadie puede contrastar un presupuesto con el daño. Cuando una cuadrilla cierra un carril, nadie modela a dónde se va el tránsito. Municipios como **Puno, Perú** (3 827 m s. n. m., heladas cada noche, radiación UV extrema) enfrentan esto con presupuestos de mantenimiento muy pequeños.

PaveLens es una aplicación web para el equipo de mantenimiento vial de un municipio:

- el **inspector** toma una foto de la calle y obtiene el daño medido en metros, el PCI y una reparación con precio;
- el **ingeniero** revisa el diagnóstico, ingresa la profundidad medida en campo y se recalculan el metrado y el presupuesto;
- el **planificador** ve qué le pasa al tránsito si se cierra la calle y en qué horario cuesta menos;
- la **cuadrilla** recibe el orden de las obras del día, el cierre de cada una y la ruta por calles reales.

Una regla recorre toda la aplicación: **la IA decide y explica; el código mide y calcula.** Las áreas, las toneladas, los precios y el PCI nunca los escribe un modelo de lenguaje, así que una alucinación no puede inflar un presupuesto.

## Cómo funciona

```
 Foto + escala ──► Meta SAM 3 / 3.1 ──► Metros ──► Muse Wonder ──► PCI, metrado, presupuesto ──► Plan de tránsito y cuadrilla
 (subida o         un polígono por      proyección   código, tipo,    ASTM D6433, mezcla en       desvíos de Wardrop,
  cámara)          deterioro            al suelo     gravedad, obra   toneladas, partidas MTC      ruta en un mapa real
```

1. **Foto y escala.** En una foto cenital basta el ancho visible de la calzada. En una foto oblicua el inspector marca las 4 esquinas de un rectángulo conocido y PaveLens resuelve una homografía; una cuadrícula de 1 m sobre la foto lo confirma.
2. **Segmentación (Meta SAM 3 o 3.1).** La foto se envía con los conceptos `pothole`, `alligator crack`, `longitudinal crack`, `transverse crack` y `rutting`. Cada deterioro vuelve como un polígono, no como una caja.
3. **Medición.** Cada polígono se proyecta al suelo: área, perímetro, largo × ancho. Las pruebas comparan cada máscara de demostración con su área real, con error menor al 1 %.
4. **Diagnóstico (Meta Muse Wonder).** Un coordinador de agentes llama a herramientas verificadas y a especialistas (diagnóstico, movilidad), y un revisor independiente controla el resultado. El modelo ve la foto y las medidas ya calculadas y devuelve código, gravedad, hallazgo y reparación en JSON, que se valida con un intento de autocorrección. Si algo queda abierto, la inspección queda en *Requiere revisión* y la pantalla indica el siguiente paso.
5. **PCI y presupuesto.** PCI según ASTM D6433 (marcado como parcial si la foto cubre menos de una unidad de muestreo de 135 m²). Parches con corte rectangular, volumen, toneladas de mezcla asfáltica en caliente y presupuesto con partidas del MTC en soles (PEN), gastos generales, utilidad e IGV. Una profundidad medida en campo recalcula todo.
6. **Planificación de desvíos.** Cierre de un carril en la red de demostración del centro de Puno: equilibrio de tránsito de Wardrop (Frank-Wolfe + BPR) por horario, horas-vehículo perdidas, costo social y un plan de manejo de tránsito escrito por Muse Wonder.
7. **Mapa de ciudades.** Calles reales de OpenStreetMap en la zona que elijas, con desvíos de referencia alrededor de un tramo cerrado.
8. **Ruta de mantenimiento.** Todas las inspecciones en un mapa, coloreadas por su PCI. Eliges las obras del día:
   - el código calcula el orden más corto (vecino más cercano + 2-opt);
   - Meta Muse Wonder decide el orden final, el cierre y el horario de cada obra y los riesgos, y el código valida su respuesta;
   - la ruta se dibuja animada sobre calles reales, con las zonas de obra marcadas.

   **Por defecto usa OpenStreetMap (sin clave ni cuenta); Google Maps es opcional.**

Todo funciona en **español e inglés**, tiene un panel de accesibilidad (tamaño de texto, alto contraste, menos movimiento, paleta para daltonismo) y se usa desde el celular (cámara, zoom con dos dedos).

Cada inspección muestra el panel **Pipeline Meta AI**, que traza cada paso: los conceptos enviados a SAM, las máscaras devueltas, la proyección, el modelo usado, cuántos intentos necesitó su JSON y qué números salieron del código.

| | |
|---|---|
| ![Zoom sobre el contorno de la piel de cocodrilo junto al presupuesto por partidas](docs/screenshots/budget.png) | ![Cierre de carril en el Jr. Lima: rutas de desvío, calles que absorben el tránsito, señalización y costos por horario](docs/screenshots/detour-planner.png) |
| **Ver en la foto** acerca la vista a un deterioro; el presupuesto lo calcula el código con las áreas medidas. | **Planificación de desvíos**: equilibrio de Wardrop sobre la red de demostración del centro de Puno. |

## Inicio rápido

Puedes usar la aplicación completa **sin ninguna clave**: arranca en **modo demostración**, con resultados fijos que la interfaz rotula *Modo demostración*. Después agregas una clave de Meta para usar los modelos reales.

### 1. Requisitos

| Herramienta | Versión | Compruébalo con | Descarga |
|---|---|---|---|
| Git | cualquiera | `git --version` | https://git-scm.com |
| .NET SDK | **10.0.100 o superior** | `dotnet --list-sdks` | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Node.js | **22.22+ o 24 LTS** (incluye npm) | `node --version` | https://nodejs.org |

No hace falta: Docker, servidor de base de datos, GPU ni servicios de pago. La API guarda los datos en un archivo SQLite local y los modelos de Meta se llaman por internet.

Los puertos **5000** (API) y **4200** (aplicación web) deben estar libres. En macOS el 5000 lo ocupa *Receptor AirPlay*: desactívalo en *Ajustes del Sistema → General → AirDrop y Handoff*, o mira [Solución de problemas](#solución-de-problemas).

### 2. Clonar

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

### 3. Arrancar

Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

El script hace cuatro cosas:
1. comprueba .NET 10 y Node, y se detiene con un mensaje claro si falta alguno;
2. crea `.env` a partir de `.env.example` la primera vez;
3. instala los paquetes la primera vez (1 a 3 minutos);
4. arranca la API y la aplicación web. En Windows cada una se abre en su propia ventana: deja ambas abiertas.

<details>
<summary>Arrancar a mano (dos terminales)</summary>

Terminal 1, la API, desde la raíz del repositorio:

```bash
dotnet run --project backend/src/PaveLens.Api --launch-profile http
```

Terminal 2, la aplicación web:

```bash
cd frontend
```

```bash
npm ci
```

```bash
npm start
```

Si quieres poner claves, copia tú mismo `.env.example` como `.env`.
</details>

### 4. Abrir la aplicación

Abre **http://localhost:4200**. La primera compilación tarda unos 30 segundos. La barra superior debe decir **Conectado**.

| Qué | URL |
|---|---|
| Aplicación web | http://localhost:4200 |
| Referencia de la API (Scalar) | http://localhost:5000/scalar |
| Estado del servidor | http://localhost:5000/health |
| Modo de IA activo | http://localhost:5000/api/v1/ai/status |

La base de datos se crea en el primer arranque con tres inspecciones de demostración: Jr. Lima (foto oblicua calibrada), Av. El Sol (bache) y Jr. Arequipa (piel de cocodrilo).

### 5. Pruébala

1. **Nueva inspección** → elige una de las escenas de ejemplo (la escala ya viene puesta) → **Analizar**.
2. Abre el resultado: contornos sobre la foto, PCI, metrado y presupuesto. Escribe una profundidad de campo y verás todo recalculado.
3. **Planificación de desvíos**: cierra una calle y compara horarios.
4. **Mantenimiento**: elige las obras del día y pulsa **Planificar la jornada con Meta** para ver la ruta en el mapa.

La [guía de uso](docs/USER_GUIDE.md) explica cada pantalla.

## Configuración (`.env`)

Toda la configuración está en un solo archivo, **`.env` en la raíz del repositorio** (junto a este README). Git lo ignora, así que tus claves no salen de tu máquina. [`.env.example`](.env.example) es la plantilla comentada.

> ⚠️ **La API lee `.env` solo al arrancar.** Después de editarlo, cierra la ventana de la API (o pulsa Ctrl+C) y vuelve a arrancar.

### Usar los modelos reales de Meta

Basta una clave. Consíguela en https://dev.meta.ai y ponla en `.env`:

```dotenv
META_API_KEY=pega-aqui-tu-clave-de-meta
```

Con esa clave, el modo pasa a Meta y la segmentación usa SAM 3.1 en Meta. Al arrancar, la API pregunta a Meta qué modelos permite tu clave y elige el mejor (el Muse Wonder más nuevo; si no, Muse Spark).

Para comprobar que funcionó, mira cualquiera de estas dos:
- la barra superior muestra los nombres de los modelos (por ejemplo *SAM 3.1 · Muse Wonder 1.0*) en lugar de *Modo demostración*;
- http://localhost:5000/api/v1/ai/status indica `Meta`.

Con los modelos reales, **usa una foto real de una pista**: las escenas de ejemplo son dibujos, así que SAM no encuentra daños en ellas y el análisis se detiene con un mensaje claro.

### Todas las opciones

| Variable | Por defecto | Qué hace |
|---|---|---|
| `PAVELENS_AI_PROVIDER` | `Auto` | `Auto` usa Meta si hay `META_API_KEY`; si no, modo demostración. `Simulation` fuerza el modo demostración; `Meta` exige la clave. |
| `META_API_KEY` | vacío | Clave de Meta para Muse Wonder y, salvo que elijas otro servicio, para SAM 3.1. |
| `META_REASONING_MODEL` | vacío | Fija un modelo (por ejemplo `muse-spark-1.3`). Vacío o `auto` elige el mejor. |
| `PAVELENS_SEGMENTATION_SERVICE` | `Auto` | `Auto`: Roboflow si hay `ROBOFLOW_API_KEY`; si no, Meta. También `Roboflow`, `Meta` o `Sidecar`. |
| `ROBOFLOW_API_KEY` | vacío | Clave gratuita de https://app.roboflow.com para usar SAM 3 en Roboflow. |
| `META_SAM31_API_KEY` | vacío | Una clave aparte solo para SAM 3.1, si la tienes. |
| `MAPS_PROVIDER` | `Osm` | El mapa que se muestra primero en Mantenimiento: `Osm` (OpenStreetMap) o `Google`. El usuario puede cambiarlo en la aplicación. |
| `OSRM_URL` | servidor público de OSRM | Rutas por calles sobre OpenStreetMap. En producción apúntalo a tu propio servidor OSRM. |
| `MAP_TILE_URL` | `tile.openstreetmap.org` | Teselas del mapa (`{z}/{x}/{y}`). En producción usa tu propio servidor de teselas o un proveedor. |
| `GOOGLE_MAPS_API_KEY` | vacío | Opcional. Activa el mapa de Google y Google Routes. Sin ella todo funciona con OpenStreetMap y no se envía nada a Google. |
| `GOOGLE_MAPS_BROWSER_KEY` | vacío | Clave aparte opcional para dibujar el mapa de Google; por defecto usa la anterior. |

Cualquier otra opción de `backend/src/PaveLens.Api/appsettings.json` también se puede poner en `.env`, escribiendo `__` en lugar de `:` (por ejemplo `Ai__Meta__Reasoning__TimeoutSeconds=120`). Orden de prioridad: variables de entorno reales, luego `.env`, luego `dotnet user-secrets`, luego `appsettings.json`.

Qué pasa si falta un servicio:

| Si falta | Qué obtienes |
|---|---|
| La clave de Meta | Modo demostración: toda la aplicación funciona con resultados fijos y rotulados. |
| El ayudante de SAM 3.1 | El script de arranque lo instala (`backend/tools/sam31`, necesita Node). A mano: `npm ci --prefix backend/tools/sam31`. |
| Internet para las teselas | La ruta de mantenimiento se planifica igual; el área del mapa muestra un aviso. |
| OSRM no responde | La ruta usa distancias en línea recta a 22 km/h y lo indica. |
| Meta devuelve un plan inválido | Se usa el orden calculado por el código, con un aviso. |
| La clave de Google | Solo OpenStreetMap; el botón de Google queda desactivado. |

### Protege tus claves

- Las claves van **solo** en `.env`: nunca en `appsettings.json`, en el código, en un issue ni en una captura. `git add .` nunca incluye `.env`.
- El navegador nunca recibe la clave de Meta. Las llamadas a Meta, OSRM y Google Routes pasan por la API de PaveLens, que limita cuántas consultas puede hacer cada usuario; además las rutas se guardan en caché y Google tiene un tope diario.
- La clave de navegador de Google solo se entrega a las páginas de la propia aplicación. Aun así se puede ver en el tráfico de red de la página, así que **restríngela en Google Cloud Console**: por sitio web (`http://localhost:4200/*` y tu dominio) y a la *Maps JavaScript API*.
- Si una clave llega a exponerse, revócala (https://dev.meta.ai, Google Cloud Console) y crea otra.

## Solución de problemas

| Síntoma | Solución |
|---|---|
| `start.ps1 cannot be loaded because running scripts is disabled` | Ejecútalo como arriba: `powershell -ExecutionPolicy Bypass -File .\start.ps1`. |
| `A compatible .NET SDK was not found` | Instala el SDK de .NET 10. Compruébalo con `dotnet --list-sdks`. |
| `The Angular CLI requires a minimum Node.js version` | Instala Node 22.22+ o 24 LTS y abre una terminal nueva. |
| Falla `npm ci` | Revisa tu conexión a internet, borra `frontend/node_modules` y vuelve a ejecutar el script. |
| Puerto 5000 o 4200 ocupado (en macOS, casi siempre AirPlay) | Cierra el otro programa, o arranca la API con `--urls http://localhost:5055` y cambia el puerto en `frontend/proxy.conf.json`. |
| La barra superior dice *Sin conexión* | La API no está corriendo o aún está arrancando. Revisa su ventana por si hay un error. |
| Sigue en *Modo demostración* tras poner la clave | Reinicia la API. Comprueba que el archivo se llame exactamente `.env` (no `.env.txt`) y esté junto a `README.md`. En Windows activa *Vista → Extensiones de nombre de archivo* para verlo. |
| La API se detiene con `Ai:Meta:...:ApiKey es obligatorio` | `PAVELENS_AI_PROVIDER=Meta` o el servicio elegido necesitan una clave que está vacía en `.env`. |
| Los modelos reales responden 401/403 | La clave es incorrecta o la cuenta no tiene acceso a ese modelo. |
| SAM 3.1: no se encuentra `node` o `decode.mjs` | Ejecuta `npm ci --prefix backend/tools/sam31` y comprueba que `node` esté en el PATH. |
| Un análisis falla con *… no encontró deterioros en la foto* | SAM no vio daño. Es lo esperado con las escenas dibujadas en modo Meta. Con tu propia foto, acércate, usa luz pareja y deja el daño en la mitad inferior del encuadre. |
| El mapa de Mantenimiento muestra teselas desordenadas o no aparece | Recarga la página. Si arrancas la web a mano, vuelve a ejecutar `npm start` después de traer cambios nuevos. |
| Empezar de cero con los datos de demostración | Detén la API y borra `backend/src/PaveLens.Api/App_Data/`. Se recrea en el siguiente arranque. |

Hay más casos en [docs/INSTALACION.es.md](docs/INSTALACION.es.md).

## Estructura

```
PaviLens/
├── backend/                         .NET 10, Clean Architecture
│   ├── PaveLens.slnx
│   ├── src/
│   │   ├── PaveLens.Domain/             Inspección, deterioros, geometría validada
│   │   ├── PaveLens.Application/        Proyección, PCI, metrado, presupuesto, tránsito, ruta de mantenimiento
│   │   ├── PaveLens.Infrastructure/     EF Core (SQLite/PostgreSQL), cola + worker, adaptadores Meta/Roboflow, OSRM/Google
│   │   └── PaveLens.Api/                Minimal API /api/v1, ProblemDetails, límites de consultas
│   ├── tests/                       Pruebas unitarias y de integración (xUnit v3)
│   ├── tools/sam31/                 Pequeño ayudante en Node que decodifica las máscaras de SAM 3.1
│   └── samples/                     Escenas de demostración y su generador
├── frontend/                        Angular 22 (componentes standalone, signals), ES/EN
├── docs/                            Guías, capturas y el video de demostración
├── .env.example                     Plantilla de configuración: cópiala como .env
└── start.ps1 / start.sh             Comprueban requisitos, crean .env, instalan y arrancan
```

## Pruebas

175 pruebas de backend (unitarias y de integración) y 19 de frontend. Las del backend se ejecutan desde `backend/`, donde está `global.json`:

```bash
cd backend
```

```bash
dotnet test --solution PaveLens.slnx
```

Desde la raíz del repositorio, las del frontend:

```bash
npm --prefix frontend test -- --watch=false
```

Las pruebas nunca llaman a Meta, Google ni OSRM: usan dobles de prueba, así que corren sin internet y sin costo.

## Documentación

- [docs/INSTALACION.es.md](docs/INSTALACION.es.md): instalación paso a paso, todas las opciones y solución de problemas ([in English](docs/GETTING_STARTED.md)).
- [docs/USER_GUIDE.md](docs/USER_GUIDE.md): cómo usar cada pantalla, de la foto al presupuesto y el desvío (en inglés).
- [backend/README.md](backend/README.md): referencia de la API y configuración del backend.
- [backend/docs/ARCHITECTURE.md](backend/docs/ARCHITECTURE.md): capas, pipeline, modelos de ingeniería y almacenamiento.
- [backend/docs/ORCHESTRATION.md](backend/docs/ORCHESTRATION.md): orquestación de agentes, perfiles regionales y mapa de ciudades.
- [backend/docs/ai-contracts.md](backend/docs/ai-contracts.md): contratos con SAM 3 y Muse Wonder.
- [docs/ORQUESTACION_AGENTICA_META.es.md](docs/ORQUESTACION_AGENTICA_META.es.md): cómo trabajan los agentes de Meta, en español.

## Hoja de ruta

Aún no implementado:

- Inspección por video con SAM 3.1 (seguimiento de máscaras entre cuadros) y agregación por unidad de muestreo.
- Desvíos con equilibrio de Wardrop sobre calles reales de OpenStreetMap (hoy corre sobre la red de demostración del centro de Puno; el Mapa de ciudades ya muestra calles reales con desvíos de referencia).
- Validación de la segmentación con los conjuntos Crack500 y RDD2022.

## Autor

**Carlos Turpo (CHARLS362)**
- Repositorio oficial: [github.com/CHARLS362/PaviLens](https://github.com/CHARLS362/PaviLens)
- Desarrollado para el **Meta Global AI Developer Hackathon 2026** (Indie Developer Track).

## Licencia

[MIT](LICENSE) · Copyright (c) 2026 Carlos Turpo (CHARLS362)
