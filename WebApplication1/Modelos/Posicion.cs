namespace Vantex;

// Cantidad acumulada y costo de un activo en un modo. Mercado y Demostración se contabilizan por separado.
public class Posicion
{
    public string Simbolo { get; set; } = "";
    public string Modo { get; set; } = "";
    public decimal Cantidad
    {
        get; set;
    }
    // Importe acumulado de las unidades que siguen en el portafolio.
    public decimal Costo
    {
        get; set;
    }

    public Posicion()
    {
    }

    public Posicion(string simbolo, string modo, decimal cantidad, decimal costo)
    {
        Simbolo = simbolo;
        Modo = modo;
        Cantidad = cantidad;
        Costo = costo;
    }
}
