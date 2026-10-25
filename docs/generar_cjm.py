# -*- coding: utf-8 -*-
"""
Genera el Customer Journey Map de Urban Park como imagen PNG.

Salida: docs/customer_journey_map.png
Ejecutar: python docs/generar_cjm.py
"""
import os
from PIL import Image, ImageDraw, ImageFont

# ----------------------------------------------------------------------------
# Paleta (la misma del sistema de diseno de la app)
# ----------------------------------------------------------------------------
FONDO        = "#FFFFFF"
TINTA        = "#0F172A"
TINTA_MEDIA  = "#475569"
TINTA_SUAVE  = "#94A3B8"
OSCURO       = "#0A0E13"
SUPERFICIE   = "#141B24"
BORDE        = "#E5E9F0"
ACENTO       = "#F2C51A"
TEAL         = "#12B5A5"
VERDE        = "#22C55E"
ROJO         = "#EF4444"
AMBAR        = "#F59E0B"
AZUL         = "#3B82F6"
GRIS_CELDA   = "#FAFBFD"
GRIS_ALT     = "#F1F4F9"

# ----------------------------------------------------------------------------
# Fuentes
# ----------------------------------------------------------------------------
def fuente(nombre, tam):
    for ruta in (rf"C:\Windows\Fonts\{nombre}.ttf", rf"C:\Windows\Fonts\{nombre}bd.ttf"):
        if os.path.exists(ruta):
            try:
                return ImageFont.truetype(ruta, tam)
            except Exception:
                pass
    return ImageFont.load_default()

F_TITULO   = fuente("segoeuib", 46)
F_SUBT     = fuente("segoeui", 22)
F_PERSONA  = fuente("segoeuib", 21)
F_PERSONAD = fuente("segoeui", 19)
F_ETAPA    = fuente("segoeuib", 22)
F_NUM      = fuente("segoeuib", 26)
F_FILA     = fuente("segoeuib", 17)
F_CELDA    = fuente("segoeui", 17)
F_CELDA_B  = fuente("segoeuib", 17)
F_PIE      = fuente("segoeui", 17)

# ----------------------------------------------------------------------------
# Contenido del journey
# ----------------------------------------------------------------------------
ETAPAS = [
    ("1", "Necesito\nestacionar",   "Sale de casa o del trabajo sin saber si habra plaza cerca."),
    ("2", "Entrar a\nla app",       "Abre Urban Park y se autentica con usuario o con Google."),
    ("3", "Encontrar\nplaza",       "Consulta disponibilidad en vivo o pide a la IA que le recomiende una."),
    ("4", "Estacionar",             "Abre el ticket con su placa y deja el auto en la plaza elegida."),
    ("5", "Pagar\ny salir",         "Cierra el ticket, ve el importe calculado y libera la plaza."),
    ("6", "Volver",                 "Revisa su historial, su perfil y vuelve a usar la app."),
]

ACCIONES = [
    "Conduce dando vueltas\nbuscando un hueco libre.",
    "Abre la app.\nEscribe usuario y clave\no usa Google.",
    "Mira el mapa de plazas\nlibres. Pide recomendacion\na la IA por precio.",
    "Elige plaza y placa.\nPulsa abrir ticket.",
    "Pulsa cerrar / cobrar.\nVe el monto y el detalle\ndel tiempo.",
    "Consulta sus tickets,\nsu perfil y su sesion\nen la app.",
]

TOUCHPOINTS = [
    "Calle / estacionamiento\nde la zona.",
    "Pantalla de inicio\nde sesion\ny registro.",
    "Pantalla Plazas\n+ Asignacion con\nDeepSeek.",
    "Pantalla Tickets\n(formulario de\napertura).",
    "Pantalla Tickets\n(cerrar y cobrar)\no la IA.",
    "Mis tickets\n+ Mi perfil\n+ Sesion y JWT.",
]

PIENSA = [
    "\u00bfHabra sitio?\n\u00bfCuanto voy a pagar\npor esto?",
    "\u00bfEs seguro? \u00bfTengo\nque crear otra cuenta\ncon otra clave?",
    "Quiero la mas barata\ny cerca de la entrada.\nNo quiero dar vueltas.",
    "Espero no haberme\nequivocado de plaza.\n\u00bfQueda registrado?",
    "\u00bfMe cobran la hora\ncompleta? \u00bfEsta bien\nel importe?",
    "La proxima vez\nquiero ir directo\na la app.",
]

# (palabra, altura 0..4)  -> 4 = muy contenta
EMOCIONES = [
    ("Frustracion", 1),
    ("Desconfianza", 2),
    ("Esperanza", 3),
    ("Alivio", 4),
    ("Satisfaccion", 4),
    ("Fidelidad", 4),
]

DOLORES = [
    "Da vueltas sin saber\ndonde hay plaza libre.\nPierde tiempo y gasolina.",
    "Registros largos\ny contrasenas olvidadas\nhacen que abandone.",
    "No sabe que zona\nesta mas libre ni\ncual le conviene.",
    "Miedo a ocupar una\nplaza ajena o a que\nno quede registrado.",
    "Falta de transparencia:\nno sabe cuanto paga\nhasta el final.",
    "Sin historial no puede\nplanificar ni reclamar\nlo que pago.",
]

OPORTUNIDADES = [
    "Mostrar ocupacion en vivo\nantes de salir de casa.",
    "Acceso en un toque con\nGoogle. Usuarios demo\npara probar sin registrarse.",
    "Recomendacion automatica\npor ocupacion, tarifa\ny preferencia.",
    "Ticket digital con placa,\nplaza y hora de entrada\na la vista.",
    "Calculo explicito por hora\ncompleta y liberacion de\nla plaza al instante.",
    "Historial, resumen de\ngasto y sesion\nconsultable.",
]

# ----------------------------------------------------------------------------
# Lienzo
# ----------------------------------------------------------------------------
MARGEN        = 54
ANCHO_ETIQUETA = 250
ANCHO_CELDA   = 372
ALTO_FILA     = 172
ALTO_EMOCION  = 250
ALTO_ETAPA    = 156
ALTO_CABECERA = 196
ALTO_PERSONA  = 92
ALTO_PIE      = 152

ANCHO = MARGEN * 2 + ANCHO_ETIQUETA + ANCHO_CELDA * len(ETAPAS)
FILAS = [("ACCIONES", ACCIONES, ALTO_FILA), ("PUNTOS DE CONTACTO", TOUCHPOINTS, ALTO_FILA),
         ("QUE PIENSA", PIENSA, ALTO_FILA), ("EMOCIONES", None, ALTO_EMOCION),
         ("PUNTOS DE DOLOR", DOLORES, ALTO_FILA), ("OPORTUNIDADES", OPORTUNIDADES, ALTO_FILA)]
ALTO = ALTO_CABECERA + ALTO_PERSONA + ALTO_ETAPA + sum(f[2] for f in FILAS) + ALTO_PIE + 20

img = Image.new("RGB", (ANCHO, ALTO), FONDO)
dib = ImageDraw.Draw(img)


def ancho_texto(texto, font):
    """Ancho en pixeles de un texto con una fuente dada."""
    return int(dib.textlength(texto, font=font))


def envolver(texto, font, ancho_max):
    """Ajusta un texto al ancho indicado devolviendo varias lineas."""
    lineas, actual = [], ""
    for palabra in texto.split():
        prueba = (actual + " " + palabra).strip()
        if ancho_texto(prueba, font) <= ancho_max:
            actual = prueba
        else:
            if actual:
                lineas.append(actual)
            actual = palabra
    if actual:
        lineas.append(actual)
    return "\n".join(lineas)

# ----------------------------------------------------------------------------
# Cabecera
# ----------------------------------------------------------------------------
dib.rectangle([0, 0, ANCHO, ALTO_CABECERA], fill=OSCURO)
dib.rectangle([0, ALTO_CABECERA - 10, ANCHO, ALTO_CABECERA], fill=ACENTO)
dib.text((MARGEN, 44), "URBAN PARK", font=F_TITULO, fill=ACENTO)
dib.text((MARGEN, 104), "Customer Journey Map  ·  del conductor que no encuentra plaza al usuario fiel",
         font=F_SUBT, fill="#E8ECF1")
dib.text((ANCHO - MARGEN - 470, 52), "Experiencia movil .NET MAUI", font=F_CELDA_B, fill="#B6C2D1")
dib.text((ANCHO - MARGEN - 470, 80), "6 etapas  ·  6 filas de analisis", font=F_CELDA, fill=TINTA_SUAVE)

# ----------------------------------------------------------------------------
# Persona
# ----------------------------------------------------------------------------
y = ALTO_CABECERA
dib.rectangle([0, y, ANCHO, y + ALTO_PERSONA], fill=GRIS_ALT)
dib.ellipse([MARGEN, y + 20, MARGEN + 52, y + 72], fill=TEAL)
dib.text((MARGEN + 15, y + 30), "A", font=F_NUM, fill="#FFFFFF")
dib.text((MARGEN + 70, y + 18), "PERSONA:  Ana, 34 anos, conductora urbana en Quito", font=F_PERSONA, fill=TINTA)
dib.text((MARGEN + 70, y + 48),
         "Objetivo: estacionar rapido y saber cuanto paga.  Frustracion actual: dar vueltas y descubrir el precio al final.",
         font=F_PERSONAD, fill=TINTA_MEDIA)

# ----------------------------------------------------------------------------
# Cabecera de etapas
# ----------------------------------------------------------------------------
y += ALTO_PERSONA
alto_cab = ALTO_ETAPA
for i, (num, nombre, _) in enumerate(ETAPAS):
    x = MARGEN + ANCHO_ETIQUETA + i * ANCHO_CELDA
    color = [TEAL, AZUL, ACENTO, AMBAR, VERDE, TEAL][i]
    dib.rectangle([x, y, x + ANCHO_CELDA - 6, y + alto_cab], fill=SUPERFICIE)
    dib.rectangle([x, y, x + ANCHO_CELDA - 6, y + 6], fill=color)
    dib.ellipse([x + 18, y + 26, x + 60, y + 68], fill=color)
    dib.text((x + 30, y + 32), num, font=F_NUM, fill=OSCURO)
    dib.multiline_text((x + 74, y + 24), nombre, font=F_ETAPA, fill="#F8FAFC", spacing=4)
    descripcion = ETAPAS[i][2]
    dib.multiline_text((x + 18, y + 78), envolver(descripcion, F_CELDA, ANCHO_CELDA - 44),
                       font=F_CELDA, fill="#B6C2D1", spacing=4)

# etiqueta de la esquina
dib.rectangle([MARGEN, y, MARGEN + ANCHO_ETIQUETA - 12, y + alto_cab], fill=OSCURO)
dib.text((MARGEN + 16, y + 30), "ETAPAS DEL", font=F_FILA, fill=ACENTO)
dib.text((MARGEN + 16, y + 56), "VIAJE", font=F_FILA, fill=ACENTO)
dib.text((MARGEN + 16, y + 90), "Lo que vive el cliente", font=F_CELDA, fill="#B6C2D1")

y += alto_cab

# ----------------------------------------------------------------------------
# Filas
# ----------------------------------------------------------------------------
for idx, (titulo, celdas, alto) in enumerate(FILAS):
    fondo = GRIS_CELDA if idx % 2 == 0 else FONDO
    dib.rectangle([MARGEN, y, ANCHO - MARGEN, y + alto], fill=fondo)

    # etiqueta de la fila
    dib.rectangle([MARGEN, y, MARGEN + ANCHO_ETIQUETA - 12, y + alto], fill="#EDF1F7")
    dib.text((MARGEN + 16, y + 20), titulo, font=F_FILA, fill=TINTA)

    if titulo == "EMOCIONES":
        # eje de la curva
        base = y + alto - 46
        puntos = []
        for i in range(len(ETAPAS)):
            _, nivel = EMOCIONES[i]
            x = MARGEN + ANCHO_ETIQUETA + i * ANCHO_CELDA + ANCHO_CELDA // 2 - 3
            puntos.append((x, base - (nivel - 1) * 40))

        # area bajo la curva
        for i in range(len(puntos) - 1):
            x1, y1 = puntos[i]
            x2, y2 = puntos[i + 1]
            dib.polygon([(x1, y1), (x2, y2), (x2, base + 6), (x1, base + 6)], fill="#EAF7F5")
        # linea
        for i in range(len(puntos) - 1):
            color = VERDE if puntos[i][1] <= base - 80 else AMBAR if puntos[i][1] <= base - 40 else ROJO
            dib.line([puntos[i], puntos[i + 1]], fill=color, width=6)
        # nodos
        for i, (px, py) in enumerate(puntos):
            palabra, nivel = EMOCIONES[i]
            color = VERDE if nivel >= 4 else AMBAR if nivel >= 3 else ROJO
            dib.ellipse([px - 14, py - 14, px + 14, py + 14], fill=color)
            dib.ellipse([px - 6, py - 6, px + 6, py + 6], fill="#FFFFFF")
            dib.text((px - ancho_texto(palabra, F_CELDA_B) // 2, y + 24), palabra, font=F_CELDA_B, fill=TINTA)
        dib.text((MARGEN + 16, y + alto - 40), "alto = mejor", font=F_CELDA, fill=TINTA_SUAVE)

    else:
        for i, texto in enumerate(celdas):
            x = MARGEN + ANCHO_ETIQUETA + i * ANCHO_CELDA
            # separador vertical suave
            dib.line([x - 6, y + 14, x - 6, y + alto - 14], fill=BORDE, width=2)
            color_texto = TINTA_MEDIA
            if titulo == "PUNTOS DE DOLOR":
                color_texto = "#B91C1C"
            elif titulo == "OPORTUNIDADES":
                color_texto = "#15803D"
            dib.multiline_text((x + 14, y + 22), texto, font=F_CELDA,
                               fill=color_texto, spacing=6)

    dib.line([MARGEN, y + alto, ANCHO - MARGEN, y + alto], fill=BORDE, width=2)
    y += alto

# ----------------------------------------------------------------------------
# Pie con indicadores
# ----------------------------------------------------------------------------
dib.rectangle([0, y, ANCHO, y + ALTO_PIE], fill=OSCURO)
dib.line([0, y, ANCHO, y], fill=ACENTO, width=8)

kpis = [
    ("Tiempo para encontrar plaza", "de 15 min dando vueltas  >  menos de 1 min en la app"),
    ("Pago sin sorpresas", "importe calculado y visible antes de confirmar"),
    ("Momentos de la verdad", "abrir ticket  ·  cerrar y cobrar  ·  recomendacion de la IA"),
]
col = (ANCHO - MARGEN * 2) // 3
for i, (titulo, detalle) in enumerate(kpis):
    x = MARGEN + i * col
    dib.text((x, y + 30), titulo, font=F_CELDA_B, fill=ACENTO)
    dib.text((x, y + 60), detalle, font=F_PIE, fill="#B6C2D1")
    dib.text((x, y + 96), "PANTALLAS: " + ["Plazas + IA", "Tickets", "IA + Tickets"][i],
             font=F_PIE, fill=TINTA_SUAVE)

salida = os.path.join(os.path.dirname(os.path.abspath(__file__)), "customer_journey_map.png")
img.save(salida, "PNG")
print("Generado:", salida)
print("Tamano:", img.size)
