using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Vantex;

// Empareja un dispositivo móvil con un alumno mediante un código temporal.
// El token obtenido solo identifica al alumno para consultar sus posiciones virtuales.
public sealed class VinculoMovil
{
    private readonly ITimeLimitedDataProtector protector;
    private readonly Dictionary<string, Codigo> codigos = new();
    private readonly object candado = new();
    private record Codigo(Guid Alumno, DateTimeOffset Expira);

    public VinculoMovil(IDataProtectionProvider proteccion)
    {
        protector = proteccion.CreateProtector("Vantex.Movil.Portafolio.v1").ToTimeLimitedDataProtector();
    }

    // Solo una sesión web autenticada puede pedir el código. Dura cinco minutos y se usa una vez.
    public object CrearCodigo(Guid alumno)
    {
        lock (candado)
        {
            var ahora = DateTimeOffset.UtcNow;
            foreach (var anterior in codigos.Where(c => c.Value.Expira <= ahora || c.Value.Alumno == alumno).ToArray())
                codigos.Remove(anterior.Key);

            string codigo;
            do { codigo = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)); }
            while (codigos.ContainsKey(codigo));
            codigos[codigo] = new Codigo(alumno, ahora.AddMinutes(5));
            return new { codigo = codigo.Insert(5, "-"), minutos = 5 };
        }
    }

    // La extracción bajo el candado evita que dos teléfonos canjeen el mismo código.
    public string? Vincular(string? entrada)
    {
        if (entrada is null || entrada.Length > 20) return null;
        var codigo = entrada.Trim().Replace("-", "").ToUpperInvariant();
        lock (candado)
        {
            if (!codigos.Remove(codigo, out var dato) || dato.Expira <= DateTimeOffset.UtcNow)
                return null;
            return protector.Protect(dato.Alumno.ToString(), TimeSpan.FromDays(30));
        }
    }

    // Valida el token antes de acceder a la cuenta. Los tokens no sirven para registrar operaciones.
    public Guid? AlumnoDe(string encabezado)
    {
        if (encabezado.Length > 4096 || !encabezado.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var id = protector.Unprotect(encabezado[7..].Trim());
            return Guid.TryParse(id, out var alumno) ? alumno : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // Copia las posiciones bajo el candado SQL y libera el recurso antes de pedir cotizaciones.
    public static async Task<IResult> Portafolio(Guid id, Almacen almacen, Mercado mercado, CancellationToken cancelacion)
    {
        Posicion[] posiciones;
        await almacen.Candado.WaitAsync(cancelacion);
        try
        {
            var alumno = almacen.Buscar(id);
            if (alumno is null) return Results.Unauthorized();
            posiciones = alumno.Posiciones.ToArray();
        }
        finally { almacen.Candado.Release(); }

        var resultados = new List<object>();
        foreach (var posicion in posiciones)
        {
            cancelacion.ThrowIfCancellationRequested();
            Cotizacion? cotizacion = null;
            try { cotizacion = await mercado.Cotizar(posicion.Simbolo, posicion.Modo); }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException) { }
            decimal? valor = cotizacion is null ? null : decimal.Round(cotizacion.Precio * posicion.Cantidad, 4);
            resultados.Add(new
            {
                posicion.Simbolo, posicion.Modo, posicion.Cantidad, posicion.Costo,
                precio = cotizacion?.Precio, resultado = valor - posicion.Costo,
                actualizado = cotizacion?.Consultado
            });
        }
        return Results.Ok(new { posiciones = resultados, consulta = DateTimeOffset.UtcNow });
    }
}

// Formato del cuerpo JSON al canjear un código desde la aplicación.
public sealed class CodigoMovil
{
    public string Codigo { get; set; } = "";
}
