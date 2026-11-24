using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
namespace Vantex;

// Razor Pages sirve las páginas; estas rutas responden con datos JSON a fetch.
public static class RutasApi
{
    public static void MapVantexApi(this WebApplication app)
    {
        // Indica los servicios activos para que el navegador muestre el estado correcto.
        app.MapGet("/api/estado", (Almacen db, Mercado mercado, Tutor tutor) => new { almacen = db.Estado, sql = db.SqlActivo, ia = tutor.Configurado, tutor = tutor.Modo, tutorEnFlujo = true, movil = true, acciones = mercado.AccionesConfiguradas, version = "Vantex" });
        // La web genera el código con su sesión. El MAUI lo canjea por un token de lectura.
        app.MapPost("/api/movil/codigo", (HttpContext contexto, VinculoMovil mov) => mov.CrearCodigo(Id(contexto)))
            .RequireAuthorization();
        app.MapPost("/api/movil/vincular", (CodigoMovil entrada, VinculoMovil mov) =>
        {
            var token = mov.Vincular(entrada.Codigo);
            return token is null
                ? Results.BadRequest(new { error = "El código venció, ya se usó o es incorrecto." })
                : Results.Ok(new { token });
        }).RequireRateLimiting("vincular-movil");
        app.MapGet("/api/movil/portafolio", async (HttpContext contexto, VinculoMovil mov, Almacen almacen,
            Mercado mercado, CancellationToken cancelacion) =>
        {
            var alumno = mov.AlumnoDe(contexto.Request.Headers.Authorization.ToString());
            return alumno.HasValue ? await VinculoMovil.Portafolio(alumno.Value, almacen, mercado, cancelacion)
                : Results.Unauthorized();
        });
        // Lecturas públicas de tasas, cotizaciones, históricos y lecciones.
        app.MapGet("/api/cetes", (Cetes cetes, CancellationToken cancelacion) => cetes.Consultar(cancelacion));
        app.MapGet("/api/mercado", (string? modo, Mercado mercado) => mercado.Listar(modo ?? "demo"));
        app.MapGet("/api/historial/{simbolo}", (string simbolo, string? modo, Mercado mercado) => mercado.Historia(simbolo, modo ?? "demo"));
        app.MapGet("/api/lecciones", () => Catalogo.Lecciones);
        // Devuelve la sesión actual; el visitante anónimo recibe usuario nulo.
        app.MapGet("/api/sesion", async (HttpContext ctx, Almacen db) =>
        {
            if (ctx.User.Identity == null || !ctx.User.Identity.IsAuthenticated)
                return Results.Ok(new
                {
                    usuario = (object?)null
                });
            await db.Candado.WaitAsync();
            try
            {
                var a = db.Buscar(Id(ctx));
                return Results.Ok(new
                {
                    usuario = a is null ? null : new
                    {
                        a.Id,
                        a.Nombre,
                        a.Email
                    }
                });
            }
            finally { db.Candado.Release(); }
        });
        // Registra una cuenta, genera el hash de la contraseña y abre la sesión web.
        app.MapPost("/api/registro", async (Registro entrada, HttpContext ctx, Almacen db) =>
        {
            var nombre = (entrada.Nombre ?? "").Trim();
            var email = (entrada.Email ?? "").Trim().ToLowerInvariant();
            if (nombre.Length < 2 || nombre.Length > 80 || email.Length > 160 || !System.Net.Mail.MailAddress.TryCreate(email, out var direccion) || direccion.Address != email || entrada.Password == null || entrada.Password.Length < 8 || entrada.Password.Length > 128)
                throw new ArgumentException("Usa un nombre de 2–80 caracteres, correo válido y contraseña de 8–128 caracteres.");
            await db.Candado.WaitAsync();
            Alumno a;
            try
            {
                if (db.PorEmail(email) != null)
                    throw new ArgumentException("Ya existe una cuenta con ese correo.");
                a = new Alumno { Nombre = nombre, Email = email };
                a.Hash = new PasswordHasher<Alumno>().HashPassword(a, entrada.Password!);
                await db.Guardar(a, true);
            }
            finally { db.Candado.Release(); }
            await Sesion(ctx, a);
            return Results.Ok(new
            {
                mensaje = "Tu cuenta educativa tiene 10,000 USD virtuales."
            });
        });
        // Comprueba las credenciales y crea la cookie que autentica al navegador.
        app.MapPost("/api/login", async (Acceso entrada, HttpContext ctx, Almacen db) =>
        {
            await db.Candado.WaitAsync();
            Alumno? a;
            try
            {
                a = db.PorEmail((entrada.Email ?? "").Trim().ToLowerInvariant());
            }
            finally { db.Candado.Release(); }
            if (a is null || string.IsNullOrEmpty(entrada.Password) || new PasswordHasher<Alumno>().VerifyHashedPassword(a, a.Hash, entrada.Password) == PasswordVerificationResult.Failed)
                return Results.Json(new
                {
                    error = "Correo o contraseña incorrectos."
                }, statusCode: 401);
            await Sesion(ctx, a);
            return Results.Ok(new
            {
                mensaje = "Bienvenido de nuevo."
            });
        });
        // Cierra la sesión web y pide al navegador descartar la cookie de acceso.
        app.MapPost("/api/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(new { mensaje = "Sesión cerrada." }); });
        // Calcula el valor y resultado de las posiciones a partir de las cotizaciones disponibles.
        app.MapGet("/api/cuenta", async (HttpContext ctx, Almacen db, Mercado mercado) =>
        {
            await db.Candado.WaitAsync();
            Alumno a;
            try
            {
                a = Actual(ctx, db);
            }
            finally { db.Candado.Release(); }
            var posiciones = new List<object>();
            decimal valorTotal = 0, costoTotal = 0;
            bool completo = true;
            foreach (var p in a.Posiciones)
            {
                Cotizacion? c = null;
                try
                {
                    c = await mercado.Cotizar(p.Simbolo, p.Modo);
                }
                catch { completo = false; }
                decimal? valor = c is null ? null : decimal.Round(c.Precio * p.Cantidad, 4);
                if (valor.HasValue)
                {
                    valorTotal += valor.Value;
                    costoTotal += p.Costo;
                }
                posiciones.Add(new
                {
                    p.Simbolo,
                    p.Modo,
                    p.Cantidad,
                    p.Costo,
                    precio = c?.Precio,
                    valor,
                    resultado = valor - p.Costo
                });
            }
            return Results.Ok(new
            {
                a.Nombre,
                a.Email,
                a.Saldo,
                a.Realizado,
                posiciones,
                operaciones = a.Operaciones.OrderByDescending(o => o.Fecha),
                metas = a.Metas,
                lecciones = a.Lecciones,
                patrimonio = completo ? (decimal?)(a.Saldo + valorTotal) : null,
                noRealizado = completo ? (decimal?)(valorTotal - costoTotal) : null,
                completo
            });
        }).RequireAuthorization();

        // Valida la solicitud y registra la compra o venta virtual de forma atómica.
        app.MapPost("/api/operaciones", async (Orden orden, HttpContext contexto, Almacen almacen, Mercado mercado) =>
        {
            if (orden.SolicitudId == Guid.Empty || (orden.Tipo != "compra" && orden.Tipo != "venta"))
            {
                throw new ArgumentException("La operación no es válida.");
            }

            // La cotización se obtiene en el servidor.
            Cotizacion cotizacion = await mercado.Cotizar(orden.Simbolo, orden.Modo);
            await almacen.Candado.WaitAsync();
            try
            {
                Alumno alumno = Actual(contexto, almacen);
                // Reconoce un envío repetido para evitar registrar la misma operación dos veces.
                if (alumno.TieneOperacion(orden.SolicitudId))
                {
                    return Results.Ok(new
                    {
                        mensaje = "Esta operación ya fue procesada."
                    });
                }

                Operacion operacion;
                string mensaje;
                if (orden.Tipo == "compra")
                {
                    operacion = alumno.Comprar(orden, cotizacion);
                    mensaje = "Compra simulada registrada.";
                }
                else
                {
                    operacion = alumno.Vender(orden, cotizacion);
                    mensaje = "Venta simulada registrada.";
                }

                await almacen.Guardar(alumno);
                return Results.Ok(new
                {
                    mensaje,
                    precio = operacion.Precio,
                    total = operacion.Total,
                    resultado = operacion.Resultado,
                    modo = operacion.Modo
                });
            }
            finally
            {
                almacen.Candado.Release();
            }
        }).RequireAuthorization();
        // La proyección se puede probar sin cuenta. Guardarla requiere una sesión.
        app.MapPost("/api/metas/proyectar", (MetaEntrada m) => Results.Ok(CalculadoraMetas.Proyectar(m)));
        app.MapPost("/api/metas", async (MetaEntrada m, HttpContext ctx, Almacen db) =>
        {
            var proyeccion = CalculadoraMetas.Proyectar(m);
            await db.Candado.WaitAsync();
            try
            {
                var a = Actual(ctx, db);
                if (a.Metas.Count >= 20)
                    throw new ArgumentException("Puedes guardar hasta 20 metas.");
                a.Metas.Add(new(Guid.NewGuid(), m.Nombre.Trim(), m.Costo, m.Inicial, m.Mensual, m.TasaAnual));
                await db.Guardar(a);
                return Results.Ok(proyeccion);
            }
            finally { db.Candado.Release(); }
        }).RequireAuthorization();
        // Comprueba el cuestionario en el servidor y registra el avance del alumno.
        app.MapPost("/api/lecciones/{id}/responder", async (string id, RespuestaQuiz respuesta, HttpContext ctx, Almacen db) =>
        {
            var l = Catalogo.Lecciones.FirstOrDefault(l => l.Id == id) ?? throw new ArgumentException("Lección no válida.");
            if (respuesta.Respuesta < 0 || respuesta.Respuesta >= l.Opciones.Length)
                throw new ArgumentException("Selecciona una respuesta.");
            var correcta = respuesta.Respuesta == l.Correcta;
            if (correcta)
            {
                await db.Candado.WaitAsync();
                try
                {
                    var a = Actual(ctx, db);
                    if (!a.Lecciones.Contains(id))
                    {
                        a.Lecciones.Add(id);
                        await db.Guardar(a);
                    }
                }
                finally { db.Candado.Release(); }
            }
            return Results.Ok(new
            {
                correcta,
                l.Explicacion
            });
        }).RequireAuthorization();
        // Exposición del tutor para respuesta completa, precarga y respuesta por fragmentos.
        app.MapPost("/api/chat", async (Pregunta p, Tutor tutor, CancellationToken cancelacion) => Results.Ok(await tutor.Responder(p, cancelacion))).RequireAuthorization();
        app.MapPost("/api/chat/preparar", async (Tutor tutor, CancellationToken cancelacion) =>
            Results.Ok(new { preparado = await tutor.Preparar(cancelacion) })).RequireAuthorization();

        app.MapPost("/api/chat/stream", async (Pregunta pregunta, Tutor tutor, HttpContext contexto) =>
        {
            contexto.Response.ContentType = "application/x-ndjson; charset=utf-8";
            var cancelacion = contexto.RequestAborted;

            async Task Escribir(object datos)
            {
                await contexto.Response.WriteAsync(JsonSerializer.Serialize(datos) + "\n", cancelacion);
                await contexto.Response.Body.FlushAsync(cancelacion);
            }

            try
            {
                var resultado = await tutor.Responder(pregunta, cancelacion, tramo => Escribir(new { tramo }));
                await Escribir(new { resultado });
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested) { }
        }).RequireAuthorization();

    }


    // Lee la identidad de la cookie firmada; la sesión se crea al registrar o iniciar sesión.
    static Guid Id(HttpContext ctx) => Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static Alumno Actual(HttpContext ctx, Almacen db) => db.Buscar(Id(ctx)) ?? throw new InvalidOperationException("Vuelve a iniciar sesión.");
    static async Task Sesion(HttpContext ctx, Alumno a) => await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, a.Id.ToString()), new Claim(ClaimTypes.Name, a.Nombre)], CookieAuthenticationDefaults.AuthenticationScheme)));

}
