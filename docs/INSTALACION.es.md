# Instalación y ejecución

De un clon recién descargado a PaveLens funcionando en unos cinco minutos. El modo demostración no necesita claves ni tiene costo. Los modelos reales de Meta necesitan una o dos claves en un archivo `.env`.

*In English: [GETTING_STARTED.md](GETTING_STARTED.md)*

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
4. **Nueva inspección** → *Prueba con una escena de muestra* → **Analizar**: el flujo completo se ejecuta otra vez y el panel de agentes muestra cada llamada a herramienta.
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
PAVELENS_AI_PROVIDER=Meta
META_API_KEY=<tu clave de la API de Meta>
PAVELENS_SEGMENTATION_SERVICE=Roboflow
ROBOFLOW_API_KEY=<tu clave de Roboflow>
```

| Variable | Para qué sirve | Dónde obtenerla |
|---|---|---|
| `PAVELENS_AI_PROVIDER` | `Simulation` (demostración) o `Meta` (modelos reales) | — |
| `META_API_KEY` | Muse Wonder: diagnóstico, asistente, plan de tránsito y coordinador de agentes. También SAM 3.1 cuando el servicio de segmentación es `Meta`. | https://dev.meta.ai |
| `PAVELENS_SEGMENTATION_SERVICE` | `Roboflow` (SAM 3, por defecto), `Meta` (SAM 3.1 directo) o `Sidecar` | — |
| `ROBOFLOW_API_KEY` | SAM 3 en la API serverless de Roboflow | https://app.roboflow.com → Settings → API Keys |
| `META_SAM31_API_KEY` | Opcional: otra clave solo para SAM 3.1 | https://dev.meta.ai |
| `META_REASONING_MODEL` | Opcional: nombre del modelo, por defecto `muse-wonder-1.0` | — |

Dos configuraciones que funcionan:

- **Meta + Roboflow** (dos claves): `PAVELENS_SEGMENTATION_SERVICE=Roboflow`, `META_API_KEY` y `ROBOFLOW_API_KEY`.
- **Solo Meta** (una clave, SAM 3.1 directo): `PAVELENS_SEGMENTATION_SERVICE=Meta` y `META_API_KEY`. Además necesita instalar una vez el decodificador de máscaras. `start.ps1` y `start.sh` lo instalan solos; a mano:

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

Con `PAVELENS_AI_PROVIDER=Meta`, la API **no arranca** si falta una clave obligatoria, e indica cuál. Nunca vuelve en silencio a resultados de demostración.

### Prioridad y otras formas de dar las claves

Gana la de más arriba:

1. argumentos de línea de comandos;
2. variables de entorno reales, por ejemplo `Ai__Meta__Reasoning__ApiKey` (para contenedores y CI);
3. `.env`;
4. `dotnet user-secrets`;
5. `appsettings.json` (aquí nunca van secretos).

Cualquier clave de configuración puede ir en `.env` con `__` en lugar de `:`, por ejemplo `Ai__Meta__Reasoning__TimeoutSeconds=180` u `Orchestration__MaxToolCalls=12`. La lista completa está en [backend/README.md](../backend/README.md) y [backend/docs/ORCHESTRATION.md](../backend/docs/ORCHESTRATION.md).

## 6. Ejecutar las pruebas

```bash
dotnet test --solution backend/PaveLens.slnx
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
| Empezar de cero con datos de demostración limpios | Detén la API y borra `backend/src/PaveLens.Api/App_Data/`; se recrea en el siguiente arranque. |
