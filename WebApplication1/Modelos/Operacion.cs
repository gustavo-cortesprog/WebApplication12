namespace Vantex;

// Compra o venta virtual registrada con su precio, fuente y resultado realizado.
public class Operacion
{
    public Guid Id
    {
        get; set;
    }
    public string Simbolo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public decimal Cantidad
    {
        get; set;
    }
    public decimal Precio
    {
        get; set;
    }
    public decimal Total
    {
        get; set;
    }
    // Guarda la ganancia o pérdida realizada al vender; en una compra vale cero.
    public decimal Resultado
    {
        get; set;
    }
    public string Fuente { get; set; } = "";
    public string Modo { get; set; } = "";
    public DateTimeOffset Fecha
    {
        get; set;
    }

    public Operacion()
    {
    }

    public Operacion(Guid id, string simbolo, string tipo, decimal cantidad, decimal precio, decimal total, decimal resultado, string fuente, string modo, DateTimeOffset fecha)
    {
        Id = id;
        Simbolo = simbolo;
        Tipo = tipo;
        Cantidad = cantidad;
        Precio = precio;
        Total = total;
        Resultado = resultado;
        Fuente = fuente;
        Modo = modo;
        Fecha = fecha;
    }
}
