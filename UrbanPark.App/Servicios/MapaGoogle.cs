using System.Globalization;
using System.Text;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>Mapas embebidos: Google (vista sin ruta) o Leaflet con marcadores rojos.</summary>
public static class MapaGoogle
{
    /// <summary>URL que el WebView intercepta al tocar un pin (WinUI/Android).</summary>
    public const string UrlClicSede = "https://codigo.urbanpark/sede/";

    public static string UrlNavegacionSede(long sedeId) => $"{UrlClicSede}{sedeId}";

    public static bool TryParseClicSede(string? url, out long sedeId)
    {
        sedeId = 0;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (url.StartsWith("urbanpark://sede/", StringComparison.OrdinalIgnoreCase))
        {
            var idText = url["urbanpark://sede/".Length..].Split('?', '#')[0];
            return long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out sedeId);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Equals("codigo.urbanpark", StringComparison.OrdinalIgnoreCase))
            return false;

        var segmentos = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segmentos.Length < 2 || !string.Equals(segmentos[0], "sede", StringComparison.OrdinalIgnoreCase))
            return false;

        return long.TryParse(segmentos[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sedeId);
    }
    /// <summary>Google Maps con pin rojo en el parqueadero, sin indicaciones.</summary>
    public static string HtmlGoogleParqueaderoSinRuta(Location origen, SedeDto sede)
    {
        var cultura = CultureInfo.InvariantCulture;
        var dLat = sede.Latitud.ToString(cultura);
        var dLng = sede.Longitud.ToString(cultura);
        var cLat = ((origen.Latitude + sede.Latitud) / 2).ToString(cultura);
        var cLng = ((origen.Longitude + sede.Longitud) / 2).ToString(cultura);
        var url = $"https://maps.google.com/maps?q={dLat},{dLng}&ll={cLat},{cLng}&hl=es&z=14&output=embed";
        return HtmlEmbebido(url);
    }
    public static string UrlRuta(Location origen, Location destino)
    {
        var cultura = CultureInfo.InvariantCulture;
        var desde = string.Create(cultura, $"{origen.Latitude},{origen.Longitude}");
        var hasta = string.Create(cultura, $"{destino.Latitude},{destino.Longitude}");
        return $"https://maps.google.com/maps?saddr={desde}&daddr={hasta}&dirflg=w&hl=es&z=15&output=embed";
    }

    public static string HtmlEmbebido(string url) =>
        "<!DOCTYPE html><html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<style>html,body{margin:0;height:100%;background:#E7EEF6;}iframe{position:absolute;inset:0;width:100%;height:100%;border:0;}</style></head>" +
        $"<body><iframe src=\"{url}\" allowfullscreen loading=\"lazy\" referrerpolicy=\"no-referrer-when-downgrade\"></iframe></body></html>";

    /// <summary>Vista general con calles (OpenStreetMap): marcadores sin rutas.</summary>
    public static string HtmlMarcadoresSinRuta(
        Location origen,
        IReadOnlyList<SedeDto> sedes,
        long? destacadaId = null,
        string? direccionUsuario = null,
        bool ubicacionReal = true,
        double? precisionMetros = null)
    {
        var cultura = CultureInfo.InvariantCulture;
        var puntosBounds = new List<string>
        {
            $"[{origen.Latitude.ToString(cultura)},{origen.Longitude.ToString(cultura)}]"
        };
        var capas = new StringBuilder();

        var oLat = origen.Latitude.ToString(cultura);
        var oLng = origen.Longitude.ToString(cultura);

        var popupTu = TextoPopupUsuario(origen, direccionUsuario, ubicacionReal, precisionMetros);
        capas.Append(
            $"var pinTu=L.circleMarker([{oLat},{oLng}],{{radius:11,color:'#15803D',fillColor:'#22C55E',fillOpacity:1,weight:2}})" +
            ".bindTooltip('Tú',{permanent:true,direction:'top',offset:[0,-8],className:'tt'}).addTo(map);" +
            $"pinTu.bindPopup('{popupTu}',{{closeButton:false,maxWidth:300,className:'pop-sede',autoPan:false}});" +
            "pinTu.on('mouseover',function(){this.openPopup();});" +
            "pinTu.on('mouseout',function(){this.closePopup();});" +
            "pinTu.on('click',function(e){L.DomEvent.stopPropagation(e);this.openPopup();});");

        foreach (var sede in sedes)
        {
            if (sede.Latitud == 0 && sede.Longitud == 0) continue;

            var activa = destacadaId is not null && sede.Id == destacadaId;
            var color = activa ? "#D93025" : "#EA4335";
            var borde = activa ? "#9B1D16" : "#B31412";
            var radio = activa ? 11 : 9;
            var lat = sede.Latitud.ToString(cultura);
            var lng = sede.Longitud.ToString(cultura);
            puntosBounds.Add($"[{lat},{lng}]");

            var titulo = EscaparJs(sede.Codigo ?? sede.Nombre ?? "P");
            var popup = TextoPopupSede(sede);
            var id = sede.Id.ToString(cultura);
            capas.Append(
                $"var pin{id}=L.circleMarker([{lat},{lng}],{{radius:{radio},color:'{borde}',fillColor:'{color}',fillOpacity:0.95,weight:2}})" +
                $".bindTooltip('{titulo}',{{permanent:true,direction:'top',offset:[0,-6],className:'tt'}}).addTo(map);" +
                $"pin{id}.bindPopup('{popup}',{{closeButton:false,maxWidth:260,className:'pop-sede',autoPan:false}});" +
                "pin" + id + ".on('mouseover',function(){this.openPopup();});" +
                "pin" + id + ".on('mouseout',function(){this.closePopup();});" +
                $"pin{id}.on('click',function(){{window.location.href='{UrlClicSede}{id}';}});");
        }

        var bounds = $"L.latLngBounds([{string.Join(",", puntosBounds)}])";

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>" +
               "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/leaflet.css\"/>" +
               "<style>html,body,#map{height:100%;width:100%;margin:0;}" +
               ".tt{background:rgba(255,255,255,.95);border:1px solid #CBD5E1;border-radius:6px;font:bold 10px system-ui;color:#1E293B;padding:2px 5px;box-shadow:0 1px 4px rgba(0,0,0,.15);}" +
               ".pop-sede .leaflet-popup-content-wrapper{border-radius:10px;border:1px solid #CBD5E1;box-shadow:0 4px 16px rgba(15,23,42,.22);}" +
               ".pop-sede .leaflet-popup-content{margin:8px 10px;font:11px/1.4 system-ui,Segoe UI,sans-serif;color:#1E293B;}" +
               ".pop-sede strong{font-size:12px;color:#0F172A;display:block;margin-bottom:4px;}" +
               "#leyenda{position:absolute;left:8px;bottom:8px;z-index:1000;background:rgba(255,255,255,.94);padding:5px 9px;border-radius:10px;font:11px system-ui;color:#334155;box-shadow:0 2px 8px rgba(0,0,0,.12);}</style></head><body>" +
               "<div id=\"map\"></div>" +
               "<div id=\"leyenda\">● verde = tú (pasa el mouse para tu dirección) · ● rojo = parqueadero</div>" +
               "<script src=\"https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/leaflet.js\"></script>" +
               "<script>" +
               "function iniciar(){" +
               "if(typeof L==='undefined'){setTimeout(iniciar,120);return;}" +
               $"var map=L.map('map',{{zoomControl:true}}).setView([{oLat},{oLng}],13);" +
               "var calles=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}',{maxZoom:19,attribution:'© Esri'});" +
               "var respaldo=L.tileLayer('https://maps.wikimedia.org/osm-intl/{z}/{x}/{y}.png',{maxZoom:19,attribution:'© OSM'});" +
               "var activa=calles;calles.on('tileerror',function(){if(activa===calles){map.removeLayer(calles);respaldo.addTo(map);activa=respaldo;}});calles.addTo(map);" +
               capas +
               $"map.fitBounds({bounds}.pad(0.1));" +
               "setTimeout(function(){map.invalidateSize();map.fitBounds(" + bounds + ".pad(0.1));},350);" +
               "}" +
               "iniciar();</script></body></html>";
    }

    private static string TextoPopupUsuario(Location origen, string? direccion, bool gpsReal, double? precisionMetros)
    {
        var cultura = CultureInfo.InvariantCulture;
        var dir = string.IsNullOrWhiteSpace(direccion)
            ? $"{origen.Latitude.ToString(cultura)}, {origen.Longitude.ToString(cultura)}"
            : direccion;

        var lineas = new List<string>
        {
            "<strong>Tu ubicación</strong>",
            EscaparHtml(dir)
        };

        if (gpsReal)
        {
            var extra = precisionMetros is > 0 and var p ? $"GPS · precisión ±{p:0} m" : "GPS";
            lineas.Add(EscaparHtml(extra));
        }
        else
            lineas.Add(EscaparHtml("Sin GPS: punto de referencia en Quito"));

        return EscaparJs(string.Join("<br/>", lineas));
    }

    private static string TextoPopupSede(SedeDto sede)
    {
        var libres = sede.PlazasLibresVehiculo > 0 ? sede.PlazasLibresVehiculo : sede.PlazasLibres;
        var plazas = libres <= 0 ? "Sin plazas libres" : $"{libres} plazas disponibles";
        var tarifa = sede.TarifaBase > 0 ? Ui.Tarifa(sede.TarifaBase) : "Consultar tarifa";
        var dir = string.IsNullOrWhiteSpace(sede.Direccion) ? (sede.Sector ?? "—") : sede.Direccion;
        var nombre = sede.Nombre ?? sede.Codigo ?? "Urban Park";
        var lineas = new List<string> { $"<strong>{EscaparHtml(nombre)}</strong>", EscaparHtml(dir), EscaparHtml(plazas), EscaparHtml(tarifa) };
        if (!string.IsNullOrWhiteSpace(sede.DistanciaTexto))
        {
            var extra = sede.DistanciaTexto + (string.IsNullOrWhiteSpace(sede.CaminandoTexto) ? "" : " · " + sede.CaminandoTexto);
            lineas.Add(EscaparHtml(extra));
        }

        return EscaparJs(string.Join("<br/>", lineas));
    }

    private static string EscaparHtml(string texto) =>
        texto.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscaparJs(string texto) =>
        texto.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}