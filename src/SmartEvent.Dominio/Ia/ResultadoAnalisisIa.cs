using System.Text.Json.Serialization;
using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Dominio.Ia;

/// <summary>
/// Contrato de la respuesta estructurada del analisis de IA, exactamente como
/// lo define el examen (SS10):
///
///   nivelRiesgo      BAJO, MEDIO o ALTO
///   resumen          texto de maximo 300 caracteres
///   alertas          arreglo de 0 a 5 mensajes
///   recomendaciones  arreglo de 1 a 5 acciones concretas
///   correoSugerido   borrador profesional; NUNCA se envia automaticamente
///
/// Los nombres JSON coinciden con el JSON Schema que se envia a la Responses
/// API, para que la deserializacion sea directa.
/// </summary>
public sealed class ResultadoAnalisisIa
{
    [JsonPropertyName("nivelRiesgo")]
    public string NivelRiesgo { get; set; } = string.Empty;

    [JsonPropertyName("resumen")]
    public string Resumen { get; set; } = string.Empty;

    [JsonPropertyName("alertas")]
    public List<string> Alertas { get; set; } = [];

    [JsonPropertyName("recomendaciones")]
    public List<string> Recomendaciones { get; set; } = [];

    [JsonPropertyName("correoSugerido")]
    public string CorreoSugerido { get; set; } = string.Empty;

    /// <summary>Nivel de riesgo ya convertido al enumerado del dominio.</summary>
    [JsonIgnore]
    public NivelRiesgo Nivel =>
        TextosEnumeracion.TryNivelRiesgo(NivelRiesgo, out var nivel) ? nivel : Enumeraciones.NivelRiesgo.Bajo;

    /// <summary>
    /// Valida que la respuesta cumpla el contrato del examen.
    ///
    /// Se valida AUNQUE se haya pedido salida estructurada con JSON Schema. El
    /// examen exige "validar/deserializar la respuesta antes de mostrarla o
    /// persistirla", y confiar ciegamente en que un modelo respeto el esquema
    /// es precisamente lo que no se debe hacer: el esquema garantiza la forma
    /// (tipos y campos), no los limites de negocio como los 300 caracteres del
    /// resumen o el maximo de cinco alertas.
    /// </summary>
    /// <param name="errores">Lista de problemas encontrados, vacia si es valido.</param>
    public bool EsValido(out IReadOnlyList<string> errores)
    {
        var problemas = new List<string>();

        if (!TextosEnumeracion.TryNivelRiesgo(NivelRiesgo, out _))
        {
            problemas.Add($"El nivel de riesgo '{NivelRiesgo}' no es BAJO, MEDIO ni ALTO.");
        }

        if (string.IsNullOrWhiteSpace(Resumen))
        {
            problemas.Add("El resumen llego vacio.");
        }
        else if (Resumen.Length > LongitudMaximaResumen)
        {
            problemas.Add($"El resumen tiene {Resumen.Length} caracteres y el maximo es {LongitudMaximaResumen}.");
        }

        if (Alertas.Count > MaximoAlertas)
        {
            problemas.Add($"Llegaron {Alertas.Count} alertas y el maximo es {MaximoAlertas}.");
        }

        if (Alertas.Exists(string.IsNullOrWhiteSpace))
        {
            problemas.Add("Alguna alerta llego vacia.");
        }

        if (Recomendaciones.Count < MinimoRecomendaciones || Recomendaciones.Count > MaximoRecomendaciones)
        {
            problemas.Add(
                $"Llegaron {Recomendaciones.Count} recomendaciones y se esperaban entre "
                + $"{MinimoRecomendaciones} y {MaximoRecomendaciones}.");
        }

        if (Recomendaciones.Exists(string.IsNullOrWhiteSpace))
        {
            problemas.Add("Alguna recomendacion llego vacia.");
        }

        if (string.IsNullOrWhiteSpace(CorreoSugerido))
        {
            problemas.Add("El correo sugerido llego vacio.");
        }

        errores = problemas;
        return problemas.Count == 0;
    }

    /// <summary>Longitud maxima del resumen, segun el contrato del examen.</summary>
    public const int LongitudMaximaResumen = 300;

    /// <summary>Numero maximo de alertas admitidas.</summary>
    public const int MaximoAlertas = 5;

    /// <summary>Numero minimo de recomendaciones exigido.</summary>
    public const int MinimoRecomendaciones = 1;

    /// <summary>Numero maximo de recomendaciones admitidas.</summary>
    public const int MaximoRecomendaciones = 5;
}

/// <summary>
/// Resultado completo de una ejecucion del analisis de IA, con lo necesario
/// para auditarla: haya salido bien o mal.
/// </summary>
public sealed class EjecucionAnalisisIa
{
    /// <summary>Indica si se obtuvo y valido una respuesta estructurada.</summary>
    public bool Exitoso { get; init; }

    /// <summary>Respuesta validada. Es null cuando <see cref="Exitoso"/> es false.</summary>
    public ResultadoAnalisisIa? Resultado { get; init; }

    /// <summary>JSON crudo devuelto por el modelo, tal cual se persiste para auditoria.</summary>
    public string? RespuestaJson { get; init; }

    /// <summary>Mensaje apto para mostrar al usuario cuando algo fallo.</summary>
    public string? MensajeUsuario { get; init; }

    /// <summary>Detalle tecnico controlado que se guarda en evt.AnalisisIA.Error.</summary>
    public string? DetalleTecnico { get; init; }

    public string Proveedor { get; init; } = string.Empty;
    public string Modelo { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;

    public int? TokensEntrada { get; init; }
    public int? TokensSalida { get; init; }
    public int DuracionMs { get; init; }

    public static EjecucionAnalisisIa Correcto(
        ResultadoAnalisisIa resultado,
        string respuestaJson,
        string proveedor,
        string modelo,
        string promptVersion,
        int? tokensEntrada,
        int? tokensSalida,
        int duracionMs) => new()
        {
            Exitoso = true,
            Resultado = resultado,
            RespuestaJson = respuestaJson,
            Proveedor = proveedor,
            Modelo = modelo,
            PromptVersion = promptVersion,
            TokensEntrada = tokensEntrada,
            TokensSalida = tokensSalida,
            DuracionMs = duracionMs
        };

    public static EjecucionAnalisisIa Fallido(
        string mensajeUsuario,
        string detalleTecnico,
        string proveedor,
        string modelo,
        string promptVersion,
        int duracionMs) => new()
        {
            Exitoso = false,
            MensajeUsuario = mensajeUsuario,
            DetalleTecnico = detalleTecnico,
            Proveedor = proveedor,
            Modelo = modelo,
            PromptVersion = promptVersion,
            DuracionMs = duracionMs
        };
}
