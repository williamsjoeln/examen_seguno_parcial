namespace SmartEvent.Integraciones.Ia;

/// <summary>
/// Configuracion de la integracion con la Responses API.
///
/// La clave se lee de la variable de entorno OPENAI_API_KEY, tal como exige el
/// examen, o de una configuracion local ignorada por Git. NUNCA esta escrita en
/// el codigo ni se sube al repositorio.
///
/// BaseUrl es configurable a proposito. El destino por defecto es
/// https://api.openai.com/v1, pero la misma Responses API la implementan otros
/// proveedores compatibles; cambiar de uno a otro es una linea de
/// configuracion, sin tocar una sola linea de codigo. Eso es exactamente lo que
/// significa "integracion bien encapsulada".
/// </summary>
public sealed class OpcionesOpenAi
{
    public const string Seccion = "OpenAI";

    /// <summary>Nombre de la variable de entorno que exige el examen para la clave.</summary>
    public const string VariableEntornoClave = "OPENAI_API_KEY";

    /// <summary>
    /// Clave de API. Se enlaza desde OPENAI_API_KEY o desde User Secrets.
    /// Nunca se registra en el log ni se persiste en la base de datos.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Direccion base del servicio, sin barra final.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Modelo a utilizar. Se persiste en evt.AnalisisIA.Modelo.</summary>
    public string Modelo { get; set; } = "gpt-5-mini";

    /// <summary>Tiempo maximo de espera de la llamada.</summary>
    public int SegundosTiempoEspera { get; set; } = 60;

    /// <summary>
    /// Version del prompt. Se persiste en evt.AnalisisIA.PromptVersion, campo
    /// que el examen exige. Permite saber con que instrucciones se genero cada
    /// analisis guardado, incluso despues de haber mejorado el prompt.
    /// </summary>
    public string PromptVersion { get; set; } = "v1";

    /// <summary>Hay clave configurada y se puede intentar la llamada.</summary>
    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Direccion completa del extremo de la Responses API.</summary>
    public string UrlResponses => $"{BaseUrl.TrimEnd('/')}/responses";

    /// <summary>
    /// Nombre del proveedor deducido de la direccion base. Se guarda en
    /// evt.AnalisisIA.Proveedor para poder auditar con que backend se genero
    /// cada analisis. NUNCA incluye la clave.
    /// </summary>
    public string Proveedor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                return "DESCONOCIDO";
            }

            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
            {
                return "DESCONOCIDO";
            }

            var host = uri.Host.ToUpperInvariant();

            if (host.Contains("API.OPENAI.COM", StringComparison.Ordinal))
            {
                return "OPENAI";
            }

            // Se toma la etiqueta principal del dominio: api.groq.com -> GROQ
            var partes = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return partes.Length >= 2 ? partes[^2] : host;
        }
    }
}
