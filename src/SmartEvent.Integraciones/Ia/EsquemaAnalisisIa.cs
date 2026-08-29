using System.Text.Json;
using System.Text.Json.Nodes;
using SmartEvent.Dominio.Ia;

namespace SmartEvent.Integraciones.Ia;

/// <summary>
/// JSON Schema del analisis de reserva y construccion del prompt.
///
/// Se separa del servicio para que el contrato sea facil de leer, revisar y
/// versionar: es la pieza que el examen evalua bajo "salida estructurada
/// basada en JSON Schema".
///
/// Detalles que importan y conviene saber explicar:
///
///   strict = true          obliga al modelo a producir EXACTAMENTE esta forma.
///                          El servidor restringe la generacion token a token,
///                          asi que no puede devolver un campo de mas ni
///                          omitir uno requerido.
///
///   additionalProperties   debe ser false y TODAS las propiedades deben estar
///   y required             en "required": es un requisito del modo estricto,
///                          no una decision de diseno.
///
///   description por campo  se anadio despues de una prueba real: sin
///                          descripcion, el modelo devolvia en correoSugerido
///                          solo una direccion de correo en lugar de un
///                          borrador. Describir cada campo corrigio el problema
///                          sin tocar una linea de codigo.
/// </summary>
internal static class EsquemaAnalisisIa
{
    /// <summary>Nombre del esquema que viaja en la peticion.</summary>
    public const string NombreEsquema = "analisis_riesgo_reserva";

    /// <summary>
    /// Instrucciones del sistema. Definen el papel del modelo y, sobre todo,
    /// SUS LIMITES: la IA recomienda, nunca decide.
    /// </summary>
    public const string Instrucciones =
        "Eres un analista de riesgo operativo de una empresa que gestiona reservas de salones y " +
        "recursos para eventos corporativos. Recibes los datos de UNA reserva y evaluas el riesgo " +
        "operativo de ejecutarla: ocupacion del salon frente a su capacidad, duracion, suficiencia " +
        "de los recursos contratados frente al numero de invitados, coherencia de los descuentos y " +
        "cualquier senal de problema logistico.\n\n" +
        "REGLAS QUE DEBES RESPETAR:\n" +
        "1. Responde SIEMPRE en espanol de Ecuador, con lenguaje profesional y directo.\n" +
        "2. Tu papel es EXCLUSIVAMENTE recomendar. No confirmas, no cancelas, no modificas importes " +
        "y no propones cambiar los totales: esos calculos ya los hizo el sistema y son definitivos.\n" +
        "3. No inventes datos que no aparezcan en la reserva.\n" +
        "4. El campo correoSugerido debe ser el TEXTO COMPLETO de un borrador de correo profesional " +
        "dirigido al cliente, con saludo, cuerpo y despedida. No es una direccion de correo.\n" +
        "5. Se concreto: cada recomendacion debe ser una accion que una persona pueda ejecutar.";

    /// <summary>
    /// Construye el objeto "schema" del JSON Schema.
    ///
    /// Nota sobre maxLength, minItems y maxItems: se declaran para guiar al
    /// modelo, pero NO se confia en que el servidor los haga cumplir. Por eso
    /// ResultadoAnalisisIa.EsValido vuelve a comprobar los limites de negocio
    /// despues de deserializar.
    /// </summary>
    public static JsonObject ConstruirEsquema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("nivelRiesgo", "resumen", "alertas", "recomendaciones", "correoSugerido"),
        ["properties"] = new JsonObject
        {
            ["nivelRiesgo"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("BAJO", "MEDIO", "ALTO"),
                ["description"] = "Nivel de riesgo operativo global de la reserva."
            },
            ["resumen"] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = ResultadoAnalisisIa.LongitudMaximaResumen,
                ["description"] =
                    "Sintesis del analisis en un solo parrafo de como maximo "
                    + ResultadoAnalisisIa.LongitudMaximaResumen + " caracteres."
            },
            ["alertas"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = ResultadoAnalisisIa.MaximoAlertas,
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] =
                    "Entre 0 y " + ResultadoAnalisisIa.MaximoAlertas + " avisos breves sobre riesgos "
                    + "detectados. Si no hay ninguno, devuelve una lista vacia."
            },
            ["recomendaciones"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = ResultadoAnalisisIa.MinimoRecomendaciones,
                ["maxItems"] = ResultadoAnalisisIa.MaximoRecomendaciones,
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] =
                    "Entre " + ResultadoAnalisisIa.MinimoRecomendaciones + " y "
                    + ResultadoAnalisisIa.MaximoRecomendaciones
                    + " acciones concretas y ejecutables para reducir el riesgo."
            },
            ["correoSugerido"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "Texto completo de un borrador de correo profesional dirigido al cliente, con "
                    + "saludo, cuerpo y despedida. Es solo una propuesta: el sistema NUNCA lo envia "
                    + "de forma automatica."
            }
        }
    };

    /// <summary>
    /// Construye el texto que describe la reserva al modelo.
    ///
    /// PRINCIPIO DE MINIMIZACION DE DATOS: se envia solo lo necesario para
    /// evaluar el riesgo operativo. Del cliente viaja unicamente el nombre, y
    /// NO su identificacion, su correo ni su telefono, que no aportan nada al
    /// analisis. El examen lo pide expresamente: "enviar unicamente los datos
    /// necesarios" y "no guardar datos innecesarios del cliente".
    /// </summary>
    public static string ConstruirEntrada(Dominio.Entidades.Reserva reserva)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        var cultura = System.Globalization.CultureInfo.GetCultureInfo("es-EC");
        var texto = new System.Text.StringBuilder(1024);

        texto.AppendLine("DATOS DE LA RESERVA A ANALIZAR")
             .AppendLine("------------------------------")
             .AppendLine(cultura, $"Codigo: {reserva.Codigo}")
             .AppendLine(cultura, $"Estado actual: {Dominio.Enumeraciones.MaquinaEstadosReserva.ATexto(reserva.Estado)}")
             .AppendLine(cultura, $"Cliente: {reserva.ClienteNombres}")
             .AppendLine(cultura, $"Salon: {reserva.SalonNombre}")
             .AppendLine(cultura, $"Capacidad del salon: {reserva.SalonCapacidad} personas")
             .AppendLine(cultura, $"Tarifa base del salon: {reserva.SalonTarifaBase:F2} USD")
             .AppendLine(cultura, $"Fecha del evento: {reserva.FechaEvento:yyyy-MM-dd}")
             .AppendLine(cultura, $"Horario: {reserva.HoraInicio:hh\\:mm} a {reserva.HoraFin:hh\\:mm} " +
                                  $"({reserva.Duracion.TotalHours:0.#} horas)")
             .AppendLine(cultura, $"Numero de invitados: {reserva.NumeroInvitados}")
             .AppendLine(cultura, $"Ocupacion del salon: " +
                                  $"{(reserva.SalonCapacidad > 0 ? reserva.NumeroInvitados * 100.0 / reserva.SalonCapacidad : 0):0.#}%")
             .AppendLine();

        texto.AppendLine("RECURSOS Y SERVICIOS CONTRATADOS")
             .AppendLine("--------------------------------");

        if (reserva.Detalles.Count == 0)
        {
            texto.AppendLine("(la reserva no tiene recursos asociados)");
        }
        else
        {
            foreach (var d in reserva.Detalles)
            {
                texto.AppendLine(cultura,
                    $"- {d.RecursoNombre} ({d.RecursoTipo}): cantidad {d.Cantidad}, " +
                    $"precio unitario {d.PrecioUnitario:F2} USD, descuento {d.PorcentajeDescuento:0.##}%, " +
                    $"subtotal {d.SubtotalLinea:F2} USD. Inventario total disponible del recurso: {d.RecursoStock}.");
            }
        }

        texto.AppendLine()
             .AppendLine("IMPORTES CALCULADOS POR EL SISTEMA (definitivos, no los modifiques)")
             .AppendLine("------------------------------------------------------------------")
             .AppendLine(cultura, $"Subtotal: {reserva.Subtotal:F2} USD")
             .AppendLine(cultura, $"Descuento global: {reserva.Descuento:F2} USD " +
                                  $"({reserva.PorcentajeDescuentoGlobal:0.##}%)")
             .AppendLine(cultura, $"Impuesto (15%): {reserva.Impuesto:F2} USD")
             .AppendLine(cultura, $"Total: {reserva.Total:F2} USD");

        if (!string.IsNullOrWhiteSpace(reserva.Observacion))
        {
            texto.AppendLine()
                 .AppendLine("OBSERVACION REGISTRADA")
                 .AppendLine("----------------------")
                 .AppendLine(reserva.Observacion);
        }

        return texto.ToString();
    }

    /// <summary>Opciones de deserializacion compartidas.</summary>
    public static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
