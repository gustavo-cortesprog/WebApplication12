namespace Vantex;

// Elemento del catálogo con símbolo, nombre, clase de activo y precio base para la demostración.
public class Activo
{
    public string Simbolo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public decimal Referencia
    {
        get; set;
    }

    public Activo()
    {
    }

    // Recibe el símbolo, la categoría y el precio inicial que se usa para generar datos de demostración.
    public Activo(string simbolo, string nombre, string tipo, decimal referencia)
    {
        Simbolo = simbolo;
        Nombre = nombre;
        Tipo = tipo;
        Referencia = referencia;
    }
}
