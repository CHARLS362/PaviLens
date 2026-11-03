# 📑 PaveLens AI: Planificación Técnica y Fundamentos de Ingeniería Civil, Matemática, Física Avanzada y Optimización de Redes Viales

> **Documento de Ingeniería Avanzada de Alto Impacto**  
> **Proyecto:** PaveLens AI 🛣️⚡ (*Autonomous Road Infrastructure Diagnostic, Structural Deformation, Cost Estimation & Smart Traffic Rerouting System*)  
> **Evento:** Meta Global AI Developer Hackathon — Indie Developer Track ($400,000 USD Prize Pool)  
> **Modelos Base:** Meta SAM 3 (Segment Anything with Concepts) & Meta Muse Wonder, consumidos por API  

---

## 1. Visión y Propósito del Sistema

La infraestructura vial representa el mayor activo físico de las naciones, con más de 64 millones de kilómetros de carreteras en el mundo. Sin embargo, su mantenimiento se gestiona con inspecciones visuales manuales subjetivas, levantamientos topográficos lentos y desvíos de tránsito improvisados que provocan congestión masiva y sobrecostos millonarios.

**PaveLens AI** transforma la conservación vial mediante una tubería analítica automatizada de cuatro fases:
1. **Percepción por concepto (Meta SAM 3):** Segmentación por píxel de siluetas de deterioro no convexo (baches, piel de cocodrilo, roderas, fisuras en bloque), descartando aproximaciones toscas tipo *bounding box*.
2. **Cubicaje e Índice de Condición (ASTM D6433):** Extracción de área ($m^2$) desde la máscara; volumen ($m^3$) y masa asfáltica compactada (toneladas) a partir de la profundidad medida por el inspector (una sola foto monocular no permite medir profundidad, ver §4.D); índice de condición PCI.
3. **Mantenimiento y Análisis de Precios Unitarios (APU - EG-2013):** Cálculo automatizado de presupuestos de remediación considerando cortes perimetrales, riego de liga, mezcla asfáltica en caliente y compactación.
4. **Resiliencia de Red y Desvío Dinámico de Tráfico (Teoría de Redes BPR & Wardrop Equilibrium):** Modelado de la pérdida de capacidad del arco vial y cálculo algorítmico del desvío vehicular óptimo para mitigar la congestión urbana.

---

## 2. Referencias Científicas y Normativas

PaveLens AI se apoya en literatura revisada por pares y en normas técnicas verificables. Cada referencia indica qué aporta al sistema. No se citan factores de impacto ni cuartiles, porque cambian cada año y dependen del índice consultado.

### A. Visión por computador y segmentación

| Referencia | Aporte a PaveLens AI |
|---|---|
| Kirillov, A. et al. (2023). *Segment Anything*. ICCV 2023. arXiv: [2304.02643](https://arxiv.org/abs/2304.02643) | Modelo base de segmentación con *prompts* (familia SAM) |
| Ravi, N. et al. (2024). *SAM 2: Segment Anything in Images and Videos*. arXiv: [2408.00714](https://arxiv.org/abs/2408.00714) | Segmentación en video y propagación de máscaras espaciotemporales |
| Owor, N. J., Adu-Gyamfi, Y., Aboah, A., & Amo-Boateng, M. (2024). *PaveSAM – segment anything for pavement distress*. Road Materials and Pavement Design. DOI: [10.1080/14680629.2024.2374863](https://doi.org/10.1080/14680629.2024.2374863), arXiv: [2409.07295](https://arxiv.org/abs/2409.07295) | Adaptación de SAM a patologías viales mediante ajuste fino eficiente del decodificador de máscaras |
| Yang, F. et al. (2020). *Feature Pyramid and Hierarchical Boosting Network for Pavement Crack Detection*. IEEE Transactions on Intelligent Transportation Systems, 21(4), 1525–1535. DOI: [10.1109/TITS.2019.2910595](https://doi.org/10.1109/TITS.2019.2910595) | Línea base de segmentación de fisuras; publica los benchmarks Crack500 y GAPs384 |
| Eisenbach, M. et al. (2017). *How to Get Pavement Distress Detection Ready for Deep Learning? A Systematic Approach*. IJCNN 2017. DOI: [10.1109/IJCNN.2017.7966099](https://doi.org/10.1109/IJCNN.2017.7966099) | Dataset GAPs y protocolo de evaluación estandarizado |

### B. Datasets de deterioro vial

| Referencia | Aporte a PaveLens AI |
|---|---|
| Arya, D. et al. (2021). *RDD2020: An Annotated Image Dataset for Automatic Road Damage Detection Using Deep Learning*. Data in Brief, 36, 107133. DOI: [10.1016/j.dib.2021.107133](https://doi.org/10.1016/j.dib.2021.107133) | Taxonomía D00/D10/D20/D40 y banco de validación multi-país |
| Arya, D. et al. (2022). *RDD2022: A Multi-National Image Dataset for Automatic Road Damage Detection*. arXiv: [2209.08538](https://arxiv.org/abs/2209.08538) | Ampliación multinacional (6 países, 47,000+ imágenes) para calibrar umbrales |

### C. Ingeniería de pavimentos y tráfico

| Referencia | Aporte a PaveLens AI |
|---|---|
| ASTM D6433 - 20. *Standard Practice for Roads and Parking Lots Pavement Condition Index Surveys*. ASTM International | Metodología oficial de cálculo del índice PCI (§5.A) |
| MTC Perú (2013). *Especificaciones Técnicas Generales para Construcción, EG-2013*. R.D. N° 22-2013-MTC/14 | Partidas de bacheo, riego de liga y mezcla asfáltica en caliente (§5.B) |
| MTC Perú (2014). *Manual de Carreteras: Conservación Vial*. R.D. N° 08-2014-MTC/14 | Catálogo de deterioros y niveles de servicio en contratos de conservación vial |
| Bureau of Public Roads (1964). *Traffic Assignment Manual*. U.S. Department of Commerce | Función de demora BPR para redes viales (§6.A) |
| Wardrop, J. G. (1952). *Some Theoretical Aspects of Road Traffic Research*. Proceedings of the Institution of Civil Engineers, Part II, 1(3), 325–378. DOI: [10.1680/ipeds.1952.11259](https://doi.org/10.1680/ipeds.1952.11259) | Principio de equilibrio de usuario en redes viales (§6.B) |
| Beckmann, M., McGuire, C. B. & Winsten, C. B. (1956). *Studies in the Economics of Transportation*. Yale University Press | Formulación convexa del equilibrio de tráfico |
| Frank, M. & Wolfe, P. (1956). *An Algorithm for Quadratic Programming*. Naval Research Logistics Quarterly, 3(1–2), 95–110. DOI: [10.1002/nav.3800030109](https://doi.org/10.1002/nav.3800030109) | Algoritmo de optimización cuadrática para asignación de flujos |
| LeBlanc, L. J., Morlok, E. K. & Pierskalla, W. P. (1975). *An Efficient Approach to Solving the Road Network Equilibrium Traffic Assignment Problem*. Transportation Research, 9(5), 309–318. DOI: [10.1016/0041-1647(75)90030-1](https://doi.org/10.1016/0041-1647(75)90030-1) | Aplicación de Frank-Wolfe a la asignación de tráfico a gran escala |

### Síntesis de Aportes Integrados en el Proyecto:
1. **Segmentación sin entrenamiento específico:** Los modelos de la familia SAM segmentan deterioros a partir de *prompts* de texto o puntos, sin entrenar una red cerrada por tipo de asfalto. La precisión real sobre pavimento se mide contra Crack500, GAPs384 y RDD2022 antes de afirmarla.
2. **De la detección al mantenimiento:** La mayoría de los trabajos académicos terminan en la métrica de visión artificial. PaveLens AI conecta la máscara con el área real, el PCI, el cubicaje, el presupuesto y el impacto en el tráfico.

---

## 3. Repositorios Mundiales y Datasets Benchmark de Referencia

PaveLens AI se evalúa y calibra contra los repositorios de código abierto y datasets benchmark más rigurosos a nivel global:

### A. Repositorios Top Globales en GitHub (Modelos de Visión, Patología Vial y Redes de Transporte)

| Repositorio GitHub | Organización / Autores | Estado / Ecosistema | Aporte a PaveLens AI |
|---|---|:---:|---|
| [facebookresearch/segment-anything](https://github.com/facebookresearch/segment-anything) | Meta AI Research | ~48,000 ⭐ | Arquitectura oficial de base SAM (ViT Encoder + Light Mask Decoder) para segmentación orgánica |
| [facebookresearch/sam2](https://github.com/facebookresearch/sam2) | Meta AI Research | ~16,000 ⭐ | Segmentación continua en tiempo real y seguimiento de máscaras en transmisiones de video vehicular |
| [sekilab/RoadDamageDetector](https://github.com/sekilab/RoadDamageDetector) | Sekimoto Lab (Univ. de Tokio) | Benchmark Global CRDDC | Repositorio oficial que centraliza los datasets y utilidades de RDD2020 y RDD2022 |
| [fyangneil/pavement-crack-detection](https://github.com/fyangneil/pavement-crack-detection) | F. Yang et al. (Temple Univ.) | Referencia Académica | Código fuente de FPHB (Feature Pyramid Hierarchical Boosting) y benchmark del dataset Crack500 |
| [KG-TSI-Civil/CrackSAM](https://github.com/KG-TSI-Civil/CrackSAM) | KG-TSI Civil Research | Especializado | Adaptación de SAM para segmentación fina de fisuras en estructuras civiles y asfalto |
| [bstabler/TransportationNetworks](https://github.com/bstabler/TransportationNetworks) | B. Stabler, H. Bar-Gera & E. Sall | Benchmark Mundial TAP | Redes estándar viales (Sioux Falls, Chicago, Anaheim) en formato `.tntp`/CSV para BPR y Wardrop |
| [AequilibraE/aequilibrae](https://github.com/AequilibraE/aequilibrae) | AequilibraE Developers | Python Transportation | Framework completo de asignación de tráfico por equilibrio de usuario con Frank-Wolfe |
| [gboeing/osmnx](https://github.com/gboeing/osmnx) | Geoff Boeing | ~6,000 ⭐ | Extracción, modelado topológico y cálculo de rutas sobre grafos viales reales de OpenStreetMap |

### B. Trabajo Futuro: Orquestación con Agentes (no implementado)

> **Estado:** nada de esta sección está en el código actual. Hoy el flujo es un pipeline determinista (SAM 3 → geometría → Muse Wonder → PCI, cubicaje, APU), lo que lo hace auditable y fácil de probar. Los proyectos siguientes son referencias para una versión futura en la que municipios y universidades puedan aportar sus propios módulos normativos o conectores de datos. Las cifras de estrellas son aproximadas y cambian con el tiempo.

| Prioridad | Idea | Por qué |
|---|---|---|
| 1 | Exponer PaveLens como servidor **MCP** (consultar inspecciones, simular cierres) | Cualquier agente o asistente podría usarlo sin integración a medida |
| 2 | Agente de priorización con **Llama Stack** sobre varias inspecciones | Plan anual de conservación con presupuesto limitado, citando PCI y costos calculados |
| 3 | Revisión humana en el bucle (**LangGraph**) antes de aprobar un presupuesto | El inspector confirma profundidad y severidad antes de emitir el expediente |

Referencias consultadas:

| Repositorio GitHub | Organización / Autores | Enfoque Tecnológico | Aporte y Rol en la Arquitectura de PaveLens AI |
|---|---|:---:|---|
| [meta-llama/llama-stack](https://github.com/meta-llama/llama-stack) | Meta AI | Estándar Oficial de Agentes Meta | Framework oficial de Meta para construir agentes autónomos con modelos Llama: define APIs unificadas de inferencia, memoria, *tool-calling* y salvaguardas (*Llama Guard*), permitiendo orquestar agentes inspectores en el borde (*edge*). |
| [langchain-ai/langgraph](https://github.com/langchain-ai/langgraph) | LangChain AI | Grafos Cíclicos y Máquinas de Estado | Orquestador multi-agente con persistencia de estado y soporte de ciclos: modela el flujo secuencial de ingeniería civil (*Percepción SAM → Validación Geométrica → Análisis Geomecánico → Presupuesto APU → Aprobación Humana en Bucle*). |
| [microsoft/autogen](https://github.com/microsoft/autogen) | Microsoft Research (~37k ⭐) | Conversación y Cooperación Multi-Agente | Marco de trabajo para debate y consenso entre agentes especializados: permite simular la interacción entre un *Surveyor Agent* (evaluador visual), un *Materials Engineer Agent* (cálculo de mezclas) y un *Traffic Dispatcher Agent* (desvíos). |
| [crewAIInc/crewAI](https://github.com/crewAIInc/crewAI) | CrewAI Team (~26k ⭐) | Equipos Agénticos Basados en Roles | Orquestación basada en perfiles profesionales con delegación autónoma de tareas, ideal para despachar cuadrillas municipales y generar órdenes de compra automatizadas bajo normativa regional. |
| [modelcontextprotocol/servers](https://github.com/modelcontextprotocol/servers) | Anthropic & Linux Foundation | Protocolo Estándar de Contexto (MCP) | Protocolo universal abierto para conectar agentes de IA con fuentes de datos externas: permite a PaveLens interactuar con servidores de bases de datos espaciales (PostGIS), catastro urbano (OpenStreetMap/GIS) y sensores IoT de vehículos de transporte. |
| [All-Hands-AI/OpenHands](https://github.com/All-Hands-AI/OpenHands) | OpenHands Community (~45k ⭐) | Agentes Autónomos de Software | Plataforma abierta de agentes capaces de ejecutar código, auditar pruebas de calibración fotogramétrica y mantener pipelines de CI/CD para modelos de visión en infraestructura. |

#### 🌐 Arquitectura propuesta (no implementada):
```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   PAVELENS AGENTIC ECOSYSTEM FOR HUMANITY                              │
├────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│                                     [ ORQUESTADOR MULTI-AGENTE ]                                       │
│                         (Llama Stack + LangGraph: Máquina de Estados Cíclicos)                         │
├────────────────────────────┬─────────────────────────────┬─────────────────────────────────────────────┤
│ 1. Perception Agent        │ 2. Geotechnical & Norm Agent│ 3. Dispatch & Mobility Agent                │
│ • Meta SAM 3 / 3.1 (video) │ • Muse Wonder               │ • Algoritmo Frank-Wolfe + BPR               │
│ • Segmentación por píxel   │ • Cálculo ASTM D6433 & APU  │ • Ruteo vehicular y mitigación de atascos   │
│ • Corrección fotogramétrica│ • Adaptación climática Puno │ • Exportar el plan de desvío (GeoJSON)      │
├────────────────────────────┴─────────────────────────────┴─────────────────────────────────────────────┤
│                                  [ CAPA DE HERRAMIENTAS Y CONECTORES MCP ]                              │
│         (Model Context Protocol: Conexión con PostGIS, OpenStreetMap, Sensores y Normas Locales)       │
└────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

* **Impacto para el Bien Común:** Este diseño modular permite que cualquier país, universidad o gobierno regional desarrolle y publique su propio *Normative Plugin Agent* (ej. Eurocódigos, normas AASHTO, MTC EG-2013) o conectores a bases de datos de obras públicas locales sin alterar el núcleo matemático del sistema.

### C. Datasets Benchmark de Deterioro Vial

* **RDD2020 / RDD2022 (Road Damage Dataset):**
  * *Origen:* Universidad de Tokio; base de los desafíos *Global Road Damage Detection Challenge* (IEEE BigData 2020) y *Crowdsensing-based Road Damage Detection Challenge* (IEEE BigData 2022).
  * *Cobertura:* RDD2020 reúne más de 26,000 imágenes de Japón, India y República Checa; RDD2022 supera las 47,000 imágenes al añadir Noruega, Estados Unidos y China.
  * *Clases estándar:* `D00` (fisura longitudinal), `D10` (fisura transversal), `D20` (piel de cocodrilo), `D40` (bache).
  * *Nota de ingeniería:* RDD está anotado con cuadros delimitadores (*bounding boxes*), no con máscaras; sirve para validar detección y clasificación en vías de múltiples países, mientras que la geometría del contorno se valida con Crack500.
* **Crack500:**
  * 500 imágenes de aproximadamente $2000 \times 1500$ píxeles tomadas con teléfono móvil en el campus de Temple University, con máscaras a nivel de píxel. Es el estándar de oro para validar el contorno y continuidad de microfisuras.
* **GAPs / GAPs384 (German Asphalt Pavement Distress):**
  * GAPs reúne 1,969 imágenes en escala de grises de carreteras federales alemanas de asfalto. GAPs384 es un subconjunto de 384 imágenes con anotación a nivel de píxel, usado como benchmark de segmentación de severidad alta.
* **Datasets comunitarios de baches (p. ej. Roboflow Universe / Kaggle Pothole-600):**
  * Útiles para pruebas rápidas de robustez ante oclusiones y sombras. Su calidad de anotación es variable y no se usan como referencia métrica única.

---

## 4. Marco Físico-Matemático Riguroso

```
┌───────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   SISTEMA MULTIFÍSICO INTEGRADO                                       │
├──────────────────────────────┬──────────────────────────────┬─────────────────────────────────────────┤
│ A. Visión & Fotogrametría    │ B. Geomecánica y Reología    │ C. Hidráulica & Clima                   │
│ • Transformada de Homografía │ • Modelo Viscoelástico Burgers│ • Ley de Darcy & Bombeo de Finos       │
│ • Fórmula de Área de Gauss   │ • Fatiga de mezcla (MEPDG)   │ • Ecuación de Clapeyron (Hielo-Deshielo)│
│ • Momentos de Área 2D        │ • Pérdida de PCI (ASTM D6433)│ • Radiación UV & Fragilización Térmica │
└──────────────────────────────┴──────────────────────────────┴─────────────────────────────────────────┘
```

---

### A. Fotogrametría Proyectiva y Corrección de Perspectiva Monocular

Las cámaras montadas en vehículos capturan el pavimento con un ángulo de inclinación $\theta$ (óptica oblicua), lo que genera una distorsión trapezoidal donde los objetos lejanos aparentan menor área por píxel que los cercanos.

Para transformar las coordenadas del plano de imagen $\mathbf{p} = [u, v, 1]^T$ al plano métrico del pavimento $\mathbf{P} = [X, Y, 1]^T$ a cota $Z=0$, se aplica una **Matriz de Homografía Plana $\mathbf{H} \in \mathbb{R}^{3 \times 3}$**. La cámara proyecta el plano como $\mathbf{p} \sim \mathbf{K} \left[ \mathbf{r}_1 \; \mathbf{r}_2 \; \mathbf{t} \right] \mathbf{P}$, por lo que la transformación inversa es:

$$\mathbf{P} \sim \mathbf{H} \, \mathbf{p}, \qquad \mathbf{H} = \left( \mathbf{K} \left[ \mathbf{r}_1 \quad \mathbf{r}_2 \quad \mathbf{t} \right] \right)^{-1}$$

En la práctica, $\mathbf{H}$ se estima con 4 puntos de referencia de distancia conocida sobre la calzada (ancho de carril, marcas viales o una mira) mediante DLT, sin necesidad de conocer $\mathbf{K}$, $\mathbf{r}$ y $\mathbf{t}$ por separado.

Donde:
* $\mathbf{K}$ es la matriz de calibración intrínseca de la cámara:
  $$\mathbf{K} = \begin{bmatrix} f_x & 0 & c_x \\ 0 & f_y & c_y \\ 0 & 0 & 1 \end{bmatrix}$$
* $\mathbf{r}_1, \mathbf{r}_2$ son las columnas de rotación del plano y $\mathbf{t}$ es el vector de traslación respecto a la superficie del asfalto.

Para tomas **cenitales** (cámara perpendicular al pavimento, p. ej. dron), basta la **Distancia de Muestreo de Terreno (*Ground Sampling Distance - GSD*)**, constante en toda la imagen:

$$\text{GSD}_x = \frac{H \cdot S_w}{f \cdot I_w}, \quad \text{GSD}_y = \frac{H \cdot S_h}{f \cdot I_h} \quad \left[\frac{\text{metros}}{\text{píxel}}\right]$$

Donde $H$ es la altura de la cámara, $S_w, S_h$ las dimensiones del sensor, $f$ la focal e $I_w, I_h$ la resolución en píxeles. En tomas **oblicuas** el GSD varía con la fila de la imagen y se usa la homografía anterior.

---

### B. Teorema de Gauss (Shoelace Formula) para Polígonos Complejos No Convexos

El borde extraído por SAM 3 está definido por un polígono simple no convexo $\mathcal{P} = \{ (x_1, y_1), (x_2, y_2), \dots, (x_n, y_n) \}$, con $(x_{n+1}, y_{n+1}) = (x_1, y_1)$. Los vértices se proyectan primero a metros (homografía o GSD); por el Teorema de Green en el plano, el área es la integral de línea sobre el contorno:

$$A = \frac{1}{2} \oint_{\partial \Omega} (x\, dy - y\, dx) = \frac{1}{2} \left| \sum_{i=1}^{n} (x_i y_{i+1} - x_{i+1} y_i) \right| \quad [m^2]$$

En toma cenital con coordenadas en píxeles, el resultado se multiplica por $\text{GSD}_x \cdot \text{GSD}_y$. En toma oblicua **no** basta con un factor: cada vértice debe proyectarse con $\mathbf{H}$ antes de sumar.

El centroide $(\bar{x}, \bar{y})$ del deterioro se calcula simultáneamente, usando el área con signo $A_s$ (sin valor absoluto):

$$\bar{x} = \frac{1}{6A_s} \sum_{i=1}^n (x_i + x_{i+1})(x_i y_{i+1} - x_{i+1} y_i), \quad \bar{y} = \frac{1}{6A_s} \sum_{i=1}^n (y_i + y_{i+1})(x_i y_{i+1} - x_{i+1} y_i)$$

---

### C. Momentos de Área de Segundo Orden y Excentricidad del Deterioro

Para discernir si el daño corresponde a un bache por socavación focal o a un ahuellamiento/fisura por canalización de cargas pesadas, se calcula el **tensor de segundos momentos centrales de área**. Los momentos se integran sobre la **superficie** del polígono (no sobre sus vértices: una suma por vértices dependería de cuántos puntos devuelva SAM y de la simplificación aplicada). Por el Teorema de Green, con $c_i = x_i y_{i+1} - x_{i+1} y_i$:

$$I_{xx} = \frac{1}{12} \sum_{i=1}^{n} c_i \left( y_i^2 + y_i y_{i+1} + y_{i+1}^2 \right), \quad I_{yy} = \frac{1}{12} \sum_{i=1}^{n} c_i \left( x_i^2 + x_i x_{i+1} + x_{i+1}^2 \right)$$

$$I_{xy} = \frac{1}{24} \sum_{i=1}^{n} c_i \left( x_i y_{i+1} + 2 x_i y_i + 2 x_{i+1} y_{i+1} + x_{i+1} y_i \right)$$

Normalizando por el área y trasladando al centroide:

$$\mathbf{\Sigma} = \begin{bmatrix} \mu_{20} & \mu_{11} \\ \mu_{11} & \mu_{02} \end{bmatrix}, \quad \mu_{20} = \frac{I_{yy}}{A_s} - \bar{x}^2, \quad \mu_{02} = \frac{I_{xx}}{A_s} - \bar{y}^2, \quad \mu_{11} = \frac{I_{xy}}{A_s} - \bar{x}\bar{y}$$

Calculando sus autovalores $\lambda_1 \ge \lambda_2 \ge 0$, se determinan:
* **Excentricidad Morfológica ($e$):**
  $$e = \sqrt{1 - \frac{\lambda_2}{\lambda_1}}$$
* **Orientación del Eje Principal ($\theta$)**, usando $\operatorname{atan2}$ para resolver el cuadrante:
  $$\theta = \frac{1}{2} \operatorname{atan2} \left( 2 \mu_{11},\; \mu_{20} - \mu_{02} \right)$$

*Criterio de Inferencia Civil (umbrales heurísticos iniciales, a calibrar con RDD2022/Crack500):*
* $e < 0.45$: Bache de colapso circular o localizado (desprendimiento por adherencia).
* $e \ge 0.85$ con $\theta \parallel \text{eje vial}$: Ahuellamiento (*rutting*) en las huellas de rodada por flujo plástico.
* $e \ge 0.85$ con $\theta \perp \text{eje vial}$: Fisura transversal por contracción térmica.

---

### D. Profundidad, Volumen y Masa de Mezcla a Reponer

Una fotografía monocular entrega el **área** del deterioro, pero no su **profundidad**: la tercera dimensión no es observable sin estereoscopía, LiDAR o una medición directa. PaveLens AI la obtiene así:

1. **Medición en campo (preferente):** el inspector ingresa la profundidad media $d$ medida con regla o wincha.
2. **Supuesto por severidad (respaldo):** si no se mide, se usa un valor por defecto por nivel de severidad, mostrado en la interfaz como *estimado* y editable.

Con la profundidad, el cubicaje sigue el criterio de corte rectangular de la §5.B (margen $m$ alrededor del contorno visible):

$$A_{\text{corte}} = (L + 2m)(W + 2m), \qquad V = A_{\text{corte}} \cdot d \quad [m^3]$$

$$M = V \cdot \rho_{\text{HMA}} \cdot (1 + f_d) \quad [t]$$

Donde $L, W$ son las dimensiones del rectángulo orientado según $\theta$ (§4.C), $\rho_{\text{HMA}} \approx 2.30\text{–}2.40\ t/m^3$ es la densidad de la mezcla compactada (dato del diseño Marshall de la obra) y $f_d$ el factor de desperdicio.

---

### E. Reología Asfáltica: Modelo Viscoelástico de Burgers

El asfalto no se comporta como un sólido elástico lineal puro ni como un fluido newtoniano puro, sino como un material viscoelástico dependiente del tiempo y de la temperatura. Bajo la carga cíclica del tráfico vehicular $\sigma(t)$, la deformación unitaria $\epsilon(t)$ responde al **Modelo de Burgers de 4 Parámetros** (Maxwell en serie con Kelvin-Voigt):

$$\epsilon(t) = \underbrace{\frac{\sigma_0}{E_1}}_{\text{Elástica instantánea}} + \underbrace{\frac{\sigma_0}{\eta_1} t}_{\text{Viscosa irrecuperable (Ahuellamiento)}} + \underbrace{\frac{\sigma_0}{E_2} \left( 1 - e^{-\frac{E_2}{\eta_2} t} \right)}_{\text{Viscoelástica retardada}}$$

Donde:
* $E_1$: Módulo de rigidez elástica instantánea del esqueleto mineral.
* $\eta_1$: Viscosidad del ligante bituminoso libre (causante del bache y la huella plástica acumulativa).
* $E_2, \eta_2$: Módulos de relajación viscoelástica interna.
* $\tau = \eta_2 / E_2$: Tiempo de relajación de tensiones.

*Alcance en PaveLens AI:* los cuatro parámetros se obtienen en laboratorio (ensayo de creep) y no son medibles desde una fotografía. El modelo se usa de forma **explicativa** (curva de deformación para un ligante de referencia), no como predicción calibrada de cada tramo.

---

### F. Hidrodinámica Subsuperficial: Ley de Darcy y Bombeo de Finos (*Pumping*)

Cuando el agua pluvial penetra a través de las microfisuras no selladas hacia la base y subbase granular, el paso dinámico del neumático a alta velocidad genera un incremento brusco de presión de poros:

$$q = -K_h \nabla h = -K_h \frac{\partial h}{\partial z} \quad \text{[Ley de Darcy]}$$

$$\frac{\partial u_w}{\partial t} = c_v \left( \frac{\partial^2 u_w}{\partial x^2} + \frac{\partial^2 u_w}{\partial y^2} + \frac{\partial^2 u_w}{\partial z^2} \right) + \frac{\partial \sigma_v}{\partial t}$$

Donde:
* $u_w$ es la presión de poro transitoria inducida por el eje del camión ($80\text{ kN}$).
* $K_h$ es el coeficiente de permeabilidad hidráulica.

Bajo presiones hidrostáticas que superan la resistencia al corte del suelo granular ($\tau = c + \sigma' \tan \phi$), se produce el fenómeno destructivo de **bombeo de finos (*pumping*)**: el agua a presión expulsa la arena y arcilla de la subbase hacia la superficie. La mezcla asfáltica pierde su apoyo estructural y colapsa instantáneamente en un **bache profundo**.

---

### G. Dinámica Térmica y Termodinámica de Congelamiento-Deshielo (*Freeze-Thaw*)

El factor más severo de destrucción vial en zonas de gran altitud es el cambio de fase del agua atrapada en los poros capilares del pavimento.

#### 1. Expansión Volumétrica del Agua al Congelarse:
$$\Delta V / V = \frac{\rho_{\text{agua}} - \rho_{\text{hielo}}}{\rho_{\text{hielo}}} = \frac{1.000\text{ g/cm}^3 - 0.917\text{ g/cm}^3}{0.917\text{ g/cm}^3} \approx +9.05\%$$

#### 2. Presión Criogénica de Cristalización (Ecuación de Clapeyron):
$$\left| \frac{dP}{dT} \right| = \frac{\Delta H_f}{T_0 \cdot \Delta v}$$

Donde:
* $\Delta H_f = 333.5\text{ kJ/kg}$ (Calor latente de fusión del hielo).
* $T_0 = 273.15\text{ K}$ ($0^\circ\text{C}$).
* $\Delta v = v_{\text{hielo}} - v_{\text{agua}} = \frac{1}{917} - \frac{1}{1000} \approx 9.05 \times 10^{-5}\text{ m}^3\text{/kg}$ (Variación de volumen específico).

Sustituyendo:
$$\left| \frac{dP}{dT} \right| = \frac{333\,500}{273.15 \times 9.05 \times 10^{-5}} \approx 1.35 \times 10^{7}\ \text{Pa/K} \approx 13.5\ \text{MPa por cada } ^\circ\text{C de subenfriamiento}$$

Esta es la presión necesaria para **impedir** que el agua se congele estando totalmente confinada: una **cota superior teórica**, no la presión real en el poro. En un pavimento real el confinamiento nunca es total (el agua y el hielo migran hacia poros vacíos y fisuras), por lo que la presión efectiva es mucho menor. Aun así, basta una fracción de esa cota para superar la resistencia a la tracción indirecta de una mezcla asfáltica (del orden de $1\text{--}3\text{ MPa}$ según la temperatura), lo que explica la fisuración por helada (*frost cracking*) en mezclas saturadas.

*Alcance en PaveLens AI:* este modelo se presenta como **factor de riesgo climático** (número de ciclos hielo-deshielo por año en Puno × saturación probable de fisuras sin sellar), no como cálculo de esfuerzo por tramo.

---

## 5. Normativa de Ingeniería Civil: Rigor Global y Adaptación a Puno (Perú)

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   MARCO NORMATIVO INTERNACIONAL Y LOCAL                                │
├────────────────────────────────────────┬───────────────────────────────────────────────────────────────┤
│ Ámbito Internacional                   │ Ámbito Nacional y Regional (Perú - Puno Altiplano)            │
├────────────────────────────────────────┼───────────────────────────────────────────────────────────────┤
│ • ASTM D6433 (PCI Pavement Index)      │ • MTC EG-2013 (Especificaciones Técnicas Generales Carreteras)│
│ • AASHTO Guide (1993 / MEPDG)          │ • Manual de Carreteras: Suelos, Geología, Geotecnia y Pav.   │
│ • FHWA Distress Identification Manual  │ • Manual de Conservación Vial del MTC                         │
│ • Superpave Asphalt Binder (PG Grade)  │ • Directivas de Asfalto Modificado con Polímeros (PMA - SBS)  │
└────────────────────────────────────────┴───────────────────────────────────────────────────────────────┘
```

---

### A. Estándar Global ASTM D6433: Cálculo del Pavement Condition Index (PCI)

El **Índice de Condición del Pavimento (PCI)** es la escala estandarizada mundialmente ($0\text{ a }100$) para auditar la salud de vías:

$$\text{PCI} = 100 - \text{CDV}$$

El algoritmo de PaveLens AI implementa la metodología matricial de deducción:
1. **Identificación de Severidad:** Clasificación de cada polígono en severidad *Baja (L)*, *Media (M)* o *Alta (H)* según profundidad y apertura.
2. **Cálculo de Densidad de Deterioro:**
   $$\text{Densidad } (\%) = \frac{\text{Área del Deterioro } (m^2)}{\text{Área de la Unidad de Muestreo } (m^2)} \times 100$$
3. **Valor Deducido ($DV_{ij}$):** La norma publica las curvas de deducción como **gráficas**, no como ecuaciones. PaveLens AI usa curvas lineales por tramos en $\log_{10}(\text{Densidad})$, con puntos digitalizados de esas gráficas, y lo declara como aproximación en el reporte:
   $$DV_{ij} \approx f_{\text{distress}, \text{sev}}\left(\log_{10} \text{Densidad}\right)$$
4. **Número máximo de deducciones admisibles ($m$)**, a partir del mayor valor deducido individual $HDV$:
   $$m = 1 + \frac{9}{98} \left( 100 - HDV \right) \le 10$$
   Solo se conservan los $m$ valores deducidos más altos.
5. **Valor Deducido Corregido ($CDV$):** Procedimiento iterativo de la norma. Se suman los valores deducidos ($TDV$), se cuenta cuántos son mayores que 2 ($q$) y se lee el $CDV$ en la curva de corrección para ese $q$. Luego el menor valor deducido mayor que 2 se reduce a 2 y se repite hasta $q = 1$. El $CDV$ final es el máximo obtenido en las iteraciones.

> *Unidad de muestreo:* ASTM D6433 evalúa unidades de muestreo de superficie definida (del orden de $225 \pm 90\ m^2$ en pavimento asfáltico). Una fotografía cubre solo una parte. Si el área evaluada es menor que 135 m², el sistema calcula las densidades sobre una unidad estándar de 225 m² y marca el resultado como **PCI parcial**: es una cota superior, porque el resto de la unidad no se observó. Agregar varias fotos de una misma unidad queda como trabajo futuro.

| Rango de PCI | Condición Operativa | Intervención Estructural Requerida |
| :---: | :---: | :--- |
| **85 – 100** | Excelente | Mantenimiento preventivo / Sellado de microfisuras |
| **70 – 85** | Bueno | Bacheo superficial focalizado y sellado de juntas |
| **55 – 70** | Regular | Bacheo profundo en caliente y fresado de huellas |
| **40 – 55** | Malo | Recapeo asfáltico (*Overlay*) de $5\text{ cm}$ |
| **0 – 40** | Fallado / Colapso | Reconstrucción total de base granular y carpeta |

---

### B. Especificaciones Técnicas del Perú: MTC EG-2013

PaveLens AI formula los análisis de precios unitarios y cubicajes siguiendo la normativa oficial del **Ministerio de Transportes y Comunicaciones (MTC)** de la República del Perú:

* **Sección 400:** *Pavimentos Asfálticos*.
* **Sección 410:** *Imprimación Asfáltica* con emulsión catiónica de rotura lenta (tipo CSS-1h o asfalto líquido MC-30 a razón de $0.8\text{--}1.5\text{ L/m}^2$).
* **Sección 411:** *Riego de Liga* con emulsión asfáltica catiónica de rotura rápida (tipo CRS-1 o CRS-2 a razón de $0.3\text{--}0.6\text{ L/m}^2$).
* **Sección 423:** *Pavimento de Concreto Asfáltico en Caliente (HMA)*, granulometría tipo MAC-1 o MAC-2, compactado al $98\%$ de la densidad de referencia Marshall (MTC E-504; la norma ASTM D1559 fue retirada y reemplazada por ASTM D6926/D6927).
* **Sección 401 & Manual de Conservación (R.D. N° 08-2014-MTC/14 - Actividades B-01/B-02):** *Corte y Bacheo Superficial y Profundo*, exigiendo corte perimetral con disco diamantado con forma rectangular regular superando en al menos $10\text{ a }15\text{ cm}$ el contorno visible del deterioro.

> **Rigor Normativo Verificado:** Las partidas corresponden a la versión oficial del MTC EG-2013 (R.D. N° 22-2013-MTC/14) y las actividades de mantenimiento rutinario/periódico al Manual de Conservación Vial vigente (R.D. N° 08-2014-MTC/14). El sistema los implementa como **parámetros configurables por región**, garantizando portabilidad normativa a cualquier país (AASHTO, Eurocódigos o MTC).

---

### C. Condicionantes Críticos y Rigor de Ingeniería en Puno (Altiplano Andino)

La región de **Puno, Perú (altitud media de 3,827 a 4,200 m.s.n.m.)** presenta uno de los entornos de servicio vial más hostiles del planeta. PaveLens AI incorpora parámetros adaptados a esta física climática extrema:

```
┌───────────────────────────────────────────────────────────────────────────────────────────────────┐
│                              DESAFÍOS INGENIERILES EXTREMOS EN PUNO                               │
├───────────────────────────────┬───────────────────────────────────┬───────────────────────────────┤
│ 1. Gradiente Térmico Extremo  │ 2. Radiación Solar UV Crítica     │ 3. Ciclos Hielo-Deshielo      │
│ • Superficie al sol muy por   │ • Índice UV diario > 15 (Extremo) │ • Humedad pluvial atrapada    │
│   encima del aire             │ • Foto-oxidación del betún        │ • Expansión del 9% en capilar │
│ • Heladas nocturnas frecuentes│ • Endurecimiento prematuro        │ • Cuña destructiva hidráulica │
│   en invierno (may–ago)       │                                   │                               │
└───────────────────────────────┴───────────────────────────────────┴───────────────────────────────┘
```

> *Fuente de los datos climáticos:* las temperaturas extremas, el número de días con helada y el índice UV se toman de las series de SENAMHI para las estaciones de Puno y del altiplano, y se cargan como parámetros por zona. La amplitud térmica diaria relevante para el diseño es la de la **superficie del pavimento**, que supera ampliamente la del aire por la radiación solar directa.

#### Requisitos Técnicos Mandatorios para Pavimentos en Puno:
1. **Selección de Ligante por Grado de Desempeño Superpave (AASHTO M320):**
   * Los asfaltos convencionales por penetración (60/70, 85/100) son propensos a **fisuración prematura por baja temperatura** y a envejecimiento acelerado por radiación UV en el altiplano.
   * Se recomienda **Asfalto Modificado con Polímeros (PMA) elastoméricos tipo SBS (Estireno-Butadieno-Estireno)** con un grado de desempeño elegido según las temperaturas de diseño del pavimento de la zona, por ejemplo **PG 58-28** o **PG 64-28**:
     * El primer número es la temperatura máxima de diseño del pavimento (promedio de 7 días): resistencia al ahuellamiento en días de sol.
     * El segundo es la temperatura mínima de diseño del pavimento: resistencia a la fisuración térmica transversal (*Low-Temperature Thermal Cracking*).
2. **Ensayos de Tracción Indirecta y Susceptibilidad al Agua (AASHTO T283):**
   * Razón de Tracción Retenida ($TSR \ge 85\%$). Se requiere aditivo promotor de adherencia química anti-desprendimiento (*anti-stripping agent*) de tipo amina al $0.5\%$ en peso del ligante.
3. **Cálculo de Espesor y Curado Térmico:**
   * En el altiplano la mezcla se enfría más rápido por la **baja temperatura del aire, el viento y la pérdida radiativa hacia un cielo despejado**. La menor presión atmosférica ($\approx 63\text{ kPa}$ a 3,800 m.s.n.m. frente a $101.3\text{ kPa}$ a nivel del mar) no acelera el enfriamiento: al bajar la densidad del aire, la convección incluso disminuye ligeramente.
   * La ventana de compactación (de la temperatura de tendido, $\approx 150^\circ\text{C}$, a la mínima de compactación, $\approx 110^\circ\text{C}$) depende del espesor de la capa, la temperatura del aire y de la base, el viento y la radiación. Un modelo de enfriamiento de capa (enfoque de PaveCool / MultiCool) permitiría estimarla y planificar el tren de compactación. **No está implementado**: hoy el sistema muestra estas condiciones como notas de obra junto al presupuesto.

---

## 6. Algoritmia de Tráfico y Resiliencia de Red Urbana

El mantenimiento vial no ocurre en el vacío. Cuando una cuadrilla cierra una calzada para ejecutar bacheo o recapeo, genera un shock de oferta en la red vial.

```
                    ┌────────────────────────────┐
                    │      Inspección SAM 3      │
                    │   Bache Severo Detectado   │
                    └─────────────┬──────────────┘
                                  │
                                  ▼
                    ┌────────────────────────────┐
                    │   Cierre de Carril Vía e   │
                    │   C_obra = C_e * (1 - r)   │
                    └─────────────┬──────────────┘
                                  │
                                  ▼
      ┌─────────────────────────────────────────────────────────┐
      │          Simulación de Asignación de Tráfico            │
      │       • Función BPR de Demora No Lineal                 │
      │       • Principio de Equilibrio de Usuario de Wardrop   │
      └───────────────────────────┬─────────────────────────────┘
                                  │
                                  ▼
                    ┌────────────────────────────┐
                    │  veh·h perdidas y tramos   │
                    │  que absorben el desvío    │
                    └────────────────────────────┘
```

---

### A. Función de Demora de la BPR (*Bureau of Public Roads*)

El tiempo de recorrido $t_e$ en cualquier calle $e \in \mathcal{E}$ crece de forma polinómica (con exponente $\beta = 4$) a medida que la relación volumen/capacidad se acerca a la saturación y la supera:

$$t_e(V_e) = t_e^0 \cdot \left[ 1 + \alpha \left( \frac{V_e}{C_e} \right)^\beta \right]$$

Donde:
* $t_e^0$ es el tiempo de viaje a flujo libre: $t_e^0 = \frac{L_e}{v_{\text{libre}}}$.
* $V_e$ es el flujo horario vehicular que transita por la vía ($\text{veh/hora}$).
* $C_e$ es la capacidad de diseño de la vía ($\text{veh/hora}$).
* $\alpha = 0.15$ y $\beta = 4.0$ (Calibración estándar de la FHWA).

*Impacto del Mantenimiento:*
$$C_e^{\text{mantenimiento}} = C_e \times \left( 1 - \frac{\text{carriles intervenidos}}{\text{carriles totales}} \right)$$

Al reducirse $C_e$ a la mitad en una calle con dos carriles, el término $(V_e / C_e)^4$ se multiplica por $16$. Ejemplo: con $V_e / C_e = 0.8$ el tiempo es $t_e = t_e^0 (1 + 0.15 \cdot 0.8^4) \approx 1.06\, t_e^0$. Al cerrar un carril, la relación sube a $1.6$ y el tiempo pasa a $t_e \approx 1.98\, t_e^0$: casi el **doble** del tiempo de viaje en ese tramo, y la diferencia crece rápidamente si la demanda es mayor. El desvío óptimo de la §6.B redistribuye ese exceso de flujo.

> *Nota:* BPR es una función de demora para planificación; no modela colas ni la dinámica de una congestión sobresaturada. Para el alcance del sistema (comparar escenarios con y sin obra) es suficiente.

---

### B. Principio de Equilibrio de Usuario de Wardrop (*User Equilibrium - UE*)

PaveLens AI asume que cada conductor escoge la ruta que minimiza su propio tiempo de viaje. El estado de equilibrio se formaliza como el problema de optimización convexa de Beckmann:

$$\min_{\mathbf{V}} \sum_{e \in \mathcal{E}} \int_0^{V_e} t_e(w) \, dw$$

$$\text{Sujeto a:} \quad \sum_{k \in \mathcal{K}_{rs}} f_k^{rs} = q_{rs} \quad \forall r,s; \quad V_e = \sum_{r,s} \sum_{k \in \mathcal{K}_{rs}} f_k^{rs} \delta_{e,k}^{rs}; \quad f_k^{rs} \ge 0$$

Donde:
* $q_{rs}$ es la demanda de viajes entre el origen $r$ y destino $s$.
* $f_k^{rs}$ es el flujo en la ruta $k$.
* $\delta_{e,k}^{rs} = 1$ si el enlace $e$ pertenece a la ruta $k$, $0$ en caso contrario.

El sistema resuelve esta asignación mediante el algoritmo de **Frank-Wolfe**, identificando las arterias con capacidad remanente e inyectando las instrucciones de desvío dinámico en la interfaz de usuario.

---

## 7. Hoja de Ruta Ejecutiva hacia la Entrega del Hackathon

| Fecha | Entregable | Estado |
|---|---|---|
| 1 Nov | Monorepo .NET 10 + Angular 22, PCI, cubicaje, APU, tráfico, escenas calibradas | ✅ |
| 2 Nov | Máscaras verificadas al 1 %, interfaz profesional, adaptadores SAM 3 (Roboflow) y Muse Wonder (`api.meta.ai`) | ✅ |
| 3 Nov | Claves API y prueba de punta a punta con fotos reales; escenas Wikimedia con atribución; repositorio git | Pendiente |
| 4 Nov | Video de 2 minutos, verificación final y envío antes de las 9:00 PM ET | Pendiente |

---

## 8. Alineación Estratégica con la Rúbrica del Jurado de Meta

* **Impacto Real (30%):** Inspección con un celular en lugar de una cuadrilla con wincha, y priorización de recursos con un índice comparable (PCI) y presupuestos auditables.
* **Demostración y Video (25%):** Contornos de SAM 3 sobre la foto, medidas en metros con el cursor, PCI, cubicaje y presupuesto que se recalculan con la profundidad medida, y red vial con el desvío.
* **Uso de Herramientas Meta AI (25%):** SAM 3 para la segmentación por concepto y Muse Wonder como evaluador de ingeniería (clasificación, severidad, protocolo y asistente). El modelo razona; los números los calcula el código.
* **Profundidad Técnica y Ejecución (20%):** Arquitectura limpia multicapa en .NET 10 (LTS) con 100 pruebas automatizadas, frontend reactivo en Angular 22, área y momentos de Gauss/Green, PCI según ASTM D6433, cubicaje y APU parametrizados para Puno, y equilibrio de tráfico de Wardrop resuelto con Frank-Wolfe. Burgers, Darcy y hielo-deshielo se presentan como modelos explicativos del riesgo climático.

---

## 9. Registro de Correcciones (2 Nov 2026)

| Sección | Corrección |
|---|---|
| §1 | El volumen y la masa dependen de la profundidad medida; una foto monocular no la entrega |
| §2 | Se reemplazó la tabla de artículos "Q1" (títulos y factores de impacto no verificables) por referencias con autores, año y fuente. PaveSAM queda marcado para verificar su DOI |
| §3 | Datos de RDD2020/2022, Crack500 y GAPs corregidos (cantidades, países, origen de las fotos, tipo de anotación). Se eliminó DeepPCB, que es un dataset de placas electrónicas |
| §4.A | Orden de la homografía: $\mathbf{H} = (\mathbf{K}[\mathbf{r}_1\ \mathbf{r}_2\ \mathbf{t}])^{-1}$. El GSD aplica solo a tomas cenitales |
| §4.B | En tomas oblicuas el área no se escala con un factor: cada vértice se proyecta antes de sumar. El centroide usa el área con signo |
| §4.C | Los momentos se integran sobre el área, no sobre los vértices. La orientación usa $\operatorname{atan2}$. Los umbrales de excentricidad quedan como heurísticos a calibrar |
| §4.D (nueva) | Cálculo de volumen y masa con corte rectangular, profundidad medida y densidad Marshall |
| §4.E | Burgers se declara explicativo: sus parámetros salen de laboratorio |
| §4.G | Clapeyron: $|dP/dT| \approx 13.5$ MPa/K (el documento decía 1.22). Se aclara que es una cota superior teórica con confinamiento total |
| §5.A | Curvas de deducción digitalizadas como aproximación; fórmula del número máximo de deducciones $m$; procedimiento iterativo del CDV; la unidad de muestreo no equivale a una foto |
| §5.B | ASTM D1559 fue retirada (reemplazo: D6926/D6927). Numeración de la EG-2013 marcada para verificar |
| §5.C | Se eliminaron cifras climáticas sin fuente (fuente: SENAMHI). Se suavizó "fracasa en menos de 1 año". La baja presión no acelera el enfriamiento de la mezcla; la ventana de compactación se calcula, no se fija en 8 min |
| §6.A | BPR crece de forma polinómica, no exponencial. Ejemplo numérico: al cerrar un carril el tiempo casi se duplica (no "paso de peatón") |
| §8 | Versiones actualizadas (.NET 10, Angular 22) y alcance realista de cada modelo |
| General | SAM 3.1 → SAM 3: el servicio por API (Roboflow) sirve SAM 3; los checkpoints 3.1 aportan sobre todo seguimiento en video, previsto como trabajo futuro |
| §3.B | La sección de agentes pasa a "trabajo futuro (no implementado)". Se retiró Qwen2-VL (no es de Meta ni se usa) y la publicación a Waze/Google Maps |
| §5.A | Las curvas de deducción son lineales por tramos, no polinómicas. Con una foto el PCI se reporta como parcial (cota superior) |
| §5.C | El modelo de enfriamiento PaveCool se declara no implementado |
| §7 | Hoja de ruta actualizada: sin mapa GIS; el foco del 3 Nov es la prueba con modelos reales |
