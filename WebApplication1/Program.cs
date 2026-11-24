using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Vantex;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

// Punto de entrada. Configura las páginas, servicios y rutas antes de arrancar.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys"))).SetApplicationName("VantexNova");
// Activa las páginas .cshtml y sus clases PageModel.
builder.Services.AddRazorPages();
// Una instancia por aplicación comparte las cachés y los candados de acceso a datos.
builder.Services.AddSingleton<Almacen>();
builder.Services.AddSingleton<Mercado>();
builder.Services.AddSingleton<Cetes>();
builder.Services.AddSingleton<Tutor>();
builder.Services.AddSingleton<TutorLocal>();
builder.Services.AddSingleton<VinculoMovil>();
// Limita los intentos de canje desde la misma dirección sin afectar login ni los demás módulos.
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.AddPolicy("vincular-movil", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));
});
// Los servicios HTTP tienen tiempos de espera distintos para IA local y proveedores de mercado.
builder.Services.AddHttpClient("tutor-local", c => c.Timeout = TimeSpan.FromSeconds(120))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
builder.Services.AddHttpClient("externo", c => { c.Timeout = TimeSpan.FromSeconds(20); c.DefaultRequestHeaders.UserAgent.ParseAdd("Vantex-Educativo/1.0"); });
// La web usa cookies. Las solicitudes de la API devuelven códigos HTTP cuando falta sesión.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "Vantex.Sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
var app = builder.Build();
await app.Services.GetRequiredService<Almacen>().Iniciar();
// Aplica cabeceras, valida formularios de la API y devuelve errores en JSON.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (ctx.Request.ContentLength > 16000)
        {
            ctx.Response.StatusCode = 413;
            return;
        }
        if (ctx.Request.Method is "POST" or "PUT" or "DELETE" && ctx.Request.Headers["X-Vantex"] != "1")
        {
            ctx.Response.StatusCode = 403;
            return;
        }
    }
    try
    {
        await next();
    }
    catch (ArgumentException ex) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (Exception ex) { app.Logger.LogError("Fallo de solicitud: {Tipo}", ex.GetType().Name); ctx.Response.StatusCode = 503; await ctx.Response.WriteAsJsonAsync(new { error = "No fue posible completar la solicitud. Revisa la conexión y vuelve a intentarlo." }); }
});
// La página inicial se sirve con Razor Pages.
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
// Aplica los permisos antes de poner las páginas y rutas a disposición del navegador.
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapVantexApi();
app.Run();
