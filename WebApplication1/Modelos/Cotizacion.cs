namespace Vantex;

// Precio de un activo con fuente, modo y hora en que el servidor lo consultó.
public class Cotizacion
{
    public string Simbolo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public decimal Precio
    {
        get; set;
    }
    public string Fuente { get; set; } = "";
    public string Modo { get; set; } = "";
    // Registra la hora de consulta para mostrar la antigüedad del dato.
    public DateTimeOffset Consultado
    {
        get; set;
    }
    public string Nota { get; set; } = "";
    public bool TiempoReal { get; set; }

    public Cotizacion()
    {
    }

    public Cotizacion(string simbolo, string nombre, string tipo, decimal precio, string fuente, string modo, DateTimeOffset consultado, string nota)
    {
        Simbolo = simbolo;
        Nombre = nombre;
        Tipo = tipo;
        Precio = precio;
        Fuente = fuente;
        Modo = modo;
        Consultado = consultado;
        Nota = nota;
    }
}
