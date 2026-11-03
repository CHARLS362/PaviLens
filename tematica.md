# 🛣️ PaveLens AI: Definición y Temática del Software

> **Documento Conceptual y de Producto**
> **Nombre:** PaveLens AI (*Road Distress Diagnostic, Cost Estimation & Work-Zone Traffic Planning*)
> **Eje del Hackathon:** *Build for Your World* (infraestructura pública y seguridad vial) & *Push the Frontier* (visión aplicada a sistemas físicos)
> **Modelos Base:** Meta SAM 3 (Segment Anything) & Meta Muse Wonder

---

## 💡 1. ¿Qué es PaveLens AI en una sola frase?

**PaveLens AI** convierte una foto de la calzada en un **expediente de conservación vial**: segmenta la forma exacta de cada deterioro, la mide en metros reales, calcula el PCI, el volumen de mezcla y el presupuesto en soles, y estima cuánto empeora el tráfico al cerrar el tramo para repararlo.

---

## 🛑 2. El Problema que Resuelve

1. **Inspecciones lentas y subjetivas.** La evaluación de pavimentos se hace a pie, con wincha y libreta. Dos inspectores pueden asignar severidades distintas al mismo bache.
2. **Cubicaje a ojo.** Sin el área real ni la profundidad, la cantidad de mezcla asfáltica se estima por exceso o por defecto, y el presupuesto no se puede auditar.
3. **Prioridades sin fundamento.** Sin un índice comparable (PCI) por tramo, es difícil justificar qué calle se repara primero.
4. **Desvíos improvisados.** Cerrar un carril sin modelar la red traslada la congestión a calles que no tienen capacidad.

---

## 🛠️ 3. ¿Cómo Funciona? (Flujo Implementado)

```
   [ 1. FOTO DE LA CALZADA ]
   Cenital (ancho de calzada conocido) u oblicua (4 esquinas de un rectángulo conocido).
                 │
                 ▼
   [ 2. META SAM 3: SEGMENTACIÓN POR CONCEPTO ]
   Un polígono por deterioro: bache, piel de cocodrilo, fisuras, ahuellamiento.
                 │
                 ▼
   [ 3. GEOMETRÍA EN METROS ]
   Homografía u escala → área (Green), perímetro, largo × ancho, excentricidad.
                 │
                 ▼
   [ 4. META MUSE WONDER: DIAGNÓSTICO ]
   Tipo, severidad, causa probable, intervención y protocolo de obra.
   Recibe las medidas ya calculadas; no inventa números.
                 │
                 ▼
   [ 5. INGENIERÍA (CÓDIGO DETERMINISTA) ]
   • PCI según ASTM D6433 (parcial si la foto cubre menos de una unidad de muestreo).
   • Cubicaje: corte rectangular + profundidad (medida en campo o supuesta por severidad).
   • APU con partidas MTC en PEN, gastos generales, utilidad e IGV.
                 │
                 ▼
   [ 6. TRÁFICO DURANTE LA OBRA (BPR + EQUILIBRIO DE WARDROP) ]
   Cierre de carriles en la red → veh·h antes y después y tramos que absorben el desvío.
```

---

## 🌟 4. Capacidades Centrales

### 1. Segmentación de contornos (Meta SAM 3)
SAM 3 recibe conceptos en texto y devuelve el contorno de cada instancia, sin entrenar un modelo por tipo de daño:
* 🔴 **Bache:** cavidad con pérdida de carpeta.
* 🟡 **Piel de cocodrilo:** malla de fisuras por fatiga.
* 🟠 **Ahuellamiento:** depresión longitudinal en la huella de las ruedas.
* ➖ **Fisuras longitudinales y transversales.**

### 2. Cuantificación y Análisis de Precios Unitarios (APU)
Ejemplo real de la escena de demostración *Av. El Sol*:
* **Bache B1:** 0.57 m² medidos, 1.32 m² de corte rectangular con 15 cm de margen.
* **Mezcla:** 0.24 t de mezcla asfáltica en caliente, con profundidad supuesta de 7.5 cm (severidad alta) hasta que el inspector la mida.
* **Presupuesto:** S/ 193.40 con IGV, desglosado en corte perimetral con disco, demolición y eliminación, riego de liga, mezcla en caliente, compactación y sellado de fisuras (la fisura FL1 de la misma foto).

### 3. Protocolo de obra guiado
Muse Wonder redacta los pasos según el Manual de Conservación Vial del MTC y la sección 423 de la EG-2013 (mezcla en caliente):
1. Señalización del cierre.
2. Corte rectangular con disco, 10–15 cm fuera del contorno visible.
3. Retiro de material suelto y limpieza.
4. Riego de liga en fondo y paredes.
5. Colocación de mezcla en capas y compactación hasta la densidad especificada.

El inspector marca cada paso como completado y puede preguntar al asistente sobre la obra.

### 4. Impacto en el tráfico durante la obra
* Se elige un tramo de la red y cuántos carriles se cierran.
* La asignación de equilibrio (Frank-Wolfe con la función de demora BPR) compara el escenario actual con el de la obra.
* El resultado muestra el costo total en veh·h y qué calles absorben el desvío.

---

## 👥 5. ¿A Quién Beneficia?

1. **Municipalidades y gobiernos regionales:** inventario de deterioros comparable y priorización por PCI.
2. **Contratistas de conservación vial:** metrados y presupuestos trazables desde la foto.
3. **Supervisión de obras:** cada cifra queda ligada a una máscara, una calibración y una profundidad.
4. **Ciudadanía:** reparaciones mejor priorizadas y desvíos planificados.

---

## 🚀 6. Diferenciador

* **Lo habitual:** un detector que dibuja una caja sobre el bache, o un chatbot que opina sobre una foto.
* **PaveLens AI:** el modelo de visión de Meta entrega la forma; la geometría proyectiva la convierte en metros; las normas (ASTM D6433, MTC) la convierten en PCI, metrado y presupuesto; y la teoría de tráfico estima el costo del cierre. Muse Wonder explica y planifica, pero los números salen de cálculos verificables.
