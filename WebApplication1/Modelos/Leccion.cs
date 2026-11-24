using System.Text.Json.Serialization;

namespace Vantex;

// Material de Academia, su ejemplo y la respuesta que el servidor compara con el cuestionario.
public class Leccion
{
    public string Id { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Nivel { get; set; } = "";
    public string Duracion { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Ejemplo { get; set; } = "";
    public string Pregunta { get; set; } = "";
    public string[] Opciones { get; set; } = Array.Empty<string>();
    // El índice correcto se queda en el servidor para que el cuestionario se pueda validar al enviar la respuesta.
    [JsonIgnore]
    public int Correcta
    {
        get; set;
    }
    public string Explicacion { get; set; } = "";

    public Leccion()
    {
    }

    public Leccion(string id, string titulo, string nivel, string duracion, string texto, string ejemplo, string pregunta, string[] opciones, int correcta, string explicacion)
    {
        Id = id;
        Titulo = titulo;
        Nivel = nivel;
        Duracion = duracion;
        Texto = texto;
        Ejemplo = ejemplo;
        Pregunta = pregunta;
        Opciones = opciones;
        Correcta = correcta;
        Explicacion = explicacion;
    }
}
