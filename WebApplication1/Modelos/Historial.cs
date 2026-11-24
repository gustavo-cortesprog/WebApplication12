namespace Vantex;

// Serie de cierres o precios de demostración. Los puntos alimentan la gráfica del navegador.
public class Historial
{
    public string Simbolo { get; set; } = "";
    public string Fuente { get; set; } = "";
    public string Modo { get; set; } = "";
    // Cada punto asocia una fecha con un precio para trazar la serie.
    public List<Punto> Puntos { get; set; } = new List<Punto>();

    public Historial()
    {
    }

    public Historial(string simbolo, string fuente, string modo, List<Punto> puntos)
    {
        Simbolo = simbolo;
        Fuente = fuente;
        Modo = modo;
        Puntos = puntos;
    }
}
