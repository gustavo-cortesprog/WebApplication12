using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Vantex;

// Administra el proceso llama.cpp y conecta sus respuestas al tutor de Vantex.
public sealed class TutorLocal : IDisposable
{
    private readonly IHttpClientFactory factory;
    private readonly string carpeta;
    private readonly SemaphoreSlim turno = new(1, 1);
    private readonly object candado = new();
    private readonly string clave = Guid.NewGuid().ToString("N");
    private Process? proceso;
    private Uri? servidor;

    public TutorLocal(IHttpClientFactory factory, IWebHostEnvironment entorno)
    {
        this.factory = factory;
        carpeta = Path.Combine(entorno.ContentRootPath, "App_Data", "tutor-local");
    }

    // La instalación local se detecta por el ejecutable y el archivo del modelo.
    public bool Disponible => OperatingSystem.IsWindows()
        && File.Exists(Path.Combine(carpeta, "llama-server.exe"))
        && File.Exists(Path.Combine(carpeta, "modelo.gguf"));

    // Precarga el modelo sin generar una respuesta de chat.
    public async Task Preparar(CancellationToken cancelacion)
    {
        using var http = Cliente();
        await EsperarModelo(http, cancelacion);
    }

    // Envía la conversación de una en una y devuelve el texto completo al terminar.
    public async Task<string> Responder(List<object> mensajes, CancellationToken cancelacion, Func<string, Task>? enviar = null)
    {
        if (!await turno.WaitAsync(TimeSpan.FromSeconds(15), cancelacion))
            throw new InvalidOperationException("El tutor local está ocupado. Intenta de nuevo.");

        try
        {
            using var http = Cliente();
            var direccion = await EsperarModelo(http, cancelacion);
            using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            tiempo.CancelAfter(TimeSpan.FromSeconds(120));
            using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(direccion, "/v1/chat/completions"));
            peticion.Content = JsonContent.Create(new
            {
                model = "vantex",
                messages = mensajes,
                max_tokens = 180,
                temperature = 0.25,
                stream = enviar is not null
            });
            using var respuesta = await http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, tiempo.Token);
            respuesta.EnsureSuccessStatusCode();
            string? texto;
            if (enviar is not null)
                texto = await LeerFlujo(respuesta.Content, enviar, tiempo.Token);
            else
            {
                using var datos = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(tiempo.Token));
                texto = datos.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }

            if (string.IsNullOrWhiteSpace(texto))
                throw new InvalidOperationException("El tutor local no devolvió una explicación.");
            return texto.Trim();
        }
        finally
        {
            turno.Release();
        }
    }

    // Lee los eventos del modelo y reenvía cada fragmento al navegador conforme llega.
    private static async Task<string> LeerFlujo(HttpContent contenido, Func<string, Task> enviar, CancellationToken cancelacion)
    {
        await using var datos = await contenido.ReadAsStreamAsync(cancelacion);
        using var lector = new StreamReader(datos);
        var texto = new StringBuilder();
        while (await lector.ReadLineAsync(cancelacion) is { } linea)
        {
            if (!linea.StartsWith("data:", StringComparison.Ordinal)) continue;
            var valor = linea[5..].Trim();
            if (valor == "[DONE]") return texto.ToString();
            using var evento = JsonDocument.Parse(valor);
            if (!evento.RootElement.TryGetProperty("choices", out var opciones)
                || opciones.GetArrayLength() == 0
                || !opciones[0].TryGetProperty("delta", out var delta)
                || !delta.TryGetProperty("content", out var contenidoParcial))
                continue;
            var parte = contenidoParcial.GetString();
            if (string.IsNullOrEmpty(parte)) continue;
            texto.Append(parte);
            await enviar(parte);
        }
        throw new InvalidOperationException("Se interrumpió la respuesta del tutor.");
    }

    // Autentica las solicitudes entre procesos locales con una clave generada al iniciar.
    private HttpClient Cliente()
    {
        var http = factory.CreateClient("tutor-local");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clave);
        return http;
    }

    // Reutiliza el proceso o elige un puerto libre de la interfaz de la propia computadora.
    private (Process Proceso, Uri Servidor) Iniciar()
    {
        lock (candado)
        {
            if (proceso is not null && !proceso.HasExited)
                return (proceso, servidor!);

            proceso?.Dispose();
            var puerto = new TcpListener(IPAddress.Loopback, 0);
            puerto.Start();
            int numero = ((IPEndPoint)puerto.LocalEndpoint).Port;
            puerto.Stop();

            var inicio = new ProcessStartInfo(Path.Combine(carpeta, "llama-server.exe"))
            {
                WorkingDirectory = carpeta,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            string[] argumentos =
            [
                "--model", Path.Combine(carpeta, "modelo.gguf"), "--alias", "vantex",
                "--host", "127.0.0.1", "--port", numero.ToString(),
                "--ctx-size", "3072", "--parallel", "1", "--threads", "2", "--n-gpu-layers", "0",
                "--batch-size", "128", "--ubatch-size", "128",
                "--reasoning", "off", "--no-webui", "--log-disable", "--sleep-idle-seconds", "300"
            ];
            inicio.Environment["LLAMA_API_KEY"] = clave;
            foreach (var argumento in argumentos)
                inicio.ArgumentList.Add(argumento);
            proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo iniciar el tutor local.");
            servidor = new Uri($"http://127.0.0.1:{numero}");
            return (proceso, servidor);
        }
    }

    // Consulta la ruta de salud hasta que el modelo esté listo o se cumpla el plazo.
    private async Task<Uri> EsperarModelo(HttpClient http, CancellationToken cancelacion)
    {
        var (actual, direccion) = Iniciar();
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        espera.CancelAfter(TimeSpan.FromSeconds(75));
        var salud = new Uri(direccion, "/health");
        while (!actual.HasExited)
        {
            try
            {
                using var respuesta = await http.GetAsync(salud, espera.Token);
                if (respuesta.IsSuccessStatusCode) return direccion;
            }
            catch (HttpRequestException) { }
            await Task.Delay(300, espera.Token);
        }
        throw new InvalidOperationException("El tutor local se cerró antes de responder.");
    }

    // Cierra el proceso que inició Vantex cuando la aplicación termina.
    public void Dispose()
    {
        lock (candado)
        {
            if (proceso is null) return;
            try
            {
                if (!proceso.HasExited) proceso.Kill();
            }
            catch (InvalidOperationException) { }
            proceso.Dispose();
        }
    }
}

