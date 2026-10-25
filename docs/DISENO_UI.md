# Sistema de diseño — Urban Park móvil

Documentación de la capa visual de la aplicación. Todo el color, la tipografía y los componentes viven
en **dos archivos**, y las pantallas no escriben nunca un color en línea:

| Archivo | Contenido |
|---|---|
| `UrbanPark.App/Resources/Styles/Colors.xaml` | Paleta (design tokens) |
| `UrbanPark.App/Resources/Styles/Styles.xaml` | Componentes (estilos implícitos y con clave) |

Desde el código C# se accede con `Ui.ColorDe("Exito")` y `Ui.EstiloDe("Tarjeta")`, de modo que un cambio
de paleta se propaga a **toda** la aplicación sin tocar las pantallas.

---

## 1. Principios de diseño

1. **Contraste para usarse de pie y con prisa.** Fondo oscuro tipo asfalto, contenido claro encima. La
   información importante salta a la vista sin esfuerzo.
2. **Un solo acento de marca.** El amarillo de señalización vial (`Acento`) marca jerarquía e identidad;
   el verde-teal (`Primario`) marca *acción*. No compiten.
3. **Semántica antes que decoración.** Verde = libre / correcto, rojo = ocupado / error, ámbar = atención
   pendiente. El color **informa**.
4. **Redondeado y con aire.** Esquinas de 16–18 px, bordes finos de 1 px y espaciado generoso: la
   diferencia entre un formulario y un producto.
5. **Cero sorpresas.** Los importes se muestran antes de confirmar y los errores dicen exactamente qué
   pasó y qué hacer.

---

## 2. Paleta

### Marca
| Token | Hex | Uso |
|---|---|---|
| `Acento` | `#F2C51A` | Amarillo de señalización: marca, títulos destacados, métricas, chips activos |
| `AcentoOscuro` | `#C9A012` | Bordes y variantes del acento |
| `AcentoSuave` | `#3D3413` | Fondos translúcidos del acento |

### Superficies oscuras (chrome)
| Token | Hex | Uso |
|---|---|---|
| `Fondo` | `#0A0E13` | Fondo de la aplicación |
| `Superficie` | `#141B24` | Tarjetas oscuras, cabecera |
| `SuperficieAlta` | `#1C2530` | Superficie elevada, botones secundarios |
| `SuperficieBorde` | `#28323F` | Bordes sobre fondo oscuro |

### Superficies claras (contenido)
| Token | Hex | Uso |
|---|---|---|
| `Blanco` | `#FFFFFF` | Tarjetas de contenido |
| `BlancoHumo` | `#F4F6FA` | Fondo de campos de formulario |
| `BordeClaro` | `#E5E9F0` | Bordes sobre fondo claro |

### Acción y estados
| Token | Hex | Uso |
|---|---|---|
| `Primario` | `#12B5A5` | Botón principal, acentos de acción |
| `PrimarioOscuro` | `#0C8C80` | Variante oscura / bordes |
| `PrimarioClaro` | `#E4F7F4` | Fondos suaves de acción |
| `Exito` / `ExitoClaro` / `ExitoTexto` | `#22C55E` / `#E7F8EE` / `#15803D` | Plato libre, estado correcto |
| `Peligro` / `PeligroClaro` / `PeligroTexto` | `#EF4444` / `#FDECEC` / `#B91C1C` | Plaza ocupada, error |
| `Advertencia` / `AdvertenciaClaro` / `AdvertenciaTexto` | `#F59E0B` / `#FEF4E3` / `#B45309` | Pendiente de sincronizar, atención |
| `Info` / `InfoClaro` / `InfoTexto` | `#3B82F6` / `#E8F0FE` / `#1D4ED8` | Información secundaria |

### Texto
| Token | Hex | Uso |
|---|---|---|
| `TextoClaro` / `TextoClaroMedio` / `TextoClaroSuave` | `#F8FAFC` / `#B6C2D1` / `#7C8A9C` | Jerarquía sobre fondo oscuro |
| `TextoFuerte` / `TextoMedio` / `TextoSuave` | `#0F172A` / `#475569` / `#94A3B8` | Jerarquía sobre fondo claro |

---

## 3. Tipografía

Se usan las fuentes incluidas en el proyecto (**Open Sans**, Regular y SemiBold), lo que garantiza el
mismo aspecto en Android, iOS y Windows sin descargar nada.

| Estilo | Tamaño | Peso | Uso |
|---|---|---|---|
| `TituloPagina` | 26 | SemiBold | Título de cada pantalla |
| `SubtituloPagina` | 13 | Regular | Bajada explicativa |
| `TituloSeccion` / `TituloSeccionClaro` | 16 | SemiBold | Título dentro de una tarjeta |
| `EncabezadoTabla` / `EncabezadoTablaClaro` | 11 + espaciado | SemiBold | Rótulos de sección y cabeceras de tabla |
| `MetricaValor` | 30 | SemiBold | Cifra destacada de un indicador |
| `MetricaEtiqueta` | 11 + espaciado | SemiBold | Rótulo del indicador |
| `EtiquetaCampo` / `EtiquetaCampoClaro` | 12 | SemiBold | Etiqueta de un campo de formulario |
| `DatoFila` / `DatoFilaFuerte` | 15 | Regular / SemiBold | Filas de datos |
| `TextoSuave` / `TextoSuaveClaro` / `Ayuda` | 12 | Regular | Texto secundario y ayudas |
| `Codigo` | 11 monoespaciada | Regular | JSON y JWT |

---

## 4. Componentes

### Contenedores
| Clave | Descripción | Dónde se usa |
|---|---|---|
| `Tarjeta` | Tarjeta oscura con borde fino, radio 18, padding 18 | Bloques destacados, paneles, cabecera de chat |
| `TarjetaClara` | Tarjeta blanca con borde claro y radio 18 | Formularios y filas de contenido |
| `TarjetaLista` | Tarjeta blanca compacta y pulsable | Elementos de lista, tarjetas de navegación |
| `TarjetaMetrica` | Tarjeta oscura de indicador | Total, ocupadas, libres, % ocupación, recaudado |
| `PanelCodigo` | Panel casi negro con borde | Respuesta JSON y JWT |
| `BarraApp` | Cabecera con las esquinas inferiores redondeadas | Menú principal |
| `Divisor` | Línea de 1 px | Separación dentro de tarjetas |

### Formularios
Los campos van **siempre** dentro de un `Border` con estilo `Campo` (claro) o `CampoOscuro` (oscuro),
con su etiqueta `EtiquetaCampo` encima. Así se consiguen esquinas redondeadas y foco coherente en las
tres plataformas, algo que el control `Entry` no permite por sí solo.

### Botones
| Clave | Aspecto | Uso |
|---|---|---|
| `BotonPrimario` | Relleno teal, texto blanco | Acción principal de la pantalla |
| `BotonAcento` | Relleno amarillo, texto oscuro | Acción estrella (asignar con IA, perfil admin) |
| `BotonSecundario` | Relleno elevado oscuro, borde fino | Acciones alternativas (actualizar, recargar, Google) |
| `BotonFantasma` | Transparente con borde | Acciones terciarias |
| `BotonPeligro` | Transparente con borde rojo | Cerrar sesión, acciones destructivas |
| `BotonMini` | Compacto (38 px, radio 10) | Acciones dentro de una fila |

### Estados y filtros
| Clave | Uso |
|---|---|
| `BadgeOk` + `BadgeOkTexto` | LIBRE, Activo, estado en orden |
| `BadgeOcupado` + `BadgeOcupadoTexto` | OCUPADA, Inactivo, error |
| `BadgeAviso` + `BadgeAvisoTexto` | "Plaza pendiente sync", pendiente de revisar |
| `BadgeMio` + `BadgeMioTexto` | "TU PLAZA" |
| `Badge` + `BadgeTexto` | Badge neutro |
| `Chip` / `ChipActivo` (+ sus textos) | Filtros Todos / Abiertos / Cerrados |
| `EstadoVacio` | Texto centrado para listas sin resultados |

---

## 5. Reglas de composición

- **Radios:** 18 px en tarjetas, 16 px en filas, 12–14 px en campos y botones, 999 en badges y chips.
- **Bordes:** siempre 1 px; el grosor ya no se usa para llamar la atención (ahora lo hace el color de fondo).
- **Espaciado:** 18 px de margen lateral de pantalla, 10 px entre elementos de una lista, 16–18 px de
  padding dentro de una tarjeta.
- **Iconografía:** un icono por acción principal, dentro de un contenedor con el color del área al 16 %
  de opacidad (botones de navegación del menú).
- **Estados de color:** un mismo dato no se colorea de dos maneras distintas en pantallas diferentes.

---

## 6. Accesibilidad

- Contraste alto entre texto y fondo en ambas superficies (oscura y clara).
- Ningún dato depende **solo** del color: los estados llevan además texto (LIBRE / OCUPADA / pendiente).
- Tamaños de toque de 38 px como mínimo y 52 px en las acciones principales.
- Todos los textos de error usan frases completas del backend, no códigos.

---

## 7. Antes y después

| Antes | Después |
|---|---|
| Colores escritos a mano en cada pantalla (~30 hex distintos) | Una sola paleta de 33 tokens con nombre semántico |
| Esquinas rectas de 2 px y bordes de colores fuertes | Radios de 12–18 px y bordes finos de 1 px |
| Menú como lista de botones planos | Panel agrupado por áreas, con icono, descripción y tarjetas pulsables |
| Botones de 3 colores sin criterio | 6 variantes con jerarquía clara (primario → fantasma → peligro) |
| Sin estados de carga, vacío ni error diferenciados | `EstadoVacio`, `MensajeError`, `MensajeOk` y spinner del sistema |
| Sin tarjetas de indicador | `TarjetaMetrica` con cifra destacada y barra de progreso |
| Campos de texto "desnudos" | Campos dentro de `Campo` con etiqueta y foco coherente en Android, iOS y Windows |
