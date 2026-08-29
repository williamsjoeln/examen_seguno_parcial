using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Reglas;

namespace SmartEvent.Aplicacion.Servicios;

/// <summary>
/// Validacion previa de una reserva, en el cliente.
///
/// PARA QUE SIRVE Y PARA QUE NO:
/// sirve para avisar al usuario de inmediato, antes de ir a la base de datos,
/// y para poder senalar el campo concreto que esta mal. NO es la autoridad:
/// exactamente las mismas reglas estan implementadas en las restricciones CHECK
/// de las tablas y en los procedimientos almacenados, y son esas las que
/// deciden. Si esta clase se equivocara o alguien la saltara, la operacion
/// seguiria siendo rechazada por SQL Server.
///
/// Que la validacion este duplicada es intencional y lo pide el examen:
/// "los totales se recalculan tanto en la interfaz como en SQL Server".
/// </summary>
public static class ValidadorReserva
{
    /// <summary>Un problema encontrado, con el campo al que corresponde.</summary>
    /// <param name="Campo">Nombre logico del control afectado, para resaltarlo.</param>
    /// <param name="Mensaje">Texto para el usuario.</param>
    public sealed record Problema(string Campo, string Mensaje);

    /// <summary>
    /// Valida la solicitud completa.
    /// </summary>
    /// <param name="solicitud">Datos capturados en el formulario.</param>
    /// <param name="capacidadSalon">Capacidad del salon elegido, para la regla D04.</param>
    /// <param name="rol">Rol del usuario, para la regla D13 de descuentos.</param>
    public static IReadOnlyList<Problema> Validar(
        SolicitudGuardarReserva solicitud,
        int capacidadSalon,
        RolUsuario rol)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var problemas = new List<Problema>();

        // --- Cliente y salon ---
        if (solicitud.IdCliente <= 0)
        {
            problemas.Add(new Problema(nameof(solicitud.IdCliente), "Seleccione un cliente."));
        }

        if (solicitud.IdSalon <= 0)
        {
            problemas.Add(new Problema(nameof(solicitud.IdSalon), "Seleccione un salon."));
        }

        // --- Horario (D01 y D02) ---
        if (!ReglasReserva.HorarioEsCoherente(solicitud.HoraInicio, solicitud.HoraFin))
        {
            problemas.Add(new Problema(nameof(solicitud.HoraFin),
                "La hora de fin debe ser posterior a la hora de inicio."));
        }
        else if (!ReglasReserva.DuracionEsValida(solicitud.HoraInicio, solicitud.HoraFin))
        {
            var horas = (solicitud.HoraFin - solicitud.HoraInicio).TotalHours;
            problemas.Add(new Problema(nameof(solicitud.HoraFin),
                $"La duracion del evento es de {horas:0.##} horas y debe estar entre "
                + $"{ReglasReserva.DuracionMinimaHoras} y {ReglasReserva.DuracionMaximaHoras} horas."));
        }

        // --- Invitados (D03 y D04) ---
        if (solicitud.NumeroInvitados <= 0)
        {
            problemas.Add(new Problema(nameof(solicitud.NumeroInvitados),
                "El numero de invitados debe ser mayor que cero."));
        }
        else if (capacidadSalon > 0 && solicitud.NumeroInvitados > capacidadSalon)
        {
            problemas.Add(new Problema(nameof(solicitud.NumeroInvitados),
                $"El salon admite hasta {capacidadSalon} invitados y se indicaron {solicitud.NumeroInvitados}."));
        }

        // --- Descuento global (D12 y D13) ---
        if (!ReglasReserva.PorcentajeDescuentoEnRango(solicitud.PorcentajeDescuentoGlobal))
        {
            problemas.Add(new Problema(nameof(solicitud.PorcentajeDescuentoGlobal),
                $"El descuento global debe estar entre 0 y {ReglasReserva.DescuentoMaximoPorcentaje}%."));
        }
        else if (!ReglasReserva.PuedeAplicarDescuento(solicitud.PorcentajeDescuentoGlobal, rol))
        {
            problemas.Add(new Problema(nameof(solicitud.PorcentajeDescuentoGlobal),
                $"Solo un ADMINISTRADOR puede aplicar un descuento global superior al "
                + $"{ReglasReserva.DescuentoSinPrivilegioPorcentaje}%."));
        }

        // --- Detalle (D07 a D13) ---
        if (solicitud.Detalles.Count == 0)
        {
            problemas.Add(new Problema(nameof(solicitud.Detalles),
                "La reserva debe incluir al menos un recurso o servicio."));
        }

        var recursosVistos = new HashSet<int>();

        for (var i = 0; i < solicitud.Detalles.Count; i++)
        {
            var linea = solicitud.Detalles[i];
            var etiqueta = $"Fila {i + 1}";

            if (linea.IdRecurso <= 0)
            {
                problemas.Add(new Problema(nameof(solicitud.Detalles), $"{etiqueta}: seleccione un recurso."));
                continue;
            }

            // D08: un recurso no puede repetirse en el mismo detalle.
            if (!recursosVistos.Add(linea.IdRecurso))
            {
                problemas.Add(new Problema(nameof(solicitud.Detalles),
                    $"{etiqueta}: el recurso esta repetido. Sume las cantidades en una sola fila."));
            }

            if (linea.Cantidad <= 0)
            {
                problemas.Add(new Problema(nameof(solicitud.Detalles),
                    $"{etiqueta}: la cantidad debe ser mayor que cero."));
            }

            if (linea.PrecioUnitario < 0m)
            {
                problemas.Add(new Problema(nameof(solicitud.Detalles),
                    $"{etiqueta}: el precio unitario no puede ser negativo."));
            }

            if (!ReglasReserva.PorcentajeDescuentoEnRango(linea.PorcentajeDescuento))
            {
                problemas.Add(new Problema(nameof(solicitud.Detalles),
                    $"{etiqueta}: el descuento debe estar entre 0 y {ReglasReserva.DescuentoMaximoPorcentaje}%."));
            }
            else if (!ReglasReserva.PuedeAplicarDescuento(linea.PorcentajeDescuento, rol))
            {
                problemas.Add(new Problema(nameof(solicitud.Detalles),
                    $"{etiqueta}: solo un ADMINISTRADOR puede aplicar descuentos superiores al "
                    + $"{ReglasReserva.DescuentoSinPrivilegioPorcentaje}%."));
            }
        }

        return problemas;
    }

    /// <summary>Une los mensajes en un texto listo para mostrar en un cuadro de dialogo.</summary>
    public static string Describir(IReadOnlyList<Problema> problemas)
    {
        ArgumentNullException.ThrowIfNull(problemas);
        return string.Join(Environment.NewLine, problemas.Select(p => "• " + p.Mensaje));
    }
}
