namespace Vantex;

// Objetivo de ahorro del alumno, con monto objetivo, capital inicial y aportación mensual.
public class Meta
{
    public Guid Id
    {
        get; set;
    }
    public string Nombre { get; set; } = "";
    public decimal Costo
    {
        get; set;
    }
    public decimal Inicial
    {
        get; set;
    }
    public decimal Mensual
    {
        get; set;
    }
    // Supuesto empleado para proyectar el saldo de la meta.
    public double TasaAnual
    {
        get; set;
    }

    public Meta()
    {
    }

    public Meta(Guid id, string nombre, decimal costo, decimal inicial, decimal mensual, double tasaAnual)
    {
        Id = id;
        Nombre = nombre;
        Costo = costo;
        Inicial = inicial;
        Mensual = mensual;
        TasaAnual = tasaAnual;
    }
}
