using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
namespace Vantex;

// Conecta el catálogo con Coinbase, Alpha Vantage y las series ficticias de demostración.
public class Mercado
{
    private readonly IHttpClientFactory factory;
    private readonly IConfiguration config;

    public Mercado(IHttpClientFactory factory, IConfiguration config)
    {
        this.factory = factory;
        this.config = config;
    }

    // Conserva precios, históricos y ventanas de espera para evitar consultas repetidas.
    private readonly ConcurrentDictionary<string, Cotizacion> cache = new();
    private readonly ConcurrentDictionary<string, List<Punto>> observados = new();
    private readonly ConcurrentDictionary<string, Historial> historicos = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset Hasta, string Error)> fallos = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> historicoConsultado = new();
    private readonly SemaphoreSlim candado = new(1, 1);
    // La web usa este indicador para saber si tiene habilitado el proveedor de acciones.
    public bool AccionesConfiguradas => !string.IsNullOrWhiteSpace(config["Mercado:AlphaVantageKey"]);
    // Calcula un precio ficticio reproducible con el activo y la hora, sin conexión externa.
    public static decimal PrecioDemo(Activo a, DateTimeOffset t)
    {
        var n = Array.IndexOf(Catalogo.Activos, a) + 1;
        var x = t.ToUnixTimeSeconds() / 600.0;
        return decimal.Round(a.Referencia * (decimal)(1 + 0.025 * Math.Sin(x + n) + 0.012 * Math.Cos(x / 3 + n)), 8);
    }
    // Selecciona el proveedor, valida el precio y agrega la muestra a las cotizaciones observadas.
    public async Task<Cotizacion> Cotizar(string simbolo, string modo)
    {
        var a = Catalogo.Activos.FirstOrDefault(x => x.Simbolo == simbolo) ?? throw new ArgumentException("Activo no válido.");
        var ahora = DateTimeOffset.UtcNow;
        if (modo == "demo")
            return new(a.Simbolo, a.Nombre, a.Tipo, PrecioDemo(a, ahora), "Generador educativo", "demo", ahora, "Precio ficticio. No representa el mercado.");
        if (modo != "real")
            throw new ArgumentException("Modo no válido.");
        var derecho = config["Mercado:StockEntitlement"] ?? "";
        bool tiempoReal = a.Tipo == "Cripto" || derecho == "realtime";
        var ttl = tiempoReal ? TimeSpan.FromSeconds(15) : derecho == "delayed" ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(12);
        if (fallos.TryGetValue(simbolo, out var fallo) && ahora < fallo.Hasta)
            throw new InvalidOperationException(fallo.Error);
        await candado.WaitAsync();
        try
        {
            if (cache.TryGetValue(simbolo, out var anterior) && ahora - anterior.Consultado < ttl)
                return anterior;
            var http = factory.CreateClient("externo");
            decimal precio;
            string fuente, nota;
            if (a.Tipo == "Cripto")
            {
                using var datos = await http.GetFromJsonAsync<JsonDocument>($"https://api.coinbase.com/v2/prices/{a.Simbolo}-USD/spot") ?? throw new HttpRequestException();
                precio = decimal.Parse(datos.RootElement.GetProperty("data").GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
                fuente = "Coinbase spot";
                nota = "Consulta periódica; hora de recepción del servidor. No incluye comisiones.";
            }
            else
            {
                var key = config["Mercado:AlphaVantageKey"];
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Acciones: falta configurar Alpha Vantage. Puedes practicar en modo demostración.");
                var extra = derecho is "realtime" or "delayed" ? "&entitlement=" + derecho : "";
                using var datos = await http.GetFromJsonAsync<JsonDocument>($"https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol={a.Simbolo}&apikey={Uri.EscapeDataString(key)}{extra}") ?? throw new HttpRequestException();
                if (!datos.RootElement.TryGetProperty("Global Quote", out var q) || !q.TryGetProperty("05. price", out var valor))
                    throw new InvalidOperationException("El proveedor no devolvió cotización: revisa cuota y permisos del plan.");
                precio = decimal.Parse(valor.GetString()!, CultureInfo.InvariantCulture);
                fuente = "Alpha Vantage";
                var dia = q.TryGetProperty("07. latest trading day", out var fecha) ? fecha.GetString() : "sin fecha";
                nota = $"Sesión reportada: {dia}. " + (derecho == "realtime" ? "Tiempo real solicitado; requiere plan habilitado." : derecho == "delayed" ? "Retraso de 15 minutos solicitado; requiere plan habilitado." : "Cotización diaria al cierre; no es intradía.");
            }
            if (precio <= 0 || precio > 100000000)
                throw new InvalidOperationException("Cotización fuera de rango.");
            var c = new Cotizacion(a.Simbolo, a.Nombre, a.Tipo, precio, fuente, "real", DateTimeOffset.UtcNow, nota) { TiempoReal = tiempoReal };
            cache[simbolo] = c;
            var lista = observados.GetOrAdd(simbolo, _ => []);
            lock (lista)
            {
                lista.Add(new(c.Consultado, c.Precio));
                if (lista.Count > 300)
                    lista.RemoveAt(0);
            }
            return c;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            fallos[simbolo] = (DateTimeOffset.UtcNow.AddMinutes(5), ex is InvalidOperationException ? ex.Message : "Proveedor no disponible. Próximo intento en unos minutos.");
            throw;
        }
        finally { candado.Release(); }
    }
    // Devuelve todo el catálogo e indica los activos sin cotización.
    public async Task<object> Listar(string modo)
    {
        var lista = new List<object>();
        foreach (var a in Catalogo.Activos)
        {
            try
            {
                lista.Add(await Cotizar(a.Simbolo, modo));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                lista.Add(new
                {
                    simbolo = a.Simbolo,
                    nombre = a.Nombre,
                    tipo = a.Tipo,
                    precio = (decimal?)null,
                    fuente = "Sin cotización",
                    modo = "no-disponible",
                    consultado = (DateTimeOffset?)null,
                    nota = ex is InvalidOperationException ? ex.Message : "No se pudo consultar el proveedor. No se inventó un precio."
                });
            }
        }
        return lista;
    }
    // Devuelve cierres del proveedor o las muestras recibidas cuando falta el histórico.
    public async Task<Historial> Historia(string simbolo, string modo)
    {
        var a = Catalogo.Activos.FirstOrDefault(x => x.Simbolo == simbolo) ?? throw new ArgumentException("Activo no válido.");
        if (modo == "demo")
        {
            var ahora = DateTimeOffset.UtcNow;
            List<Punto> serie = new List<Punto>();
            for (int i = 0; i < 49; i++)
            {
                DateTimeOffset fecha = ahora.AddMinutes((i - 48) * 30);
                serie.Add(new Punto(fecha, PrecioDemo(a, fecha)));
            }
            return new Historial(simbolo, "Serie ficticia educativa", "demo", serie);
        }
        if (modo != "real")
            throw new ArgumentException("Modo no válido.");
        if (historicos.TryGetValue(simbolo, out var h) && h.Puntos.Count > 0 && historicoConsultado.TryGetValue(simbolo, out var ultima) && DateTimeOffset.UtcNow - ultima < TimeSpan.FromMinutes(a.Tipo == "Cripto" ? 30 : 720))
            return h;
        try
        {
            var http = factory.CreateClient("externo");
            List<Punto> puntos = new List<Punto>();
            if (a.Tipo == "Cripto")
            {
                using var datos = await http.GetFromJsonAsync<JsonDocument>($"https://api.exchange.coinbase.com/products/{simbolo}-USD/candles?granularity=3600") ?? throw new HttpRequestException();
                puntos = datos.RootElement.EnumerateArray().Select(v => new Punto(DateTimeOffset.FromUnixTimeSeconds(v[0].GetInt64()), v[4].GetDecimal())).OrderBy(v => v.Fecha).TakeLast(48).ToList();
            }
            else
            {
                var key = config["Mercado:AlphaVantageKey"];
                if (string.IsNullOrWhiteSpace(key))
                    throw new HttpRequestException();
                using var datos = await http.GetFromJsonAsync<JsonDocument>($"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={simbolo}&apikey={Uri.EscapeDataString(key)}") ?? throw new HttpRequestException();
                if (datos.RootElement.TryGetProperty("Time Series (Daily)", out var serie))
                    puntos = serie.EnumerateObject().Select(p => new Punto(DateTimeOffset.Parse(p.Name + "T00:00:00Z"), decimal.Parse(p.Value.GetProperty("4. close").GetString()!, CultureInfo.InvariantCulture))).OrderBy(p => p.Fecha).TakeLast(60).ToList();
            }
            if (puntos.Count > 0)
            {
                h = new(simbolo, a.Tipo == "Cripto" ? "Coinbase Exchange · cierres horarios" : "Alpha Vantage · cierres diarios", "real", puntos);
                historicos[simbolo] = h;
                historicoConsultado[simbolo] = DateTimeOffset.UtcNow;
                return h;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        var vistos = observados.GetOrAdd(simbolo, _ => []);
        lock (vistos)
            return new(simbolo, "Solo cotizaciones observadas en esta sesión; histórico no disponible", "real", vistos.ToList());
    }
}
