namespace Vantex;

// Saldo estimado de un mes para dibujar la curva de ahorro.
public class PuntoAhorro
{
    public int Mes
    {
        get; set;
    }
    public double Saldo
    {
        get; set;
    }
}

// Resumen del plazo y los puntos calculados bajo la tasa elegida.
public class ProyeccionMeta
{
    // Queda sin valor cuando la proyección no alcanza el objetivo dentro del horizonte calculado.
    public int? Meses
    {
        get; set;
    }
    public decimal SinRendimiento
    {
        get; set;
    }
    public List<PuntoAhorro> Puntos { get; set; } = new List<PuntoAhorro>();
    public string Consejo { get; set; } = "";
    public string Nota { get; set; } = "";
}
