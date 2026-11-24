using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Vantex;

// Decide cómo responder una pregunta y conserva el contexto reciente que recibió del navegador.
public class Tutor
{
    private readonly IHttpClientFactory factory;
    private readonly IConfiguration config;
    private readonly TutorLocal local;
    private readonly ILogger<Tutor> registro;

    public Tutor(IHttpClientFactory factory, IConfiguration config, TutorLocal local, ILogger<Tutor> registro)
    {
        this.factory = factory;
        this.config = config;
        this.local = local;
        this.registro = registro;
    }

    // Indica al modelo el rol educativo y los datos fijos de Vantex.
    private const string Instrucciones = """
        Eres el tutor educativo de Vantex. Responde preguntas abiertas de cualquier tema en el idioma del usuario.
        Usa el contexto de la conversación y empieza con la respuesta directa. Responde en texto plano,
        sin tablas ni encabezados. Si no te piden un formato específico, escribe entre dos y cuatro frases claras.
        Pide aclaración si falta información. Si no puedes comprobar un dato, dilo. Evita inventar nombres o cifras.
        En finanzas explica conceptos y riesgos sin recomendar operaciones concretas ni prometer ganancias.
        Hecho sobre CETES: son deuda del Gobierno de México. Se compran con descuento y se cobra su valor
        nominal al vencimiento. No pagan intereses periódicos. El rendimiento es la diferencia entre ambos importes.
        Una acción representa una participación en una empresa. Los dividendos dependen de la empresa.
        Vantex simula compras y ventas con dinero virtual. Cripto consulta Coinbase y Acciones consulta Alpha Vantage.
        La sección CETES muestra las últimas tasas de subasta de Banxico. En SOFIPOs y bancos, el alumno ingresa la tasa hipotética.
        En demostración, los precios de Cripto y Acciones son ficticios. No puedes ver la cuenta del alumno ni consultar tasas actuales.
        """;

    // El proveedor externo se usa solo si se configuró una clave. La web recibe el modo disponible mediante la API de estado.
    private bool UsaOpenAI => !string.IsNullOrWhiteSpace(config["IA:OpenAIKey"]);
    public string Modo => UsaOpenAI ? "ia" : local.Disponible ? "local" : "biblioteca";
    public bool Configurado => Modo != "biblioteca";

    // Inicia el motor local al entrar a la pantalla, antes de enviar una pregunta.
    public async Task<bool> Preparar(CancellationToken cancelacion)
    {
        if (Modo != "local") return false;
        try
        {
            await local.Preparar(cancelacion);
            return true;
        }
        catch (Exception ex) when (!cancelacion.IsCancellationRequested && Fallo(ex))
        {
            return false;
        }
    }

    // Intenta OpenAI si está configurado, luego IA local y finalmente biblioteca.
    public async Task<object> Responder(Pregunta pregunta, CancellationToken cancelacion, Func<string, Task>? enviar = null)
    {
        var mensaje = pregunta.Mensaje?.Trim();
        if (string.IsNullOrWhiteSpace(mensaje) || mensaje.Length > 1500)
            throw new ArgumentException("Escribe una pregunta de 1 a 1500 caracteres.");

        var conversacion = new List<object>();
        foreach (var turno in (pregunta.Historial ?? []).TakeLast(4))
        {
            if (turno?.Rol is ("user" or "assistant") && !string.IsNullOrWhiteSpace(turno.Texto))
                conversacion.Add(new { role = turno.Rol, content = turno.Texto[..Math.Min(600, turno.Texto.Length)] });
        }
        conversacion.Add(new { role = "user", content = mensaje });

        if (UsaOpenAI)
        {
            try
            {
                var texto = await ResponderOpenAI(conversacion, cancelacion);
                return new { modo = "ia", respuesta = texto, nota = "Explicación generada por IA." };
            }
            catch (Exception ex) when (!cancelacion.IsCancellationRequested && Fallo(ex))
            {
                registro.LogWarning("OpenAI no respondió al tutor: {Tipo}", ex.GetType().Name);
            }
        }

        if (local.Disponible)
        {
            try
            {
                conversacion.Insert(0, new { role = "system", content = Instrucciones });
                var texto = await local.Responder(conversacion, cancelacion, enviar);
                return new { modo = "local", respuesta = texto, nota = "Explicación generada en esta computadora." };
            }
            catch (Exception ex) when (!cancelacion.IsCancellationRequested && Fallo(ex))
            {
                registro.LogWarning("El tutor local no respondió: {Tipo}", ex.GetType().Name);
            }
        }

        return new
        {
            modo = "biblioteca",
            respuesta = Biblioteca(mensaje),
            nota = Configurado ? "La IA no está disponible por el momento." : "Biblioteca educativa de Vantex."
        };
    }

    private static bool Fallo(Exception ex) => ex is HttpRequestException or OperationCanceledException
        or JsonException or InvalidOperationException or KeyNotFoundException or Win32Exception;

    // Solicita una respuesta al proveedor externo con la clave guardada en el servidor.
    private async Task<string> ResponderOpenAI(List<object> conversacion, CancellationToken cancelacion)
    {
        using var http = factory.CreateClient("externo");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["IA:OpenAIKey"]);
        peticion.Content = JsonContent.Create(new
        {
            model = config["IA:Modelo"] ?? "gpt-4.1-mini",
            store = false,
            max_output_tokens = 600,
            instructions = Instrucciones,
            input = conversacion
        });
        using var respuesta = await http.SendAsync(peticion, cancelacion);
        respuesta.EnsureSuccessStatusCode();
        using var datos = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancelacion));
        var texto = new List<string>();
        foreach (var item in datos.RootElement.GetProperty("output").EnumerateArray())
            if (item.TryGetProperty("content", out var contenido))
                foreach (var bloque in contenido.EnumerateArray())
                    if (bloque.GetProperty("type").GetString() == "output_text")
                        texto.Add(bloque.GetProperty("text").GetString() ?? "");
        var explicacion = string.Join("\n", texto).Trim();
        if (explicacion.Length == 0)
            throw new InvalidOperationException("El tutor no devolvió una explicación.");
        return explicacion;
    }

    // Devuelve explicaciones educativas disponibles incluso cuando la IA falla.
    private static string Biblioteca(string texto)
    {
        var p = texto.ToLowerInvariant();
        if (p.Contains("meta") || p.Contains("carro") || p.Contains("teléfono") || p.Contains("ahorr"))
            return "Para planear una meta define su costo, tu ahorro actual y una aportación mensual sostenible. Primero revisa el escenario sin rendimiento. Compara después escenarios hipotéticos, incluyendo uno negativo: la inversión puede retrasar la meta. Para gastos cercanos, considera disponibilidad y riesgo. Puedes usar Mis metas para explorar estos supuestos.";
        if (p.Contains("divers"))
            return "Diversificar significa distribuir la exposición entre activos. Puede reducir la dependencia de uno solo, pero no garantiza ganancias. Tener varias criptomonedas puede mantener una concentración importante en el mismo sector.";
        if (p.Contains("cripto") || p.Contains("bitcoin"))
            return "Una criptomoneda es un activo digital. Su precio puede cambiar intensamente y existen riesgos tecnológicos y de custodia. En el simulador usas dinero virtual para aprender; una ganancia simulada no predice resultados con dinero real.";
        if (p.Contains("acción") || p.Contains("acciones"))
            return "Una acción representa una participación en una empresa. Puedes obtener una ganancia o pérdida por su precio, y algunas pagan dividendos. Vantex calcula el resultado por precio; todavía no modela dividendos, impuestos ni comisiones.";
        if (p.Contains("ganan") || p.Contains("pérd") || p.Contains("perd"))
            return "El resultado no realizado compara el valor actual de tus posiciones con su costo de compra. Al vender se realiza la ganancia o pérdida. Ejemplo: comprar por 100 USD y vender por 90 USD realiza una pérdida de 10 USD, sin considerar costos.";
        return "La biblioteca no tiene una explicación para esa pregunta. Vuelve a intentarlo cuando esté disponible el tutor de IA.";
    }
}
