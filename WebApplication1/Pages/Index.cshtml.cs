using Microsoft.AspNetCore.Mvc.RazorPages;
using Vantex;

namespace WebApplication1.Pages;

// Entrega a la página los datos generales del proyecto y el total de lecciones.
public class IndexModel : PageModel
{
    public string Equipo { get; private set; } = "Nova Systems";
    public int TotalLecciones
    {
        get; private set;
    }

    // Cuenta las lecciones del catálogo antes de renderizar la página principal.
    public void OnGet()
    {
        TotalLecciones = Catalogo.Lecciones.Length;
    }
}
