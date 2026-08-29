using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Ia;

namespace SmartEvent.Integraciones.Ia;

/// <summary>
/// Analisis de riesgo mediante la RESPONSES API de OpenAI, con salida
/// estructurada por JSON Schema.
///
/// Es la unica clase de la solucion que habla con el servicio de IA. Los
/// formularios reciben IServicioAnalisisIa y no saben si por debajo hay HTTP,
/// que modelo se usa ni donde esta la clave.
///
/// POR QUE UN HttpClient PROPIO Y NO EL SDK OFICIAL:
/// el examen admite "el cliente oficial de .NET o una implementacion HTTP
/// equivalente bien encapsulada". Se eligio HTTP directo por tres razones:
///   1. El contrato que importa es el JSON Schema, y aqui queda a la vista.
///   2. No se arrastra una dependencia mas que pueda cambiar de forma.
///   3. La direccion base es configurable, asi que la misma implementacion
///      sirve para cualquier proveedor que exponga la Responses API.
///
/// CONTROL DE ERRORES: este servicio NUNCA lanza una excepcion hacia la
/// interfaz por un fallo del servicio externo. Devuelve una EjecucionAnalisisIa
/// marcada como no exitosa, con un mensaje apto para el usuario y un detalle
/// tecnico que se guarda en la auditoria. Es lo que exige el caso CA-09: la
/// aplicacion sigue operativa.
///
/// LIMITE DE LA IA: esta clase solo devuelve datos. No confirma, no cancela, no
/// toca importes y no ejecuta SQL. Es literalmente incapaz de hacerlo: no
/// recibe ningun repositorio.
/// </summary>
public sealed class ServicioAnalisisIaResponses : IServicioAnalisisIa
{
    private readonly IHttpClientFactory _fabricaHttp;
    private readonly OpcionesOpenAi _opciones;
    private readonly IRegistradorSeguro _registro;

    /// <summary>Nombre del cliente HTTP registrado en el contenedor.</summary>
    public const string NombreClienteHttp = "OpenAiResponses";

    public ServicioAnalisisIaResponses(
        IHttpClientFactory fabricaHttp,
        IOptions<OpcionesOpenAi> opciones,
        IRegistradorSeguro registro)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _fabricaHttp = fabricaHttp ?? throw new ArgumentNullException(nameof(fabricaHttp));
        _opciones = opciones.Value;
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
    }

    public bool EstaConfigurado => _opciones.EstaConfigurado;
    public string Modelo => _opciones.Modelo;
    public string Proveedor => _opciones.Proveedor;
    public string PromptVersion => _opciones.PromptVersion;

    public async Task<EjecucionAnalisisIa> AnalizarAsync(Reserva reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        var cronometro = Stopwatch.StartNew();

        // ---------- Caso CA-09: no hay clave configurada ----------
        if (!_opciones.EstaConfigurado)
        {
            cronometro.Stop();
            _registro.Advertencia("Se solicito un analisis de IA sin clave configurada.");

            return EjecucionAnalisisIa.Fallido(
                "No hay una clave de OpenAI configurada, por lo que no se pudo ejecutar el analisis. "
                + "La reserva no sufrio ningun cambio. Puede continuar trabajando normalmente y, si "
                + "necesita confirmarla, registre una justificacion de contingencia.",
                $"Falta la variable de entorno {OpcionesOpenAi.VariableEntornoClave}.",
                _opciones.Proveedor, _opciones.Modelo, _opciones.PromptVersion,
                (int)cronometro.ElapsedMilliseconds);
        }

        // Tiempo maximo propio, enlazado con la cancelacion del usuario: el
        // formulario puede cancelar con un boton y ademas hay un tope duro.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _opciones.SegundosTiempoEspera)));

        try
        {
            var cliente = _fabricaHttp.CreateClient(NombreClienteHttp);

            using var peticion = new HttpRequestMessage(HttpMethod.Post, _opciones.UrlResponses);
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opciones.ApiKey);
            peticion.Content = JsonContent.Create(ConstruirCuerpo(reserva));

            using var respuesta = await cliente
                .SendAsync(peticion, HttpCompletionOption.ResponseContentRead, limite.Token)
                .ConfigureAwait(false);

            var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token).ConfigureAwait(false);

            if (!respuesta.IsSuccessStatusCode)
            {
                cronometro.Stop();
                return TraducirErrorHttp(respuesta.StatusCode, cuerpo, cronometro);
            }

            // ---------- Respuesta vacia ----------
            if (string.IsNullOrWhiteSpace(cuerpo))
            {
                cronometro.Stop();
                return Fallido(cronometro,
                    "El servicio de analisis devolvio una respuesta vacia. Intente nuevamente.",
                    "Cuerpo de respuesta vacio con codigo HTTP 200.");
            }

            return ProcesarRespuesta(cuerpo, cronometro);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            // Cancelacion pedida por el usuario con el boton Cancelar.
            throw;
        }
        catch (OperationCanceledException)
        {
            cronometro.Stop();
            return Fallido(cronometro,
                $"El servicio de analisis no respondio en {_opciones.SegundosTiempoEspera} segundos. "
                + "La reserva no sufrio ningun cambio.",
                $"Tiempo de espera agotado tras {_opciones.SegundosTiempoEspera} segundos.");
        }
        catch (HttpRequestException ex)
        {
            // Sin conexion, DNS que no resuelve, certificado invalido...
            cronometro.Stop();
            return Fallido(cronometro,
                "No se pudo contactar con el servicio de analisis. Verifique su conexion a Internet. "
                + "La reserva no sufrio ningun cambio.",
                $"HttpRequestException: {ex.StatusCode?.ToString() ?? "sin codigo"} - {ex.Message}");
        }
        catch (JsonException ex)
        {
            cronometro.Stop();
            return Fallido(cronometro,
                "El servicio de analisis devolvio una respuesta que no se pudo interpretar.",
                $"JsonException al leer la respuesta: {ex.Message}");
        }
    }

    // ===================== CONSTRUCCION DE LA PETICION =====================

    /// <summary>
    /// Cuerpo de la peticion a POST /v1/responses.
    ///
    /// La forma es la de la Responses API:
    ///   model              modelo a usar
    ///   input              lista de mensajes con papel y contenido
    ///   text.format        formato de salida; aqui, json_schema estricto
    /// </summary>
    private JsonObject ConstruirCuerpo(Reserva reserva) => new()
    {
        ["model"] = _opciones.Modelo,
        ["input"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = EsquemaAnalisisIa.Instrucciones
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = EsquemaAnalisisIa.ConstruirEntrada(reserva)
            }
        },
        ["text"] = new JsonObject
        {
            ["format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["name"] = EsquemaAnalisisIa.NombreEsquema,
                ["strict"] = true,
                ["schema"] = EsquemaAnalisisIa.ConstruirEsquema()
            }
        }
    };

    // ===================== LECTURA DE LA RESPUESTA =====================

    private EjecucionAnalisisIa ProcesarRespuesta(string cuerpo, Stopwatch cronometro)
    {
        JsonNode? raiz;

        try
        {
            raiz = JsonNode.Parse(cuerpo);
        }
        catch (JsonException ex)
        {
            cronometro.Stop();
            return Fallido(cronometro,
                "El servicio de analisis devolvio una respuesta con formato invalido.",
                $"La respuesta completa no es JSON valido: {ex.Message}");
        }

        if (raiz is null)
        {
            cronometro.Stop();
            return Fallido(cronometro,
                "El servicio de analisis devolvio una respuesta vacia.",
                "JsonNode.Parse devolvio null.");
        }

        // ---------- Rechazo explicito del modelo ----------
        var rechazo = ExtraerRechazo(raiz);

        if (!string.IsNullOrWhiteSpace(rechazo))
        {
            cronometro.Stop();
            _registro.Advertencia("El modelo rechazo la solicitud de analisis.");

            return Fallido(cronometro,
                "El servicio de analisis no pudo procesar esta reserva. La reserva no sufrio ningun cambio.",
                $"Rechazo del modelo: {Recortar(rechazo, 300)}");
        }

        // ---------- Texto de salida ----------
        var textoSalida = ExtraerTextoSalida(raiz);

        if (string.IsNullOrWhiteSpace(textoSalida))
        {
            cronometro.Stop();
            return Fallido(cronometro,
                "El servicio de analisis no devolvio ningun contenido.",
                "No se encontro texto de salida en la respuesta.");
        }

        // ---------- Deserializacion ----------
        ResultadoAnalisisIa? resultado;

        try
        {
            resultado = JsonSerializer.Deserialize<ResultadoAnalisisIa>(
                textoSalida, EsquemaAnalisisIa.OpcionesJson);
        }
        catch (JsonException ex)
        {
            cronometro.Stop();
            return Fallido(cronometro,
                "El analisis llego con un formato que no corresponde al esperado.",
                $"JSON de salida invalido: {ex.Message}");
        }

        if (resultado is null)
        {
            cronometro.Stop();
            return Fallido(cronometro,
                "El analisis llego vacio.",
                "La deserializacion del JSON de salida devolvio null.");
        }

        // ---------- Validacion del contrato de negocio ----------
        // Se valida aunque se haya pedido salida estricta: el esquema garantiza
        // la FORMA, no los limites de negocio.
        if (!resultado.EsValido(out var errores))
        {
            cronometro.Stop();
            _registro.Advertencia("El analisis de IA no cumplio el contrato: " + string.Join(" | ", errores));

            return Fallido(cronometro,
                "El analisis recibido no cumple el formato esperado, por lo que no se mostrara. "
                + "Puede intentarlo nuevamente.",
                "Contrato incumplido: " + Recortar(string.Join(" | ", errores), 400));
        }

        var (tokensEntrada, tokensSalida) = ExtraerTokens(raiz);
        cronometro.Stop();

        _registro.Informacion(
            $"Analisis de IA correcto. Proveedor={_opciones.Proveedor} Modelo={_opciones.Modelo} "
            + $"Riesgo={resultado.NivelRiesgo} Duracion={cronometro.ElapsedMilliseconds}ms.");

        return EjecucionAnalisisIa.Correcto(
            resultado, textoSalida, _opciones.Proveedor, _opciones.Modelo, _opciones.PromptVersion,
            tokensEntrada, tokensSalida, (int)cronometro.ElapsedMilliseconds);
    }

    /// <summary>
    /// Extrae el texto generado recorriendo output[].content[].
    ///
    /// La Responses API devuelve una lista de elementos de salida; los de tipo
    /// "message" contienen partes de contenido, y las de tipo "output_text"
    /// llevan el JSON que pedimos. Se recorre en lugar de asumir una posicion
    /// fija porque el modelo puede intercalar otros elementos, como bloques de
    /// razonamiento.
    /// </summary>
    private static string? ExtraerTextoSalida(JsonNode raiz)
    {
        // Algunos servidores ofrecen ademas un atajo con el texto ya unido.
        var atajo = raiz["output_text"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(atajo))
        {
            return atajo;
        }

        if (raiz["output"] is not JsonArray salida)
        {
            return null;
        }

        foreach (var elemento in salida)
        {
            if (elemento?["content"] is not JsonArray contenido)
            {
                continue;
            }

            foreach (var parte in contenido)
            {
                if (parte?["type"]?.GetValue<string>() == "output_text")
                {
                    var texto = parte["text"]?.GetValue<string>();

                    if (!string.IsNullOrWhiteSpace(texto))
                    {
                        return texto;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Busca un rechazo explicito del modelo dentro de la salida.</summary>
    private static string? ExtraerRechazo(JsonNode raiz)
    {
        if (raiz["output"] is not JsonArray salida)
        {
            return null;
        }

        foreach (var elemento in salida)
        {
            if (elemento?["content"] is not JsonArray contenido)
            {
                continue;
            }

            foreach (var parte in contenido)
            {
                if (parte?["type"]?.GetValue<string>() == "refusal")
                {
                    return parte["refusal"]?.GetValue<string>();
                }
            }
        }

        return null;
    }

    /// <summary>Lee el consumo de tokens, si el proveedor lo informa.</summary>
    private static (int? Entrada, int? Salida) ExtraerTokens(JsonNode raiz)
    {
        var uso = raiz["usage"];

        if (uso is null)
        {
            return (null, null);
        }

        int? entrada = uso["input_tokens"]?.GetValue<int>();
        int? salida = uso["output_tokens"]?.GetValue<int>();

        return (entrada, salida);
    }

    // ===================== ERRORES =====================

    /// <summary>
    /// Traduce un codigo HTTP de error a un mensaje comprensible.
    ///
    /// El cuerpo devuelto por el servicio NO se muestra al usuario: podria
    /// contener informacion interna del proveedor. Solo va, recortado, al
    /// detalle tecnico que se guarda en la auditoria.
    /// </summary>
    private EjecucionAnalisisIa TraducirErrorHttp(HttpStatusCode codigo, string cuerpo, Stopwatch cronometro)
    {
        var mensajeUsuario = codigo switch
        {
            HttpStatusCode.Unauthorized =>
                "La clave de OpenAI configurada no es valida o fue revocada. "
                + "La reserva no sufrio ningun cambio.",

            HttpStatusCode.Forbidden =>
                "La clave configurada no tiene permiso para usar este modelo. "
                + "La reserva no sufrio ningun cambio.",

            HttpStatusCode.NotFound =>
                "El modelo configurado no existe en el servicio de analisis. "
                + "Revise la configuracion OpenAI:Modelo.",

            HttpStatusCode.TooManyRequests =>
                "Se alcanzo el limite de uso del servicio de analisis. Espere unos minutos "
                + "e intente nuevamente. La reserva no sufrio ningun cambio.",

            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                "El servicio de analisis tardo demasiado en responder. Intente nuevamente.",

            HttpStatusCode.BadRequest =>
                "El servicio de analisis rechazo la solicitud. El detalle quedo registrado para diagnostico.",

            _ when (int)codigo >= 500 =>
                "El servicio de analisis presenta un problema temporal. Intente nuevamente en unos minutos.",

            _ => "No se pudo completar el analisis. La reserva no sufrio ningun cambio."
        };

        _registro.Advertencia(
            $"La Responses API devolvio HTTP {(int)codigo} ({codigo}) desde {_opciones.Proveedor}.");

        return Fallido(cronometro, mensajeUsuario,
            $"HTTP {(int)codigo} {codigo}. Respuesta: {Recortar(cuerpo, 300)}");
    }

    private EjecucionAnalisisIa Fallido(Stopwatch cronometro, string mensajeUsuario, string detalleTecnico) =>
        EjecucionAnalisisIa.Fallido(
            mensajeUsuario, detalleTecnico,
            _opciones.Proveedor, _opciones.Modelo, _opciones.PromptVersion,
            (int)cronometro.ElapsedMilliseconds);

    private static string Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var limpio = texto.Trim().ReplaceLineEndings(" ");
        return limpio.Length <= maximo ? limpio : limpio[..maximo] + "...";
    }
}
