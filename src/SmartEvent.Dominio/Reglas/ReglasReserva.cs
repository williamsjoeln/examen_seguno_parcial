using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Dominio.Reglas;

/// <summary>
/// Constantes y comprobaciones puras de las reglas de negocio del examen.
///
/// IMPORTANTE PARA LA DEFENSA: esta clase NO es la autoridad. Las mismas
/// reglas estan implementadas en SQL Server (restricciones CHECK y
/// procedimientos almacenados) y ES SQL QUIEN DECIDE. Esta copia en C# existe
/// unicamente para dar retroalimentacion inmediata al usuario mientras escribe,
/// sin ir y volver a la base de datos en cada tecla.
///
/// Si alguna vez las dos difirieran, gana SQL Server: el procedimiento
/// rechazaria la operacion y la interfaz mostraria su mensaje.
/// </summary>
public static class ReglasReserva
{
    /// <summary>Duracion minima de un evento (regla D02).</summary>
    public const int DuracionMinimaHoras = 2;

    /// <summary>Duracion maxima de un evento (regla D02).</summary>
    public const int DuracionMaximaHoras = 12;

    /// <summary>Porcentaje maximo de descuento admitido (regla D12).</summary>
    public const decimal DescuentoMaximoPorcentaje = 20m;

    /// <summary>Porcentaje de descuento que puede aplicar cualquier rol (regla D13).</summary>
    public const decimal DescuentoSinPrivilegioPorcentaje = 10m;

    /// <summary>Longitud minima del motivo de cancelacion (regla D23).</summary>
    public const int LongitudMinimaMotivoCancelacion = 20;

    /// <summary>Longitud minima de la justificacion de contingencia de IA (regla D22).</summary>
    public const int LongitudMinimaJustificacionContingencia = 20;

    /// <summary>Tasa de impuesto aplicada sobre la base neta (regla D17).</summary>
    public const decimal TasaImpuesto = 0.15m;

    /// <summary>
    /// Deteccion de cruce de franjas horarias.
    ///
    /// Implementa LITERALMENTE la formula que exige el examen:
    ///     inicioNuevo &lt; finExistente  AND  finNuevo &gt; inicioExistente
    ///
    /// Se usan comparaciones ESTRICTAS a proposito: dos franjas adyacentes
    /// (una termina a las 13:00 y la otra empieza a las 13:00) NO se cruzan.
    /// </summary>
    public static bool HayCruce(TimeSpan inicioNuevo, TimeSpan finNuevo,
                                TimeSpan inicioExistente, TimeSpan finExistente) =>
        inicioNuevo < finExistente && finNuevo > inicioExistente;

    /// <summary>Regla D01: la hora de fin debe ser posterior a la de inicio.</summary>
    public static bool HorarioEsCoherente(TimeSpan inicio, TimeSpan fin) => fin > inicio;

    /// <summary>Regla D02: la duracion debe estar entre 2 y 12 horas.</summary>
    public static bool DuracionEsValida(TimeSpan inicio, TimeSpan fin)
    {
        if (!HorarioEsCoherente(inicio, fin))
        {
            return false;
        }

        var minutos = (fin - inicio).TotalMinutes;
        return minutos >= DuracionMinimaHoras * 60 && minutos <= DuracionMaximaHoras * 60;
    }

    /// <summary>Regla D13: solo ADMINISTRADOR puede superar el 10% de descuento.</summary>
    public static bool PuedeAplicarDescuento(decimal porcentaje, RolUsuario rol) =>
        porcentaje <= DescuentoSinPrivilegioPorcentaje || rol == RolUsuario.Administrador;

    /// <summary>Regla D12: el porcentaje debe estar entre 0 y 20.</summary>
    public static bool PorcentajeDescuentoEnRango(decimal porcentaje) =>
        porcentaje >= 0m && porcentaje <= DescuentoMaximoPorcentaje;

    /// <summary>Regla D23: el motivo de cancelacion necesita al menos 20 caracteres utiles.</summary>
    public static bool MotivoCancelacionEsValido(string? motivo) =>
        !string.IsNullOrWhiteSpace(motivo) && motivo.Trim().Length >= LongitudMinimaMotivoCancelacion;

    /// <summary>Regla D22: la justificacion de contingencia necesita al menos 20 caracteres utiles.</summary>
    public static bool JustificacionContingenciaEsValida(string? justificacion) =>
        !string.IsNullOrWhiteSpace(justificacion)
        && justificacion.Trim().Length >= LongitudMinimaJustificacionContingencia;

    /// <summary>
    /// Regla D20: validacion de correo electronico.
    /// Se evita deliberadamente una expresion regular compleja (son una fuente
    /// clasica de vulnerabilidades por retroceso catastrofico). Se usa el
    /// analizador de direcciones de la propia plataforma, que es el mismo que
    /// usara MailKit al construir el mensaje.
    /// </summary>
    public static bool EmailEsValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var direccion = new System.Net.Mail.MailAddress(email.Trim());
            return direccion.Address == email.Trim()
                && direccion.Host.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
