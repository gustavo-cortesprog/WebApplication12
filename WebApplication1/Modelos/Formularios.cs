namespace Vantex;

// Datos del formulario para crear una cuenta. El servidor transforma la contraseña antes de almacenarla.
public class Registro
{
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";

    public Registro()
    {
    }

    public Registro(string nombre, string email, string password)
    {
        Nombre = nombre;
        Email = email;
        Password = password;
    }
}

// Credenciales que el servidor verifica para emitir la cookie de sesión.
public class Acceso
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";

    public Acceso()
    {
    }

    public Acceso(string email, string password)
    {
        Email = email;
        Password = password;
    }
}

// Solicitud de compra o venta virtual. SolicitudId identifica un reintento.
public class Orden
{
    public string Simbolo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public decimal Cantidad
    {
        get; set;
    }
    public string Modo { get; set; } = "";
    public Guid SolicitudId
    {
        get; set;
    }

    public Orden()
    {
    }

    public Orden(string simbolo, string tipo, decimal cantidad, string modo, Guid solicitudId)
    {
        Simbolo = simbolo;
        Tipo = tipo;
        Cantidad = cantidad;
        Modo = modo;
        SolicitudId = solicitudId;
    }
}

// Importes y tasa hipotética que recibe la calculadora de ahorro.
public class MetaEntrada
{
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
    public double TasaAnual
    {
        get; set;
    }

    public MetaEntrada()
    {
    }

    public MetaEntrada(string nombre, decimal costo, decimal inicial, decimal mensual, double tasaAnual)
    {
        Nombre = nombre;
        Costo = costo;
        Inicial = inicial;
        Mensual = mensual;
        TasaAnual = tasaAnual;
    }
}

// Pregunta nueva y turnos recientes que el navegador manda al tutor.
public class Pregunta
{
    public string Mensaje { get; set; } = "";
    public List<TurnoTutor>? Historial { get; set; }

    public Pregunta()
    {
    }

    public Pregunta(string mensaje)
    {
        Mensaje = mensaje;
    }
}

// Mensaje de usuario o asistente usado como contexto de una pregunta siguiente.
public class TurnoTutor
{
    public string Rol { get; set; } = "";
    public string Texto { get; set; } = "";
}

// Índice de la opción elegida en el cuestionario de Academia.
public class RespuestaQuiz
{
    public int Respuesta
    {
        get; set;
    }

    public RespuestaQuiz()
    {
    }

    public RespuestaQuiz(int respuesta)
    {
        Respuesta = respuesta;
    }
}
