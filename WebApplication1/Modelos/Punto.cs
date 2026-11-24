namespace Vantex;

// Par de fecha y precio usado para dibujar una serie de mercado.
public class Punto
{
    public DateTimeOffset Fecha
    {
        get; set;
    }
    public decimal Precio
    {
        get; set;
    }

    public Punto()
    {
    }

    public Punto(DateTimeOffset fecha, decimal precio)
    {
        Fecha = fecha;
        Precio = precio;
    }
}
