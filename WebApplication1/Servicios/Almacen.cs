using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
namespace Vantex;

// Repositorio: todo SQL queda aquí. El navegador nunca recibe la conexión.
public class Almacen
{
    private readonly ILogger<Almacen> log;
    private readonly string rutaScript;

    public Almacen(IConfiguration config, IWebHostEnvironment entorno, ILogger<Almacen> registro)
    {
        conexion = config.GetConnectionString("Vantex") ?? "";
        archivo = Path.Combine(entorno.ContentRootPath, "App_Data", "respaldo-local.json");
        rutaScript = Path.Combine(entorno.ContentRootPath, "Datos", "Vantex.sql");
        log = registro;
    }

    // Mantiene una copia en memoria y serializa las escrituras con Candado.
    private readonly Dictionary<Guid, Alumno> alumnos = new Dictionary<Guid, Alumno>();
    public readonly SemaphoreSlim Candado = new SemaphoreSlim(1, 1);
    private readonly string conexion;
    private readonly string archivo;
    public bool SqlActivo
    {
        get; private set;
    }
    public string Estado => SqlActivo ? "SQL Server · VantexNova" : "Respaldo local JSON · SQL no disponible";
    // Devuelve una copia; el estado principal cambia al confirmar Guardar.
    public Alumno? Buscar(Guid id)
    {
        if (alumnos.ContainsKey(id))
        {
            return Copia(alumnos[id]);
        }
        return null;
    }
    // Busca el correo normalizado que usa el inicio de sesión.
    public Alumno? PorEmail(string email)
    {
        foreach (Alumno alumno in alumnos.Values)
        {
            if (alumno.Email == email)
            {
                return Copia(alumno);
            }
        }
        return null;
    }
    // Clona la cuenta para que editar los datos de una consulta no modifique el estado que ya está guardado.
    private static Alumno Copia(Alumno a) => JsonSerializer.Deserialize<Alumno>(JsonSerializer.Serialize(a))!;
    // Prepara SQL y reconstruye las cuentas. Ante fallos usa el archivo local.
    public async Task Iniciar()
    {
        try
        {
            var cs = new SqlConnectionStringBuilder(conexion) { InitialCatalog = "master" };
            await using (var cn = new SqlConnection(cs.ConnectionString))
            {
                await cn.OpenAsync();
                await Ejecutar(cn, null, "IF DB_ID(N'VantexNova') IS NULL CREATE DATABASE VantexNova;");
            }
            await using var db = new SqlConnection(conexion);
            await db.OpenAsync();
            var script = await File.ReadAllTextAsync(rutaScript);
            // Solo ejecuta la creación de tablas; CREATE DATABASE y consultas finales se excluyen.
            var tablas = script[(script.IndexOf("IF OBJECT_ID", StringComparison.Ordinal))..];
            tablas = tablas[..tablas.IndexOf("\nGO", StringComparison.Ordinal)];
            await Ejecutar(db, null, tablas);
            await using (var cmd = new SqlCommand("SELECT u.Id,u.Nombre,u.Email,u.PasswordHash,c.Saldo,c.Realizado FROM Usuarios u JOIN Cuentas c ON c.UsuarioId=u.Id", db))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    alumnos[r.GetGuid(0)] = new Alumno { Id = r.GetGuid(0), Nombre = r.GetString(1), Email = r.GetString(2), Hash = r.GetString(3), Saldo = r.GetDecimal(4), Realizado = r.GetDecimal(5) };
            foreach (Alumno alumno in alumnos.Values)
            {
                await CargarPosiciones(db, alumno);
                await CargarOperaciones(db, alumno);
                await CargarMetas(db, alumno);
                await CargarProgreso(db, alumno);
            }
            SqlActivo = true;
        }
        catch (Exception ex)
        {
            log.LogWarning("SQL no disponible ({Tipo}). Se usa respaldo local identificado en pantalla.", ex.GetType().Name);
            alumnos.Clear();
            if (File.Exists(archivo))
            {
                var guardados = JsonSerializer.Deserialize<List<Alumno>>(await File.ReadAllTextAsync(archivo)) ?? new List<Alumno>();
                foreach (var a in guardados)
                    alumnos[a.Id] = a;
            }
        }
    }
    // Persiste un alumno en una transacción; la copia en memoria se actualiza al final.
    public async Task Guardar(Alumno a, bool nuevo = false)
    {
        // Se invoca bajo Candado desde las rutas: una operación a la vez en este servidor local.
        if (SqlActivo)
        {
            await using var cn = new SqlConnection(conexion);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            if (nuevo)
            {
                await Ejecutar(cn, tx, "INSERT INTO Usuarios VALUES(@id,@nombre,@email,@hash)",
                new SqlParameter("@id", a.Id),
                new SqlParameter("@nombre", a.Nombre),
                new SqlParameter("@email", a.Email),
                new SqlParameter("@hash", a.Hash));
                await Ejecutar(cn, tx, "INSERT INTO Cuentas VALUES(@id,@saldo,@realizado)",
                new SqlParameter("@id", a.Id),
                new SqlParameter("@saldo", a.Saldo),
                new SqlParameter("@realizado", a.Realizado));
            }
            else
            {
                await Ejecutar(cn, tx, "UPDATE Cuentas SET Saldo=@saldo,Realizado=@realizado WHERE UsuarioId=@id",
                new SqlParameter("@id", a.Id),
                new SqlParameter("@saldo", a.Saldo),
                new SqlParameter("@realizado", a.Realizado));
            }
            // Sustituye solo las posiciones, metas y progreso de este usuario dentro de la transacción.
            await Ejecutar(cn, tx, "DELETE FROM Posiciones WHERE UsuarioId=@id; DELETE FROM Metas WHERE UsuarioId=@id; DELETE FROM Progreso WHERE UsuarioId=@id",
                new SqlParameter("@id", a.Id));
            foreach (var p in a.Posiciones)
                await Ejecutar(cn, tx, "INSERT INTO Posiciones VALUES(@id,@simbolo,@modo,@cantidad,@costo)",
                new SqlParameter("@id", a.Id),
                new SqlParameter("@simbolo", p.Simbolo),
                new SqlParameter("@modo", p.Modo),
                new SqlParameter("@cantidad", p.Cantidad),
                new SqlParameter("@costo", p.Costo));
            foreach (var m in a.Metas)
                await Ejecutar(cn, tx, "INSERT INTO Metas VALUES(@meta,@id,@nombre,@costo,@inicial,@mensual,@tasa)",
                new SqlParameter("@meta", m.Id),
                new SqlParameter("@id", a.Id),
                new SqlParameter("@nombre", m.Nombre),
                new SqlParameter("@costo", m.Costo),
                new SqlParameter("@inicial", m.Inicial),
                new SqlParameter("@mensual", m.Mensual),
                new SqlParameter("@tasa", m.TasaAnual));
            foreach (var l in a.Lecciones)
                await Ejecutar(cn, tx, "INSERT INTO Progreso VALUES(@id,@leccion)",
                new SqlParameter("@id", a.Id),
                new SqlParameter("@leccion", l));
            foreach (var o in a.Operaciones)
                await Ejecutar(cn, tx, "IF NOT EXISTS(SELECT 1 FROM Operaciones WHERE Id=@op AND UsuarioId=@id) INSERT INTO Operaciones VALUES(@op,@id,@simbolo,@tipo,@cantidad,@precio,@total,@resultado,@fuente,@modo,@fecha)",
                new SqlParameter("@op", o.Id),
                new SqlParameter("@id", a.Id),
                new SqlParameter("@simbolo", o.Simbolo),
                new SqlParameter("@tipo", o.Tipo),
                new SqlParameter("@cantidad", o.Cantidad),
                new SqlParameter("@precio", o.Precio),
                new SqlParameter("@total", o.Total),
                new SqlParameter("@resultado", o.Resultado),
                new SqlParameter("@fuente", o.Fuente),
                new SqlParameter("@modo", o.Modo),
                new SqlParameter("@fecha", o.Fecha));
            await tx.CommitAsync();
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(archivo)!);
            var estado = alumnos.Values.Where(x => x.Id != a.Id).Append(a).ToList();
            await File.WriteAllTextAsync(archivo + ".tmp", JsonSerializer.Serialize(estado));
            File.Move(archivo + ".tmp", archivo, true);
        }
        alumnos[a.Id] = Copia(a);
    }
    // Completa el alumno con las posiciones que conserva en cada modo.
    private async Task CargarPosiciones(SqlConnection conexion, Alumno alumno)
    {
        string sql = "SELECT Simbolo,Modo,Cantidad,Costo FROM Posiciones WHERE UsuarioId=@id";
        await using SqlCommand comando = new SqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("@id", alumno.Id);
        await using SqlDataReader lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            alumno.Posiciones.Add(new Posicion(lector.GetString(0), lector.GetString(1), lector.GetDecimal(2), lector.GetDecimal(3)));
        }
    }

    // Recupera los movimientos virtuales desde el más reciente.
    private async Task CargarOperaciones(SqlConnection conexion, Alumno alumno)
    {
        string sql = "SELECT Id,Simbolo,Tipo,Cantidad,Precio,Total,Resultado,Fuente,Modo,Fecha FROM Operaciones WHERE UsuarioId=@id ORDER BY Fecha DESC";
        await using SqlCommand comando = new SqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("@id", alumno.Id);
        await using SqlDataReader lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            alumno.Operaciones.Add(new Operacion(lector.GetGuid(0), lector.GetString(1), lector.GetString(2), lector.GetDecimal(3), lector.GetDecimal(4), lector.GetDecimal(5), lector.GetDecimal(6), lector.GetString(7), lector.GetString(8), lector.GetFieldValue<DateTimeOffset>(9)));
        }
    }

    // Recupera los objetivos guardados en la calculadora de ahorro.
    private async Task CargarMetas(SqlConnection conexion, Alumno alumno)
    {
        string sql = "SELECT Id,Nombre,Costo,Inicial,Mensual,TasaAnual FROM Metas WHERE UsuarioId=@id";
        await using SqlCommand comando = new SqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("@id", alumno.Id);
        await using SqlDataReader lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            alumno.Metas.Add(new Meta(lector.GetGuid(0), lector.GetString(1), lector.GetDecimal(2), lector.GetDecimal(3), lector.GetDecimal(4), lector.GetDouble(5)));
        }
    }

    // Recupera los identificadores de las lecciones completadas.
    private async Task CargarProgreso(SqlConnection conexion, Alumno alumno)
    {
        string sql = "SELECT LeccionId FROM Progreso WHERE UsuarioId=@id";
        await using SqlCommand comando = new SqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("@id", alumno.Id);
        await using SqlDataReader lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            alumno.Lecciones.Add(lector.GetString(0));
        }
    }

    // Ejecuta SQL con parámetros para que los datos del alumno conserven su tipo y precisión.
    private static async Task Ejecutar(SqlConnection conexion, SqlTransaction? transaccion, string sql, params SqlParameter[] parametros)
    {
        await using SqlCommand comando = new SqlCommand(sql, conexion, transaccion);
        foreach (SqlParameter parametro in parametros)
        {
            if (parametro.Value is decimal)
            {
                parametro.SqlDbType = SqlDbType.Decimal;
                parametro.Precision = 28;
                parametro.Scale = 8;
            }
            comando.Parameters.Add(parametro);
        }
        await comando.ExecuteNonQueryAsync();
    }
}
