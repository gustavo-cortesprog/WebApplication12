namespace Vantex;

// Proyecta el saldo hipotético con aportaciones mensuales sin modificar la cuenta.
public static class CalculadoraMetas
{
    public static ProyeccionMeta Proyectar(MetaEntrada meta)
    {
        if (string.IsNullOrWhiteSpace(meta.Nombre) || meta.Nombre.Trim().Length > 80)
        {
            throw new ArgumentException("Escribe un nombre de hasta 80 caracteres para tu meta.");
        }
        if (meta.Costo <= 0 || meta.Costo > 100000000 || meta.Inicial < 0 ||
            meta.Inicial > 100000000 || meta.Mensual <= 0 || meta.Mensual > 1000000)
        {
            throw new ArgumentException("Revisa el costo, el ahorro inicial y la aportación mensual.");
        }
        if (!double.IsFinite(meta.TasaAnual) || meta.TasaAnual < -50 || meta.TasaAnual > 50)
        {
            throw new ArgumentException("La tasa hipotética debe estar entre -50 % y 50 %.");
        }

        ProyeccionMeta resultado = new ProyeccionMeta();
        decimal faltante = Math.Max(0, meta.Costo - meta.Inicial);
        resultado.SinRendimiento = Math.Ceiling(faltante / meta.Mensual);

        // Convierte una tasa anual efectiva al equivalente mensual.
        double tasaMensual = Math.Pow(1 + meta.TasaAnual / 100, 1.0 / 12) - 1;
        double saldo = (double)meta.Inicial;
        int meses = 0;
        resultado.Puntos.Add(new PuntoAhorro { Mes = 0, Saldo = Math.Round(saldo, 2) });

        // Primero aplica el rendimiento y después añade la aportación de ese mes.
        while (saldo < (double)meta.Costo && meses < 600)
        {
            saldo = saldo * (1 + tasaMensual) + (double)meta.Mensual;
            meses++;
            resultado.Puntos.Add(new PuntoAhorro { Mes = meses, Saldo = Math.Round(saldo, 2) });
        }

        if (saldo >= (double)meta.Costo)
        {
            resultado.Meses = meses;
        }
        resultado.Consejo = "Separa la aportación mensual de los rendimientos inciertos. Para una meta cercana, evalúa disponibilidad y riesgo. Compara tasas negativas y cero.";
        resultado.Nota = "Aportaciones al final de cada mes. Tasa anual constante e hipotética. Sin inflación, impuestos ni comisiones. Límite: 600 meses.";
        return resultado;
    }
}
