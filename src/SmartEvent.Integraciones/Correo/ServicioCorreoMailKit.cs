using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Integraciones.Correo;

/// <summary>
/// Envio de correo HTML con MailKit.
///
/// Es la UNICA clase de toda la solucion que abre una conexion SMTP. Los
/// formularios jamas la instancian: reciben IServicioCorreo.
///
/// Garantias que exige el examen y como se cumplen:
///   - Timeout           SmtpClient.Timeout mas un CancellationTokenSource
///                       enlazado, para que la interfaz nunca quede colgada.
///   - Cancelacion       todas las llamadas de MailKit reciben el token.
///   - Sin credenciales  la clave viene de IOptions y nunca se registra ni se
///                       persiste; de la configuracion solo sale "host:puerto".
///   - HTML seguro       todos los valores que provienen de datos se codifican
///                       con HtmlEncode antes de insertarse en la plantilla.
///   - No interrumpe     ningun fallo de red se propaga como excepcion: se
///                       devuelve un ResultadoEnvioCorreo con Exitoso = false.
/// </summary>
public sealed class ServicioCorreoMailKit : IServicioCorreo
{
    private readonly OpcionesSmtp _opciones;
    private readonly IRegistradorSeguro _registro;

    private static readonly CultureInfo Cultura = new("es-EC");

    public ServicioCorreoMailKit(IOptions<OpcionesSmtp> opciones, IRegistradorSeguro registro)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _opciones = opciones.Value;
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
    }

    public string DescripcionServidor => _opciones.Descripcion;

    // ===================== ASUNTO Y CUERPO =====================

    public string ConstruirAsunto(DatosCorreoReserva datos)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var accion = datos.TipoEvento == TipoEventoCorreo.Confirmacion
            ? "Reserva confirmada"
            : "Reserva cancelada";

        return $"SmartEvent - {accion} {datos.Reserva.Codigo} - {datos.Reserva.FechaEvento:dd/MM/yyyy}";
    }

    /// <summary>
    /// Construye el cuerpo HTML del mensaje.
    ///
    /// TODOS los valores dinamicos pasan por WebUtility.HtmlEncode. Sin eso,
    /// un cliente registrado como  Eventos &lt;script&gt;...  inyectaria etiquetas
    /// en el correo. El examen lo pide expresamente: "evitar HTML generado
    /// directamente desde valores sin codificacion".
    /// </summary>
    public string ConstruirCuerpoHtml(DatosCorreoReserva datos)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var r = datos.Reserva;
        var esConfirmacion = datos.TipoEvento == TipoEventoCorreo.Confirmacion;

        var colorEstado = r.Estado switch
        {
            EstadoReserva.Confirmada => "#1b7f4f",
            EstadoReserva.Cancelada  => "#b02a37",
            EstadoReserva.Finalizada => "#495057",
            _                        => "#8a6d3b"
        };

        var html = new StringBuilder(4096);

        html.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>").Append(E(ConstruirAsunto(datos))).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:24px;background:#f4f5f7;")
            .Append("font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#212529;\">")
            .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" ")
            .Append("style=\"max-width:680px;margin:0 auto;background:#ffffff;border-radius:8px;")
            .Append("border:1px solid #dee2e6;overflow:hidden;\"><tr><td style=\"padding:24px 28px;\">");

        // ---------- Encabezado ----------
        html.Append("<h1 style=\"margin:0 0 4px;font-size:20px;color:#0d3b66;\">SmartEvent</h1>")
            .Append("<p style=\"margin:0 0 20px;font-size:13px;color:#6c757d;\">")
            .Append("Gestion de reservas de salones y recursos</p>");

        html.Append("<h2 style=\"margin:0 0 12px;font-size:17px;\">")
            .Append(esConfirmacion ? "Su reserva ha sido confirmada" : "Su reserva ha sido cancelada")
            .Append("</h2>");

        html.Append("<p style=\"margin:0 0 18px;font-size:14px;line-height:1.5;\">Estimado(a) ")
            .Append(E(r.ClienteNombres)).Append(": ")
            .Append(esConfirmacion
                ? "confirmamos los siguientes detalles de su evento."
                : "le informamos que su reserva ha sido cancelada. A continuacion, el detalle del registro.")
            .Append("</p>");

        // ---------- Estado ----------
        html.Append("<p style=\"margin:0 0 18px;\"><span style=\"display:inline-block;padding:6px 14px;")
            .Append("border-radius:14px;font-size:13px;font-weight:600;color:#ffffff;background:")
            .Append(colorEstado).Append(";\">Estado: ")
            .Append(E(MaquinaEstadosReserva.ATexto(r.Estado))).Append("</span></p>");

        // ---------- Datos de la reserva ----------
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" ")
            .Append("style=\"width:100%;font-size:14px;margin:0 0 22px;\">");

        AgregarFila(html, "Codigo", r.Codigo);
        AgregarFila(html, "Cliente", r.ClienteNombres);
        AgregarFila(html, "Salon", r.SalonNombre);
        AgregarFila(html, "Fecha del evento", r.FechaEvento.ToString("dddd, dd 'de' MMMM 'de' yyyy", Cultura));
        AgregarFila(html, "Horario", $"{r.HoraInicio:hh\\:mm} a {r.HoraFin:hh\\:mm} ({r.Duracion.TotalHours:0.#} horas)");
        AgregarFila(html, "Numero de invitados", r.NumeroInvitados.ToString(Cultura));

        if (!string.IsNullOrWhiteSpace(r.Observacion))
        {
            AgregarFila(html, "Observacion", r.Observacion);
        }

        if (!esConfirmacion && !string.IsNullOrWhiteSpace(datos.Motivo))
        {
            AgregarFila(html, "Motivo de cancelacion", datos.Motivo);
        }

        html.Append("</table>");

        // ---------- Tabla de recursos ----------
        html.Append("<h3 style=\"margin:0 0 10px;font-size:15px;\">Recursos y servicios</h3>")
            .Append("<table role=\"presentation\" cellpadding=\"8\" cellspacing=\"0\" ")
            .Append("style=\"width:100%;border-collapse:collapse;font-size:13px;margin:0 0 20px;\">")
            .Append("<thead><tr style=\"background:#0d3b66;color:#ffffff;text-align:left;\">")
            .Append("<th style=\"border:1px solid #dee2e6;\">Recurso</th>")
            .Append("<th style=\"border:1px solid #dee2e6;\">Tipo</th>")
            .Append("<th style=\"border:1px solid #dee2e6;text-align:right;\">Cantidad</th>")
            .Append("<th style=\"border:1px solid #dee2e6;text-align:right;\">Precio</th>")
            .Append("<th style=\"border:1px solid #dee2e6;text-align:right;\">Desc.</th>")
            .Append("<th style=\"border:1px solid #dee2e6;text-align:right;\">Subtotal</th>")
            .Append("</tr></thead><tbody>");

        var alterna = false;

        foreach (var d in r.Detalles)
        {
            var fondo = alterna ? "#f8f9fa" : "#ffffff";
            alterna = !alterna;

            html.Append("<tr style=\"background:").Append(fondo).Append(";\">")
                .Append("<td style=\"border:1px solid #dee2e6;\">").Append(E(d.RecursoNombre)).Append("</td>")
                .Append("<td style=\"border:1px solid #dee2e6;\">").Append(E(d.RecursoTipo)).Append("</td>")
                .Append("<td style=\"border:1px solid #dee2e6;text-align:right;\">")
                .Append(d.Cantidad.ToString(Cultura)).Append("</td>")
                .Append("<td style=\"border:1px solid #dee2e6;text-align:right;\">")
                .Append(Moneda(d.PrecioUnitario)).Append("</td>")
                .Append("<td style=\"border:1px solid #dee2e6;text-align:right;\">")
                .Append(d.PorcentajeDescuento.ToString("0.##", Cultura)).Append(" %</td>")
                .Append("<td style=\"border:1px solid #dee2e6;text-align:right;\">")
                .Append(Moneda(d.SubtotalLinea)).Append("</td></tr>");
        }

        html.Append("</tbody></table>");

        // ---------- Totales ----------
        html.Append("<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" ")
            .Append("style=\"width:100%;max-width:340px;margin-left:auto;font-size:14px;\">");

        AgregarTotal(html, "Subtotal", Moneda(r.Subtotal), false);

        if (r.Descuento > 0m)
        {
            AgregarTotal(html, $"Descuento ({r.PorcentajeDescuentoGlobal.ToString("0.##", Cultura)} %)",
                "-" + Moneda(r.Descuento), false);
        }

        AgregarTotal(html, "Impuesto (15 %)", Moneda(r.Impuesto), false);
        AgregarTotal(html, "TOTAL", Moneda(r.Total), true);

        html.Append("</table>");

        // ---------- Pie ----------
        html.Append("<hr style=\"border:none;border-top:1px solid #dee2e6;margin:24px 0 14px;\">")
            .Append("<p style=\"margin:0;font-size:12px;color:#6c757d;line-height:1.5;\">")
            .Append("Este mensaje fue generado automaticamente por SmartEvent AI. ")
            .Append("Por favor no responda a esta direccion.<br>")
            .Append("Generado el ")
            .Append(E(DateTime.Now.ToString("dd/MM/yyyy HH:mm", Cultura)))
            .Append("</p>");

        html.Append("</td></tr></table></body></html>");

        return html.ToString();
    }

    // ===================== ENVIO =====================

    public async Task<ResultadoEnvioCorreo> EnviarAsync(DatosCorreoReserva datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var cronometro = Stopwatch.StartNew();
        var servidor = _opciones.Descripcion;

        if (!_opciones.EstaConfigurado)
        {
            cronometro.Stop();
            _registro.Advertencia("Se intento enviar un correo sin configuracion SMTP.");

            return new ResultadoEnvioCorreo(
                false,
                "No hay un servidor de correo configurado. La reserva se guardo correctamente, "
                + "pero no se notifico al cliente. Puede reintentar el envio desde la consulta de reservas.",
                "Configuracion SMTP ausente (Smtp:Host o Smtp:Puerto sin valor).",
                servidor,
                0);
        }

        var destinatario = datos.Reserva.ClienteEmail;

        if (!Dominio.Reglas.ReglasReserva.EmailEsValido(destinatario))
        {
            cronometro.Stop();

            return new ResultadoEnvioCorreo(
                false,
                "El cliente no tiene un correo electronico valido, por lo que no se pudo notificar.",
                $"Direccion de destino invalida para la reserva {datos.Reserva.Codigo}.",
                servidor,
                (int)cronometro.ElapsedMilliseconds);
        }

        // Tiempo maximo total del envio: si el servidor no responde, la
        // operacion se cancela sola y la interfaz no queda bloqueada.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _opciones.SegundosTiempoEspera)));

        try
        {
            var mensaje = ConstruirMensaje(datos, destinatario);

            using var cliente = new SmtpClient
            {
                Timeout = Math.Max(5, _opciones.SegundosTiempoEspera) * 1000
            };

            var seguridad = _opciones.UsarSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await cliente.ConnectAsync(_opciones.Host, _opciones.Puerto, seguridad, limite.Token)
                         .ConfigureAwait(false);

            // RequiereAutenticacion ya garantiza que ninguno de los dos es nulo
            // ni vacio; los operadores de null se ponen para que el compilador
            // pueda comprobarlo sin depender de esa propiedad.
            if (_opciones.RequiereAutenticacion)
            {
                await cliente.AuthenticateAsync(
                        _opciones.Usuario ?? string.Empty,
                        _opciones.Password ?? string.Empty,
                        limite.Token)
                    .ConfigureAwait(false);
            }

            await cliente.SendAsync(mensaje, limite.Token).ConfigureAwait(false);
            await cliente.DisconnectAsync(true, limite.Token).ConfigureAwait(false);

            cronometro.Stop();

            // Se registra el destinatario y el servidor, nunca la contrasena.
            _registro.Informacion(
                $"Correo enviado. Reserva={datos.Reserva.Codigo} Destino={destinatario} "
                + $"Servidor={servidor} Duracion={cronometro.ElapsedMilliseconds}ms.");

            return new ResultadoEnvioCorreo(
                true,
                $"Se notifico al cliente en {destinatario}.",
                null,
                servidor,
                (int)cronometro.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            // Cancelacion pedida por el usuario: se propaga.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Se agoto el tiempo de espera propio.
            cronometro.Stop();
            return Fallo(datos, servidor, cronometro,
                "El servidor de correo no respondio dentro del tiempo de espera.",
                $"Tiempo de espera agotado tras {_opciones.SegundosTiempoEspera} segundos contra {servidor}.");
        }
        catch (AuthenticationException ex)
        {
            cronometro.Stop();
            return Fallo(datos, servidor, cronometro,
                "El servidor de correo rechazo las credenciales configuradas.",
                $"Autenticacion SMTP rechazada por {servidor}. {ex.GetType().Name}.");
        }
        catch (SmtpCommandException ex)
        {
            cronometro.Stop();
            return Fallo(datos, servidor, cronometro,
                "El servidor de correo rechazo el mensaje.",
                $"SmtpCommandException {ex.StatusCode} en {ex.ErrorCode}: {ex.Message}");
        }
        catch (SmtpProtocolException ex)
        {
            cronometro.Stop();
            return Fallo(datos, servidor, cronometro,
                "Ocurrio un error de protocolo al comunicarse con el servidor de correo.",
                $"SmtpProtocolException contra {servidor}: {ex.Message}");
        }
        catch (SocketException ex)
        {
            cronometro.Stop();
            return Fallo(datos, servidor, cronometro,
                "No se pudo conectar con el servidor de correo. Verifique que este disponible.",
                $"SocketException {ex.SocketErrorCode} al conectar con {servidor}.");
        }
        catch (IOException ex)
        {
            cronometro.Stop();
            return Fallo(datos, servidor, cronometro,
                "Se perdio la conexion con el servidor de correo durante el envio.",
                $"IOException contra {servidor}: {ex.Message}");
        }
    }

    private ResultadoEnvioCorreo Fallo(
        DatosCorreoReserva datos,
        string servidor,
        Stopwatch cronometro,
        string mensajeUsuario,
        string detalleTecnico)
    {
        _registro.Advertencia(
            $"Fallo el envio de correo. Reserva={datos.Reserva.Codigo} Servidor={servidor}. {detalleTecnico}");

        return new ResultadoEnvioCorreo(
            false,
            mensajeUsuario + " La reserva NO se modifico; puede reintentar el envio desde la consulta de reservas.",
            detalleTecnico,
            servidor,
            (int)cronometro.ElapsedMilliseconds);
    }

    private MimeMessage ConstruirMensaje(DatosCorreoReserva datos, string destinatario)
    {
        var mensaje = new MimeMessage();

        mensaje.From.Add(new MailboxAddress(_opciones.RemitenteNombre, _opciones.RemitenteCorreo));
        mensaje.To.Add(new MailboxAddress(datos.Reserva.ClienteNombres, destinatario));
        mensaje.Subject = ConstruirAsunto(datos);

        var cuerpo = new BodyBuilder
        {
            HtmlBody = ConstruirCuerpoHtml(datos),
            TextBody = ConstruirCuerpoTexto(datos)
        };

        mensaje.Body = cuerpo.ToMessageBody();
        return mensaje;
    }

    /// <summary>
    /// Version en texto plano del mensaje. Se incluye porque algunos clientes
    /// de correo no muestran HTML, y ademas mejora la puntuacion antispam.
    /// </summary>
    private static string ConstruirCuerpoTexto(DatosCorreoReserva datos)
    {
        var r = datos.Reserva;
        var texto = new StringBuilder();

        texto.AppendLine("SMARTEVENT - " + (datos.TipoEvento == TipoEventoCorreo.Confirmacion
                ? "RESERVA CONFIRMADA" : "RESERVA CANCELADA"))
             .AppendLine()
             .AppendLine(Cultura, $"Estimado(a) {r.ClienteNombres}:")
             .AppendLine()
             .AppendLine(Cultura, $"Codigo .............. {r.Codigo}")
             .AppendLine(Cultura, $"Estado .............. {MaquinaEstadosReserva.ATexto(r.Estado)}")
             .AppendLine(Cultura, $"Salon ............... {r.SalonNombre}")
             .AppendLine(Cultura, $"Fecha ............... {r.FechaEvento:dd/MM/yyyy}")
             .AppendLine(Cultura, $"Horario ............. {r.HoraInicio:hh\\:mm} a {r.HoraFin:hh\\:mm}")
             .AppendLine(Cultura, $"Invitados ........... {r.NumeroInvitados}")
             .AppendLine();

        if (!string.IsNullOrWhiteSpace(datos.Motivo))
        {
            texto.AppendLine(Cultura, $"Motivo: {datos.Motivo}").AppendLine();
        }

        texto.AppendLine("RECURSOS Y SERVICIOS");

        foreach (var d in r.Detalles)
        {
            texto.AppendLine(Cultura,
                $"  - {d.RecursoNombre} x{d.Cantidad} ... {Moneda(d.SubtotalLinea)}");
        }

        texto.AppendLine()
             .AppendLine(Cultura, $"Subtotal ............ {Moneda(r.Subtotal)}");

        if (r.Descuento > 0m)
        {
            texto.AppendLine(Cultura, $"Descuento ........... -{Moneda(r.Descuento)}");
        }

        texto.AppendLine(Cultura, $"Impuesto (15%) ...... {Moneda(r.Impuesto)}")
             .AppendLine(Cultura, $"TOTAL ............... {Moneda(r.Total)}")
             .AppendLine()
             .AppendLine("Mensaje generado automaticamente por SmartEvent AI. No responda a esta direccion.");

        return texto.ToString();
    }

    // ===================== APOYO =====================

    /// <summary>
    /// Codificacion HTML de un valor. El nombre es corto a proposito porque se
    /// usa en cada dato insertado en la plantilla: si se ve una interpolacion
    /// sin E(...) alrededor, es un error.
    /// </summary>
    private static string E(string? valor) => WebUtility.HtmlEncode(valor ?? string.Empty);

    private static string Moneda(decimal valor) => valor.ToString("C2", Cultura);

    private static void AgregarFila(StringBuilder html, string etiqueta, string valor)
    {
        html.Append("<tr><td style=\"padding:5px 0;color:#6c757d;width:190px;vertical-align:top;\">")
            .Append(E(etiqueta)).Append("</td>")
            .Append("<td style=\"padding:5px 0;font-weight:600;\">").Append(E(valor)).Append("</td></tr>");
    }

    private static void AgregarTotal(StringBuilder html, string etiqueta, string valor, bool destacado)
    {
        var estiloCelda = destacado
            ? "border-top:2px solid #0d3b66;font-size:16px;font-weight:700;color:#0d3b66;"
            : "border-top:1px solid #dee2e6;";

        html.Append("<tr><td style=\"").Append(estiloCelda).Append("\">").Append(E(etiqueta)).Append("</td>")
            .Append("<td style=\"").Append(estiloCelda).Append("text-align:right;\">")
            .Append(E(valor)).Append("</td></tr>");
    }
}
