namespace Vantex;

// Cuenta educativa que conserva saldo virtual, posiciones, operaciones y avance de Academia.
public class Alumno
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string Hash { get; set; } = "";
    public decimal Saldo { get; set; } = 10000;
    public decimal Realizado
    {
        get; set;
    }
    public List<Posicion> Posiciones { get; set; } = new List<Posicion>();
    public List<Operacion> Operaciones { get; set; } = new List<Operacion>();
    public List<Meta> Metas { get; set; } = new List<Meta>();
    public List<string> Lecciones { get; set; } = new List<string>();

    // Detecta un reintento del mismo identificador para evitar registrar dos veces.
    public bool TieneOperacion(Guid id)
    {
        foreach (Operacion operacion in Operaciones)
        {
            if (operacion.Id == id)
            {
                return true;
            }
        }
        return false;
    }

    // Resta USD virtuales y acumula unidades y costo en la posición del mismo modo.
    public Operacion Comprar(Orden orden, Cotizacion cotizacion)
    {
        decimal total = CalcularTotal(orden.Cantidad, cotizacion.Precio);
        if (Saldo < total)
        {
            throw new ArgumentException("Saldo virtual insuficiente.");
        }

        Posicion? posicion = BuscarPosicion(orden.Simbolo, orden.Modo);
        if (posicion == null)
        {
            posicion = new Posicion(orden.Simbolo, orden.Modo, 0, 0);
            Posiciones.Add(posicion);
        }

        Saldo -= total;
        posicion.Cantidad += orden.Cantidad;
        posicion.Costo += total;
        return AgregarOperacion(orden, cotizacion, total, 0);
    }

    // Liquida las unidades vendidas y calcula la diferencia frente a su costo proporcional.
    public Operacion Vender(Orden orden, Cotizacion cotizacion)
    {
        decimal total = CalcularTotal(orden.Cantidad, cotizacion.Precio);
        Posicion? posicion = BuscarPosicion(orden.Simbolo, orden.Modo);
        if (posicion == null || posicion.Cantidad < orden.Cantidad)
        {
            throw new ArgumentException("No tienes esa cantidad en el modo seleccionado.");
        }

        decimal costoVendido;
        if (orden.Cantidad == posicion.Cantidad)
        {
            costoVendido = posicion.Costo;
        }
        else
        {
            costoVendido = decimal.Round(posicion.Costo * orden.Cantidad / posicion.Cantidad, 4);
        }

        decimal resultado = total - costoVendido;
        Saldo += total;
        Realizado += resultado;
        posicion.Cantidad -= orden.Cantidad;
        posicion.Costo -= costoVendido;
        if (posicion.Cantidad == 0)
        {
            Posiciones.Remove(posicion);
        }

        return AgregarOperacion(orden, cotizacion, total, resultado);
    }

    // Un símbolo puede tener dos posiciones distintas, una por cada fuente de precios.
    private Posicion? BuscarPosicion(string simbolo, string modo)
    {
        foreach (Posicion posicion in Posiciones)
        {
            if (posicion.Simbolo == simbolo && posicion.Modo == modo)
            {
                return posicion;
            }
        }
        return null;
    }

    // Valida la cantidad fraccionaria y redondea el importe a la precisión de la base de datos.
    private decimal CalcularTotal(decimal cantidad, decimal precio)
    {
        if (cantidad <= 0 || cantidad > 1000000 || decimal.Round(cantidad, 8) != cantidad)
        {
            throw new ArgumentException("Usa una cantidad positiva de hasta 8 decimales.");
        }

        decimal total = decimal.Round(precio * cantidad, 4, MidpointRounding.AwayFromZero);
        if (total < 0.01m || total > 100000000m)
        {
            throw new ArgumentException("La operación debe valer entre 0.01 y 100,000,000 USD virtuales.");
        }
        return total;
    }

    // Guarda los datos de la ejecución para que el historial no dependa de precios posteriores.
    private Operacion AgregarOperacion(Orden orden, Cotizacion cotizacion, decimal total, decimal resultado)
    {
        Operacion operacion = new Operacion(
            orden.SolicitudId, orden.Simbolo, orden.Tipo, orden.Cantidad,
            cotizacion.Precio, total, resultado, cotizacion.Fuente,
            orden.Modo, DateTimeOffset.UtcNow);
        Operaciones.Add(operacion);
        return operacion;
    }
}
