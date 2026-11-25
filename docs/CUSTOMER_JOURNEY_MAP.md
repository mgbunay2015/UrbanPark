# Customer Journey Map — Urban Park

Documento de diseño de experiencia de la aplicación móvil **Urban Park**.
Acompaña al diagrama **[customer_journey_map.png](customer_journey_map.png)** (generado con
`generar_cjm.py`, reproducible).

---

## 1. Qué es y para qué sirve

Un **Customer Journey Map (CJM)** es la representación visual del recorrido completo que hace una
persona al relacionarse con un producto: qué hace, qué toca, qué piensa, **cómo se siente** y dónde
aparecen las fricciones. No describe pantallas: describe **experiencias**.

Sirve para tres cosas concretas en este proyecto:

1. **Justificar las decisiones de diseño**: cada pantalla existe porque resuelve un punto de dolor
   concreto del recorrido (ver la tabla de trazabilidad del apartado 7).
2. **Priorizar**: los esfuerzos se concentran en los *momentos de la verdad*, no en lo accesorio.
3. **Medir**: cada etapa tiene su indicador, de modo que la experiencia se puede evaluar, no solo opinar.

---

## 2. Persona

| | |
|---|---|
| **Nombre** | Ana, 34 años |
| **Perfil** | Conductora urbana, trabaja en el centro y usa el auto a diario |
| **Contexto** | Llega a zonas densas en hora punta, con poco tiempo y sin conocer la disponibilidad |
| **Objetivo** | Estacionar rápido, cerca y **sabiendo de antemano cuánto va a pagar** |
| **Frustración actual** | Dar vueltas sin encontrar plaza y descubrir el precio solo al final |
| **Nivel técnico** | Medio: usa el celular a diario, valora que **no le hagan crear otra cuenta más** |
| **Dispositivo** | Android, uso a una mano, a menudo de pie y con prisa |

> En la app esta persona se corresponde con el rol **CLIENTE** (usuarios demo `cliente1`, `cliente2`, `cliente3`).

**Escenario:** martes por la mañana, Ana sale de casa sin saber si habrá sitio. Quiere resolver todo
desde el celular antes y durante el estacionamiento.

---

## 3. Las 6 etapas del recorrido

| # | Etapa | Qué busca Ana | Momento del día |
|---|---|---|---|
| 1 | **Necesito estacionar** | Saber si hay sitio antes de llegar | Antes de salir |
| 2 | **Entrar a la app** | Acceder sin fricción ni registros eternos | Al llegar a la zona |
| 3 | **Encontrar plaza** | La mejor plaza según precio y cercanía | En la calle |
| 4 | **Estacionar** | Dejar constancia de dónde y desde cuándo | Ya en el parqueadero |
| 5 | **Pagar y salir** | Saber cuánto paga y salir sin trabas | Al terminar |
| 6 | **Volver** | Tener historial y repetir sin esfuerzo | Después |

---

## 4. El mapa

Cada fila es una dimensión del análisis; cada columna, una etapa.

| | **1. Necesito estacionar** | **2. Entrar a la app** | **3. Encontrar plaza** | **4. Estacionar** | **5. Pagar y salir** | **6. Volver** |
|---|---|---|---|---|---|---|
| **Acciones** | Conduce dando vueltas buscando un hueco | Abre la app; escribe usuario y clave o usa Google | Mira el mapa de plazas libres; pide recomendación a la IA | Elige plaza y placa; pulsa abrir ticket | Pulsa cerrar / cobrar; ve el monto y el detalle | Consulta sus tickets, su perfil y su sesión |
| **Puntos de contacto** | Calle / estacionamiento de la zona | Pantalla de inicio de sesión y registro | Pantalla **Plazas** + **Asignación con DeepSeek** | Pantalla **Tickets** (formulario) | Pantalla **Tickets** (cerrar y cobrar) o la IA | **Mis tickets** + **Mi perfil** + **Sesión y JWT** |
| **Qué piensa** | "¿Habrá sitio? ¿Cuánto voy a pagar por esto?" | "¿Es seguro? ¿Tengo que crear otra cuenta con otra clave?" | "Quiero la más barata y cerca de la entrada. No quiero dar vueltas." | "Espero no haberme equivocado de plaza. ¿Queda registrado?" | "¿Me cobran la hora completa? ¿Está bien el importe?" | "La próxima vez quiero ir directo a la app." |
| **Emociones** | 😖 Frustración | 😐 Desconfianza | 🙂 Esperanza | 😌 Alivio | 😀 Satisfacción | 🤝 Fidelidad |
| **Puntos de dolor** | Da vueltas sin saber dónde hay plaza libre: pierde tiempo y gasolina | Los registros largos y las contraseñas olvidadas hacen que abandone | No sabe qué zona está más libre ni cuál le conviene | Miedo a ocupar una plaza ajena o a que no quede registrado | Falta de transparencia: no sabe cuánto paga hasta el final | Sin historial no puede planificar ni reclamar lo que pagó |
| **Oportunidades** | Mostrar ocupación en vivo antes de salir de casa | Acceso en un toque con Google; usuarios demo para probar sin registrarse | Recomendación automática por ocupación, tarifa y preferencia | Ticket digital con placa, plaza y hora de entrada a la vista | Cálculo explícito por hora completa y liberación de la plaza al instante | Historial, resumen de gasto y sesión consultable |

### Curva emocional

```
Satisfacción  │                                   ●━━━━●━━━━●   (5) Pagar   (6) Volver
              │                       ●                          (4) Estacionar
Esperanza     │              ●                                   (3) Encontrar plaza
              │      ●                                           (2) Entrar
Frustración   │ ●                                                (1) Necesito estacionar
              └──────────────────────────────────────────────────────────────
```

La curva sube de forma sostenida: el diseño está pensado para **convertir la frustración inicial en
fidelidad**, y el salto más importante ocurre entre la etapa 2 y la 3, donde la mayoría de las apps
de estacionamiento pierden al usuario.

---

## 5. Momentos de la verdad

Tres instantes deciden si Ana vuelve a usar la app:

| Momento | Riesgo | Cómo lo resuelve Urban Park |
|---|---|---|
| **Abrir el ticket** (etapa 4) | Que falle o que no quede constancia → pierde la confianza | El ticket se crea con placa, plaza, zona, hora de entrada y tarifa copiada de la plaza; se ve de inmediato en la lista |
| **Cerrar y cobrar** (etapa 5) | Que el importe sorprenda o que no pueda salir | El importe se calcula en vivo mientras el ticket está abierto, se muestra el estimado **antes** de confirmar y el cobro **nunca** depende de que el servicio de plazas esté disponible |
| **Recomendación de la IA** (etapa 3) | Que la IA falle o invente una plaza | Si DeepSeek no responde, el backend usa el respaldo heurístico local; el campo `proveedorIa` dice siempre qué se usó (`deepseek/…` o `heuristica-local`) |

---

## 6. Indicadores por etapa

| Etapa | Indicador | Objetivo |
|---|---|---|
| 1. Necesito estacionar | Tiempo hasta ver la ocupación | < 1 min desde abrir la app |
| 2. Entrar a la app | Acciones para entrar | ≤ 2 (o 1 con Google) |
| 3. Encontrar plaza | Toques hasta elegir plaza | ≤ 2 (recomendación automática) |
| 4. Estacionar | Tickets abiertos con error | 0 |
| 5. Pagar y salir | Diferencia entre importe estimado y cobrado | $0.00 |
| 6. Volver | Usuarios con historial consultado | ≥ 1 consulta por visita |

---

## 7. Trazabilidad: del dolor a la pantalla

Esta es la parte que conecta el mapa con el producto. Cada punto de dolor tiene su oportunidad de
diseño y su implementación concreta en la app.

| # | Punto de dolor | Oportunidad de diseño | Implementación en la app | Pantalla |
|---|---|---|---|---|
| 1 | Dar vueltas sin saber dónde hay plaza | Ocupación visible antes de salir | Indicadores total / ocupadas / libres / % con barra de progreso y cuadrícula de plazas con estado | **Plazas** |
| 2 | Registros largos y contraseñas olvidadas | Entrada en un toque + prueba sin registro | "Entrar con Google" y 5 perfiles demo con un toque que rellenan usuario y clave | **Iniciar sesión** |
| 3 | No saber qué zona conviene | Recomendación automática | Elección por ocupación de zona → tarifa → preferencia, con motivo y alternativas | **Asignación con DeepSeek** |
| 4 | Miedo a ocupar plaza ajena o a que no quede registro | Ticket digital inmediato | Ticket con placa, plaza, zona, tarifa y hora; conflicto controlado (una plaza y una placa por ticket abierto) | **Tickets** |
| 5 | No saber cuánto paga hasta el final | Importe visible y cálculo explícito | "Valor por cobrar (ahora)" mientras está abierto y confirmación con el estimado antes de cerrar; regla de **hora completa** | **Tickets** |
| 6 | Sin historial no puede reclamar | Historial y resumen de gasto | Resumen "Tickets usados / He pagado / Me falta por pagar" y listado con filtros Abiertos/Cerrados | **Tickets** |
| 7 | Desconfianza en la operación | Transparencia del estado del sistema | Reporte del administrador con sello "Estado: en orden / revisar" y panel de resiliencia con la respuesta JSON | **Reporte IA** / **Resilience** |

---

## 8. Cómo el diseño visual refuerza el recorrido

La interfaz se apoya en un **sistema de diseño** (`Resources/Styles/Colors.xaml` y `Styles.xaml`) con
decisiones tomadas a partir del mapa:

| Decisión de diseño | Por qué (etapa del recorrido) |
|---|---|
| Fondo oscuro tipo asfalto + paneles claros | Identidad de estacionamiento y contraste alto para usarse **de pie y con prisa** (etapas 1–3) |
| Amarillo de señalización como único acento de marca | Se lee como **señalética vial**: jerarquía inmediata, sin decoración |
| Verde-teal en las acciones principales | Guía el ojo hacia el botón que resuelve la etapa |
| Tarjetas con esquinas redondeadas y bordes finos | Reduce la sensación de "formulario" en las etapas 3–5 |
| Badges de estado (LIBRE / OCUPADA / TU PLAZA / Plaza pendiente sync) | Respuesta a la pregunta "¿qué pasa aquí?" **sin leer** (etapas 3 y 5) |
| Importe estimado visible antes de confirmar | Ataca directamente el dolor de la etapa 5 |
| Colores por rol y panel agrupado por áreas | Cada perfil ve solo lo suyo: menos ruido, menos error |
| Avatar con iniciales y sesión siempre visible | Refuerza la sensación de cuenta propia (etapa 6) |
| Mensajes de error con el texto exacto del backend | Evita el "algo salió mal": siempre se sabe **qué** pasó y **qué** hacer |

---

## 9. Resumen ejecutivo

- **Recorrido:** 6 etapas, de la frustración de no encontrar plaza a la fidelidad de volver.
- **Palanca principal:** transparencia anticipada — ocupación antes de salir e importe antes de pagar.
- **Mayor riesgo:** la etapa 3 (encontrar plaza), mitigada con la recomendación por IA **y** su respaldo
  heurístico, de modo que la experiencia nunca se rompe.
- **Evidencia en el producto:** las 7 oportunidades del mapa están implementadas y verificadas en las
  pantallas indicadas (ver `docs/GUIA_EJECUCION.md`, sección de verificación).

---

### Reproducir el diagrama

```bash
python docs/generar_cjm.py      # genera docs/customer_journey_map.png
```
