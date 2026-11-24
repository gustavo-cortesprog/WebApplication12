using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Vantex;

// Recupera las tasas anuales publicadas por Banxico y las comparte entre consultas.
public sealed class Cetes
{
    private const string Fuente = "https://www.banxico.org.mx/tipcamb/llenarTasasInteresAction.do?idioma=sp";
    // Son los días al vencimiento que se muestran en el selector de CETES.
    private static readonly int[] Plazos = [28, 91, 182, 364];
    private readonly IHttpClientFactory factory;
    private readonly SemaphoreSlim candado = new(1, 1);
    private Subasta? ultima;
    private DateTimeOffset siguienteConsulta;
    private bool consultaFallida;

    private record Tasa(int Dias, decimal? Rendimiento);
    private record Subasta(DateOnly Fecha, DateTimeOffset Consultada, Tasa[] Tasas);

    public Cetes(IHttpClientFactory factory)
    {
        this.factory = factory;
    }

    // Usa una caché de 60 segundos y conserva la última lectura correcta ante fallos.
    public async Task<object> Consultar(CancellationToken cancelacion)
    {
        await candado.WaitAsync(cancelacion);
        try
        {
            if (DateTimeOffset.UtcNow >= siguienteConsulta)
            {
                try
                {
                    using var http = factory.CreateClient("externo");
                    var html = await http.GetStringAsync(Fuente, cancelacion);
                    var subasta = LeerSubasta(WebUtility.HtmlDecode(html));
                    if (ultima is not null && subasta.Fecha < ultima.Fecha)
                        throw new InvalidOperationException("Banxico devolvió una subasta anterior.");
                    ultima = subasta;
                    consultaFallida = false;
                }
                catch (Exception error) when (!cancelacion.IsCancellationRequested
                    && error is HttpRequestException or OperationCanceledException or InvalidOperationException)
                {
                    consultaFallida = true;
                }
                siguienteConsulta = DateTimeOffset.UtcNow.AddSeconds(60);
            }

            return new
            {
                fechaSubasta = ultima?.Fecha,
                consultado = ultima?.Consultada,
                tasas = ultima?.Tasas ?? [],
                consultaFallida
            };
        }
        finally
        {
            candado.Release();
        }
    }

    // Lee la fecha y las cuatro tasas de la tabla oficial de la última subasta.
    private static Subasta LeerSubasta(string html)
    {
        var fechaHtml = Regex.Match(html, @"Resultados de la última subasta[^<]*</a>\s*</td>\s*</tr>\s*<tr>\s*<td\b[^>]*>\s*(\d{2}/\d{2}/\d{4})",
            RegexOptions.IgnoreCase);
        if (!DateOnly.TryParseExact(fechaHtml.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fecha))
            throw new InvalidOperationException("Banxico no devolvió la fecha de subasta.");

        var tasas = new List<Tasa>();
        for (int i = 0; i < Plazos.Length; i++)
        {
            var valor = Regex.Match(html, $@"<div\s+id=""CA{i}_DATO""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);
            bool disponible = decimal.TryParse(valor.Groups[1].Value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var tasa) && tasa is >= 0 and <= 100;
            tasas.Add(new Tasa(Plazos[i], disponible ? tasa : null));
        }
        if (tasas.All(t => t.Rendimiento is null))
            throw new InvalidOperationException("Banxico no devolvió tasas de CETES.");
        return new Subasta(fecha, DateTimeOffset.UtcNow, tasas.ToArray());
    }
}
