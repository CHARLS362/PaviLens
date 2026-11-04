# Instalación y ejecución

De un clon recién descargado a PaveLens funcionando en unos cinco minutos. El modo demostración no necesita claves ni tiene costo. Los modelos reales de Meta necesitan una o dos claves en un archivo `.env`.

*In English: [GETTING_STARTED.md](GETTING_STARTED.md)* · Cuando ya funcione, la **[guía de uso](USER_GUIDE.md)** (en inglés) explica cada pantalla.

## Camino rápido: solo tu clave de Meta

Si ya tienes Git, el SDK de .NET 10 y Node.js 22.22+ (ver [Requisitos](#1-requisitos)), estos son los únicos pasos:

**1. Clonar el repositorio**

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

**2. Crear `.env` y pegar tu clave de Meta.** Copia la plantilla:

Windows (PowerShell):

```powershell
Copy-Item .env.example .env
```

macOS / Linux:

```bash
cp .env.example .env
```

Abre `.env` con cualquier editor de texto, busca la línea `META_API_KEY=` y pega tu clave después del `=`, sin espacios ni comillas:

```dotenv
META_API_KEY=pega-aqui-tu-clave-de-meta
```

Guarda el archivo y deja las demás líneas como están. ¿Aún no tienes clave? Créala en https://dev.meta.ai. Si te saltas este paso, PaveLens corre en **modo demostración**, gratis.

> 🔒 Git ignora `.env`, así que tu clave nunca llega a GitHub. No la pegues en `appsettings.json` ni en el código. Si se expone, revócala en https://dev.meta.ai y crea otra.

**3. Arrancar PaveLens**

Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

La primera vez instala los paquetes (de 1 a 3 minutos). En Windows se abren dos ventanas nuevas, una para la API y otra para la web. Déjalas abiertas mientras uses PaveLens: cerrarlas lo detiene.

**4. Abrir http://localhost:4200.** La barra superior debe mostrar los modelos de Meta, por ejemplo *SAM 3.1 · Muse Wonder 1.0*, en lugar de *Modo demostración*. Pulsa **Nueva inspección**, sube una foto real de una pista, fija la escala y pulsa **Analizar**. La [guía de uso](USER_GUIDE.md) recorre cada paso.

> Con los modelos reales de Meta, sube una **foto real de una pista**. Las tres escenas de muestra son dibujos: SAM suele no encontrar daños en ellas y el análisis termina como *Falló* con ese motivo. Están pensadas para el modo demostración.

El resto de la página explica cada paso en detalle, las demás opciones de configuración y los problemas frecuentes.

## 1. Requisitos

| Herramienta | Versión | Comprobar | Descarga |
|---|---|---|---|
| Git | cualquiera | `git --version` | https://git-scm.com |
| SDK de .NET | 10.0.100 o posterior | `dotnet --list-sdks` | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Node.js | 22.22+ o 24 LTS (incluye npm) | `node --version` | https://nodejs.org |

No hace falta servidor de base de datos, Docker ni GPU: la API usa SQLite en un archivo local y los modelos de Meta se consumen por API.

## 2. Clonar

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

## 3. Ejecutar

### Opción A: un solo comando

Windows (PowerShell):

```powershell
.\start.ps1
```

Si PowerShell no permite ejecutar scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

El script:
1. comprueba .NET 10 y Node;
2. crea `.env` a partir de `.env.example` la primera vez (modo demostración);
3. instala los paquetes del frontend la primera vez (`npm ci`, alrededor de un minuto);
4. arranca la API y la aplicación web.

### Opción B: a mano, en dos terminales

Terminal 1, la API:

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

### Abrir

| Qué | URL |
|---|---|
| Aplicación web | http://localhost:4200 |
| Referencia de la API (Scalar) | http://localhost:5000/scalar |
| Salud | http://localhost:5000/health |
| Proveedor de IA activo | http://localhost:5000/api/v1/ai/status |

En el primer arranque la API crea la base SQLite (`backend/src/PaveLens.Api/App_Data/pavelens.db`) y carga tres inspecciones de demostración: Jr. Lima (foto oblicua calibrada), Av. El Sol (bache) y Jr. Arequipa (piel de cocodrilo).

## 4. Probar (modo demostración)

1. Elige una inspección a la izquierda: la foto muestra cada deterioro delineado y medido en metros, con el PCI y los metrados.
2. Pulsa un código en el **registro de deterioros** (panel derecho): la foto se acerca a él.
3. Abre la pestaña **Presupuesto**: análisis de precios unitarios calculado por código a partir de las áreas medidas.
4. **Nueva inspección** → *Prueba con una escena de muestra* → **Analizar**: el flujo completo se ejecuta otra vez y el panel de agentes muestra cada llamada a herramienta. (Las escenas de muestra son para el modo demostración; con los modelos reales de Meta, sube una foto real).
5. **Planificación de desvíos** (barra superior): cierra un carril en la red de demostración de Puno y compara franjas horarias.
6. **ES / EN** cambia el idioma; **Accesibilidad** ajusta tamaño de texto, contraste, movimiento y una paleta para daltonismo.

En modo demostración los resultados vienen de escenarios fijos y cada pantalla indica *Modo demostración*.

## 5. Usar los modelos reales de Meta (`.env`)

Todas las claves van en **un solo archivo: `.env` en la raíz del repositorio** (junto a `README.md`). Git lo ignora, así que las claves nunca llegan a GitHub. Si el script no lo creó:

```powershell
Copy-Item .env.example .env
```

```bash
cp .env.example .env
```

Abre `.env` y completa:

```dotenv
META_API_KEY=<tu clave de la API de Meta>
```

Cualquier clave de la API de Meta funciona sola. Con los valores por defecto (`Auto`) la API pasa a los modelos reales, segmenta con SAM 3.1 en Meta usando la misma clave y, al arrancar, lista los modelos que permite la clave (`GET /v1/models`) para elegir los mejores: el Muse Wonder más nuevo, si no el Muse Spark más nuevo, y el SAM más nuevo. Nunca elige modelos `-contributor`, que comparten datos para entrenamiento. El registro de arranque y `GET /api/v1/ai/status` muestran los modelos elegidos.

| Variable | Para qué sirve | Dónde obtenerla |
|---|---|---|
| `PAVELENS_AI_PROVIDER` | `Auto` (por defecto: `Meta` si hay `META_API_KEY`, si no demostración), `Simulation` o `Meta` | — |
| `META_API_KEY` | Muse Wonder: diagnóstico, asistente, plan de tránsito y coordinador de agentes. También SAM 3.1 cuando el servicio de segmentación es `Meta`. | https://dev.meta.ai |
| `PAVELENS_SEGMENTATION_SERVICE` | `Auto` (por defecto: `Roboflow` si hay `ROBOFLOW_API_KEY`, si no `Meta`), `Roboflow` (SAM 3), `Meta` (SAM 3.1 directo) o `Sidecar` | — |
| `ROBOFLOW_API_KEY` | SAM 3 en la API serverless de Roboflow | https://app.roboflow.com → Settings → API Keys |
| `META_SAM31_API_KEY` | Opcional: otra clave solo para SAM 3.1 | https://dev.meta.ai |
| `META_REASONING_MODEL` | Opcional: un modelo fijo; vacío o `auto` elige el mejor disponible | — |

Dos configuraciones que funcionan:

- **Solo Meta** (una clave, SAM 3.1 directo, la opción por defecto): solo `META_API_KEY`.
- **Meta + Roboflow** (dos claves): `META_API_KEY` y `ROBOFLOW_API_KEY`.

SAM 3.1 directo necesita instalar una vez el decodificador de máscaras. `start.ps1` y `start.sh` lo instalan solos; a mano:

  ```bash
  cd backend/tools/sam31
  ```

  ```bash
  npm ci
  ```

Reinicia la API después de editar `.env`. Luego comprueba:

- el registro de la API muestra `Configuration: ...\.env | AI provider: Meta`;
- http://localhost:5000/api/v1/ai/status indica `Meta` con las claves configuradas;
- la insignia *Modo demostración* de la barra superior cambia por los nombres de los modelos.

En modo Meta, la API **no arranca** si falta una clave obligatoria, e indica cuál. Nunca vuelve en silencio a resultados de demostración.

### Prioridad y otras formas de dar las claves

Gana la de más arriba:

1. argumentos de línea de comandos;
2. variables de entorno reales, por ejemplo `Ai__Meta__Reasoning__ApiKey` (para contenedores y CI);
3. `.env`;
4. `dotnet user-secrets`;
5. `appsettings.json` (aquí nunca van secretos).

Cualquier clave de configuración puede ir en `.env` con `__` en lugar de `:`, por ejemplo `Ai__Meta__Reasoning__TimeoutSeconds=180` u `Orchestration__MaxToolCalls=12`. La lista completa está en [backend/README.md](../backend/README.md) y [backend/docs/ORCHESTRATION.md](../backend/docs/ORCHESTRATION.md).

## 6. Ejecutar las pruebas

Ejecuta las pruebas desde `backend/` porque `global.json` está allí:

```bash
cd backend
```
```bash
dotnet test --solution PaveLens.slnx
```

```bash
cd frontend
```

```bash
npm test -- --watch=false
```

Las pruebas siempre corren en modo demostración e ignoran `.env`, así que nunca gastan créditos.

## 7. Problemas frecuentes

| Síntoma | Solución |
|---|---|
| `No se puede cargar start.ps1 porque la ejecución de scripts está deshabilitada` | `powershell -ExecutionPolicy Bypass -File .\start.ps1` |
| `A compatible .NET SDK was not found` | Instala el SDK de .NET 10 (`global.json` pide 10.0.100 o posterior). |
| `The Angular CLI requires a minimum Node.js version` | Instala Node 22.22+ o 24 LTS. |
| El puerto 5000 o 4200 ya está en uso | Cierra el otro proceso, o arranca la API con `--urls http://localhost:5055` y ajusta `frontend/proxy.conf.json`. |
| La web dice *Sin conexión* | La API no está corriendo o aún está arrancando; revisa su ventana. |
| La API se cierra con `Ai:Meta:...:ApiKey es obligatorio` | Falta una clave en `.env` para el proveedor o servicio elegido. |
| Los modelos reales responden 401/403 | La clave es incorrecta o la cuenta no tiene acceso al modelo; pruébala en la consola del proveedor. |
| SAM 3.1: no encuentra `node` o `decode.mjs` | Ejecuta `npm ci` en `backend/tools/sam31` y verifica que `node` esté en el PATH. |
| Una inspección falla con *… no encontró deterioros en la foto* | SAM no vio daños. Es lo esperado con las escenas dibujadas en modo Meta. Con tu propia foto, acércate más, usa luz pareja y deja el daño en la mitad inferior del encuadre. Ver la [guía de uso](USER_GUIDE.md#9-taking-good-photos). |
| La barra superior sigue diciendo *Modo demostración* después de poner la clave | La API lee `.env` solo al arrancar: cierra la ventana de la API y arráncala otra vez. Comprueba que el archivo se llame exactamente `.env` (no `.env.txt`) y esté junto a `README.md`. En Windows, activa *Ver → Extensiones de nombre de archivo* para ver la extensión real. |
| Empezar de cero con datos de demostración limpios | Detén la API y borra `backend/src/PaveLens.Api/App_Data/`; se recrea en el siguiente arranque. |
