using System.Globalization;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Auditoria de integraciones: intentos de correo y analisis de IA.
///
/// Cumple los requisitos F31 a F34 del examen.
///
/// SOBRE LOS SECRETOS: esta pantalla muestra los errores tecnicos para poder
/// diagnosticar, que es justo lo que pide el examen ("los errores tecnicos
/// deben estar disponibles para diagnostico SIN EXPONER SECRETOS"). Puede
/// hacerlo con tranquilidad porque el secreto nunca llego a la base de datos:
///   - de la configuracion SMTP solo se guarda host y puerto
///   - de OpenAI solo se guardan el proveedor y el modelo
///   - la API key y la contrasena de correo no se persisten en ningun campo
///
/// Es decir, la proteccion no consiste en ocultar datos aqui, sino en no
/// haberlos guardado nunca.
///
/// Esta es ademas la pantalla donde se DEMUESTRA CA-07: tras una falla de SMTP
/// y un reintento, se ven dos filas para la misma reserva, una en ERROR y otra
/// en ENVIADO, con numeros de intento 1 y 2.
/// </summary>
internal sealed class FrmAuditoriaIntegraciones : Form
{
    private readonly ServicioReservas _reservas;
    private readonly IServicioCorreo _correo;
    private readonly IServicioAnalisisIa _ia;
    private readonly IRegistradorSeguro _registro;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-EC");

    private CancellationTokenSource? _cancelacion;

    private readonly TabControl _pestanas = new();

    // ---------- Correos ----------
    private readonly TextBox _txtCodigoCorreo = new();
    private readonly TextBox _txtDestinatario = new();
    private readonly ComboBox _cboEstadoCorreo = new();
    private readonly ComboBox _cboTipoEvento = new();
    private readonly DataGridView _grillaCorreos = new();
    private readonly TextBox _txtDetalleCorreo = new();
    private readonly Label _lblResumenCorreos = new();

    // ---------- Analisis de IA ----------
    private readonly TextBox _txtCodigoIa = new();
    private readonly ComboBox _cboNivelRiesgo = new();
    private readonly CheckBox _chkSoloErroresIa = new();
    private readonly DataGridView _grillaIa = new();
    private readonly TextBox _txtDetalleIa = new();
    private readonly Label _lblResumenIa = new();

    public FrmAuditoriaIntegraciones(
        ServicioReservas reservas,
        IServicioCorreo correo,
        IServicioAnalisisIa ia,
        IRegistradorSeguro registro)
    {
        _reservas = reservas ?? throw new ArgumentNullException(nameof(reservas));
        _correo = correo ?? throw new ArgumentNullException(nameof(correo));
        _ia = ia ?? throw new ArgumentNullException(nameof(ia));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));

        ConstruirInterfaz();
    }

    private void ConstruirInterfaz()
    {
        Text = "Auditoria de integraciones";
        WindowState = FormWindowState.Maximized;
        BackColor = AyudasUi.Paleta.Fondo;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(1120, 660);

        _pestanas.Dock = DockStyle.Fill;
        _pestanas.Font = new Font("Segoe UI", 10F);
        _pestanas.Padding = new Point(16, 6);

        _pestanas.TabPages.Add(ConstruirPestanaCorreos());
        _pestanas.TabPages.Add(ConstruirPestanaIa());
        _pestanas.TabPages.Add(ConstruirPestanaConfiguracion());

        Controls.Add(_pestanas);

        Shown += async (_, _) =>
        {
            _cancelacion = new CancellationTokenSource();
            await CargarCorreosAsync();
            await CargarAnalisisAsync();
        };

        FormClosing += (_, _) => _cancelacion?.Cancel();
    }

    // =====================================================================
    // CORREOS
    // =====================================================================

    private TabPage ConstruirPestanaCorreos()
    {
        var pagina = new TabPage("Intentos de correo")
        {
            BackColor = AyudasUi.Paleta.Fondo,
            Padding = new Padding(12)
        };

        // ---------- Filtros ----------
        var filtros = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White };

        filtros.Controls.Add(EtiquetaFiltro("Codigo de reserva", 12, 6));
        _txtCodigoCorreo.Location = new Point(12, 24);
        _txtCodigoCorreo.Size = new Size(180, 26);
        _txtCodigoCorreo.BorderStyle = BorderStyle.FixedSingle;
        _txtCodigoCorreo.PlaceholderText = "RSV-...";
        filtros.Controls.Add(_txtCodigoCorreo);

        filtros.Controls.Add(EtiquetaFiltro("Destinatario", 202, 6));
        _txtDestinatario.Location = new Point(202, 24);
        _txtDestinatario.Size = new Size(220, 26);
        _txtDestinatario.BorderStyle = BorderStyle.FixedSingle;
        filtros.Controls.Add(_txtDestinatario);

        filtros.Controls.Add(EtiquetaFiltro("Estado", 432, 6));
        _cboEstadoCorreo.Location = new Point(432, 24);
        _cboEstadoCorreo.Size = new Size(140, 26);
        _cboEstadoCorreo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboEstadoCorreo.FlatStyle = FlatStyle.Flat;
        _cboEstadoCorreo.Items.AddRange(["(todos)", "ENVIADO", "ERROR"]);
        _cboEstadoCorreo.SelectedIndex = 0;
        filtros.Controls.Add(_cboEstadoCorreo);

        filtros.Controls.Add(EtiquetaFiltro("Tipo", 582, 6));
        _cboTipoEvento.Location = new Point(582, 24);
        _cboTipoEvento.Size = new Size(160, 26);
        _cboTipoEvento.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboTipoEvento.FlatStyle = FlatStyle.Flat;
        _cboTipoEvento.Items.AddRange(["(todos)", "CONFIRMACION", "CANCELACION"]);
        _cboTipoEvento.SelectedIndex = 0;
        filtros.Controls.Add(_cboTipoEvento);

        var btnBuscar = new Button { Text = "Buscar", Location = new Point(756, 22), Size = new Size(120, 30) };
        btnBuscar.EstiloPrimario();
        btnBuscar.Click += async (_, _) => await CargarCorreosAsync();
        filtros.Controls.Add(btnBuscar);

        var btnLimpiar = new Button { Text = "Limpiar", Location = new Point(886, 22), Size = new Size(110, 30) };
        btnLimpiar.EstiloSecundario();
        btnLimpiar.Click += async (_, _) =>
        {
            _txtCodigoCorreo.Clear();
            _txtDestinatario.Clear();
            _cboEstadoCorreo.SelectedIndex = 0;
            _cboTipoEvento.SelectedIndex = 0;
            await CargarCorreosAsync();
        };
        filtros.Controls.Add(btnLimpiar);

        _lblResumenCorreos.Location = new Point(1010, 28);
        _lblResumenCorreos.Size = new Size(300, 20);
        _lblResumenCorreos.ForeColor = AyudasUi.Paleta.TextoSuave;
        filtros.Controls.Add(_lblResumenCorreos);

        // ---------- Grilla ----------
        _grillaCorreos.Dock = DockStyle.Fill;
        _grillaCorreos.EstiloEstandar();
        _grillaCorreos.AutoGenerateColumns = false;

        AgregarColumna(_grillaCorreos, "ReservaCodigo", "Reserva", nameof(CorreoEnviado.ReservaCodigo), 70);
        AgregarColumna(_grillaCorreos, "Destinatario", "Destinatario", nameof(CorreoEnviado.Destinatario), 130);
        AgregarColumna(_grillaCorreos, "TipoEvento", "Tipo", nameof(CorreoEnviado.TipoEvento), 70);
        AgregarColumna(_grillaCorreos, "Intento", "Intento", nameof(CorreoEnviado.Intento), 40,
            alineacion: DataGridViewContentAlignment.MiddleCenter);
        AgregarColumna(_grillaCorreos, "Estado", "Estado", nameof(CorreoEnviado.Estado), 55,
            alineacion: DataGridViewContentAlignment.MiddleCenter);
        AgregarColumna(_grillaCorreos, "ServidorSmtp", "Servidor", nameof(CorreoEnviado.ServidorSmtp), 80);
        AgregarColumna(_grillaCorreos, "DuracionMs", "ms", nameof(CorreoEnviado.DuracionMs), 40,
            alineacion: DataGridViewContentAlignment.MiddleRight);
        AgregarColumna(_grillaCorreos, "FechaIntento", "Fecha", nameof(CorreoEnviado.FechaIntento), 80, "g");
        AgregarColumna(_grillaCorreos, "Usuario", "Usuario", nameof(CorreoEnviado.Usuario), 60);

        _grillaCorreos.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grillaCorreos.Rows[e.RowIndex].DataBoundItem is not CorreoEnviado fila)
            {
                return;
            }

            e.CellStyle!.BackColor = fila.FueExitoso
                ? Color.FromArgb(209, 240, 220)
                : Color.FromArgb(248, 215, 218);

            e.CellStyle.ForeColor = fila.FueExitoso
                ? Color.FromArgb(21, 87, 36)
                : Color.FromArgb(114, 28, 36);

            var nombre = _grillaCorreos.Columns[e.ColumnIndex].Name;

            if (nombre == "Estado")
            {
                e.Value = TextosEnumeracion.ATexto(fila.Estado);
                e.FormattingApplied = true;
            }
            else if (nombre == "TipoEvento")
            {
                e.Value = TextosEnumeracion.ATexto(fila.TipoEvento);
                e.FormattingApplied = true;
            }
        };

        _grillaCorreos.SelectionChanged += (_, _) =>
        {
            if (_grillaCorreos.CurrentRow?.DataBoundItem is CorreoEnviado fila)
            {
                MostrarDetalleCorreo(fila);
            }
        };

        // ---------- Detalle tecnico ----------
        var panelDetalle = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            BackColor = Color.White,
            Padding = new Padding(12)
        };

        panelDetalle.Controls.Add(new Label
        {
            Text = "Detalle tecnico del intento seleccionado",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(12, 6)
        });

        _txtDetalleCorreo.Location = new Point(12, 28);
        _txtDetalleCorreo.Size = new Size(1080, 106);
        _txtDetalleCorreo.Multiline = true;
        _txtDetalleCorreo.ReadOnly = true;
        _txtDetalleCorreo.ScrollBars = ScrollBars.Vertical;
        _txtDetalleCorreo.BorderStyle = BorderStyle.FixedSingle;
        _txtDetalleCorreo.BackColor = Color.FromArgb(248, 249, 250);
        _txtDetalleCorreo.Font = new Font("Consolas", 9F);
        _txtDetalleCorreo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        panelDetalle.Controls.Add(_txtDetalleCorreo);

        pagina.Controls.Add(_grillaCorreos);
        pagina.Controls.Add(panelDetalle);
        pagina.Controls.Add(filtros);

        return pagina;
    }

    private void MostrarDetalleCorreo(CorreoEnviado fila)
    {
        var texto =
            $"Reserva ......... {fila.ReservaCodigo}" + Environment.NewLine
            + $"Asunto .......... {fila.Asunto}" + Environment.NewLine
            + $"Destinatario .... {fila.Destinatario}" + Environment.NewLine
            + $"Tipo de evento .. {TextosEnumeracion.ATexto(fila.TipoEvento)}" + Environment.NewLine
            + $"Numero de intento {fila.Intento}" + Environment.NewLine
            + $"Estado .......... {TextosEnumeracion.ATexto(fila.Estado)}" + Environment.NewLine
            + $"Servidor SMTP ... {fila.ServidorSmtp ?? "(no registrado)"}" + Environment.NewLine
            + $"Duracion ........ {fila.DuracionMs?.ToString(Cultura) ?? "-"} ms" + Environment.NewLine
            + $"Fecha ........... {fila.FechaIntento.ToString("dd/MM/yyyy HH:mm:ss", Cultura)}"
            + Environment.NewLine
            + $"Usuario ......... {fila.Usuario}";

        if (!string.IsNullOrWhiteSpace(fila.Error))
        {
            texto += Environment.NewLine + Environment.NewLine
                   + "ERROR REGISTRADO:" + Environment.NewLine + fila.Error;
        }

        _txtDetalleCorreo.Text = texto;
    }

    private async Task CargarCorreosAsync()
    {
        if (_cancelacion is null) { return; }

        var filtro = new FiltroAuditoriaCorreo
        {
            Codigo = string.IsNullOrWhiteSpace(_txtCodigoCorreo.Text) ? null : _txtCodigoCorreo.Text.Trim(),
            Destinatario = string.IsNullOrWhiteSpace(_txtDestinatario.Text) ? null : _txtDestinatario.Text.Trim(),
            Estado = _cboEstadoCorreo.SelectedIndex > 0
                ? TextosEnumeracion.EstadoCorreoDesde(_cboEstadoCorreo.SelectedItem!.ToString()!)
                : null,
            TipoEvento = _cboTipoEvento.SelectedIndex > 0
                ? TextosEnumeracion.TipoEventoDesde(_cboTipoEvento.SelectedItem!.ToString()!)
                : null,
            MaximoFilas = 300
        };

        var lista = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ConsultarCorreosAsync(filtro, _cancelacion.Token),
            "No se pudo consultar la auditoria de correos.");

        if (lista is null) { return; }

        _grillaCorreos.DataSource = lista.ToList();

        var errores = lista.Count(c => !c.FueExitoso);
        _lblResumenCorreos.Text = $"{lista.Count} intento(s), {errores} con error.";
    }

    // =====================================================================
    // ANALISIS DE IA
    // =====================================================================

    private TabPage ConstruirPestanaIa()
    {
        var pagina = new TabPage("Analisis de IA")
        {
            BackColor = AyudasUi.Paleta.Fondo,
            Padding = new Padding(12)
        };

        var filtros = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White };

        filtros.Controls.Add(EtiquetaFiltro("Codigo de reserva", 12, 6));
        _txtCodigoIa.Location = new Point(12, 24);
        _txtCodigoIa.Size = new Size(180, 26);
        _txtCodigoIa.BorderStyle = BorderStyle.FixedSingle;
        _txtCodigoIa.PlaceholderText = "RSV-...";
        filtros.Controls.Add(_txtCodigoIa);

        filtros.Controls.Add(EtiquetaFiltro("Nivel de riesgo", 202, 6));
        _cboNivelRiesgo.Location = new Point(202, 24);
        _cboNivelRiesgo.Size = new Size(150, 26);
        _cboNivelRiesgo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboNivelRiesgo.FlatStyle = FlatStyle.Flat;
        _cboNivelRiesgo.Items.AddRange(["(todos)", "BAJO", "MEDIO", "ALTO"]);
        _cboNivelRiesgo.SelectedIndex = 0;
        filtros.Controls.Add(_cboNivelRiesgo);

        _chkSoloErroresIa.Text = "Solo analisis fallidos";
        _chkSoloErroresIa.Location = new Point(372, 28);
        _chkSoloErroresIa.AutoSize = true;
        filtros.Controls.Add(_chkSoloErroresIa);

        var btnBuscar = new Button { Text = "Buscar", Location = new Point(540, 22), Size = new Size(120, 30) };
        btnBuscar.EstiloPrimario();
        btnBuscar.Click += async (_, _) => await CargarAnalisisAsync();
        filtros.Controls.Add(btnBuscar);

        var btnLimpiar = new Button { Text = "Limpiar", Location = new Point(670, 22), Size = new Size(110, 30) };
        btnLimpiar.EstiloSecundario();
        btnLimpiar.Click += async (_, _) =>
        {
            _txtCodigoIa.Clear();
            _cboNivelRiesgo.SelectedIndex = 0;
            _chkSoloErroresIa.Checked = false;
            await CargarAnalisisAsync();
        };
        filtros.Controls.Add(btnLimpiar);

        _lblResumenIa.Location = new Point(800, 28);
        _lblResumenIa.Size = new Size(340, 20);
        _lblResumenIa.ForeColor = AyudasUi.Paleta.TextoSuave;
        filtros.Controls.Add(_lblResumenIa);

        _grillaIa.Dock = DockStyle.Fill;
        _grillaIa.EstiloEstandar();
        _grillaIa.AutoGenerateColumns = false;

        AgregarColumna(_grillaIa, "ReservaCodigo", "Reserva", nameof(AnalisisIa.ReservaCodigo), 70);
        AgregarColumna(_grillaIa, "Proveedor", "Proveedor", nameof(AnalisisIa.Proveedor), 60);
        AgregarColumna(_grillaIa, "Modelo", "Modelo", nameof(AnalisisIa.Modelo), 100);
        AgregarColumna(_grillaIa, "PromptVersion", "Prompt", nameof(AnalisisIa.PromptVersion), 40,
            alineacion: DataGridViewContentAlignment.MiddleCenter);
        AgregarColumna(_grillaIa, "Riesgo", "Riesgo", nameof(AnalisisIa.NivelRiesgo), 50,
            alineacion: DataGridViewContentAlignment.MiddleCenter);
        AgregarColumna(_grillaIa, "Exitoso", "Resultado", nameof(AnalisisIa.Exitoso), 60,
            alineacion: DataGridViewContentAlignment.MiddleCenter);
        AgregarColumna(_grillaIa, "TokensEntrada", "Tok. ent.", nameof(AnalisisIa.TokensEntrada), 45,
            alineacion: DataGridViewContentAlignment.MiddleRight);
        AgregarColumna(_grillaIa, "TokensSalida", "Tok. sal.", nameof(AnalisisIa.TokensSalida), 45,
            alineacion: DataGridViewContentAlignment.MiddleRight);
        AgregarColumna(_grillaIa, "DuracionMs", "ms", nameof(AnalisisIa.DuracionMs), 40,
            alineacion: DataGridViewContentAlignment.MiddleRight);
        AgregarColumna(_grillaIa, "Fecha", "Fecha", nameof(AnalisisIa.Fecha), 80, "g");
        AgregarColumna(_grillaIa, "Usuario", "Usuario", nameof(AnalisisIa.Usuario), 60);

        _grillaIa.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grillaIa.Rows[e.RowIndex].DataBoundItem is not AnalisisIa fila)
            {
                return;
            }

            e.CellStyle!.BackColor = fila.Exitoso
                ? Color.FromArgb(209, 240, 220)
                : fila.EsContingenciaManual
                    ? Color.FromArgb(255, 243, 205)
                    : Color.FromArgb(248, 215, 218);

            var nombre = _grillaIa.Columns[e.ColumnIndex].Name;

            if (nombre == "Exitoso")
            {
                e.Value = fila.Exitoso
                    ? "Correcto"
                    : fila.EsContingenciaManual ? "Contingencia" : "Fallido";
                e.FormattingApplied = true;
            }
            else if (nombre == "Riesgo")
            {
                e.Value = fila.NivelRiesgo.HasValue
                    ? TextosEnumeracion.ATexto(fila.NivelRiesgo.Value)
                    : "-";
                e.FormattingApplied = true;
            }
        };

        _grillaIa.SelectionChanged += (_, _) =>
        {
            if (_grillaIa.CurrentRow?.DataBoundItem is AnalisisIa fila)
            {
                MostrarDetalleIa(fila);
            }
        };

        var panelDetalle = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 190,
            BackColor = Color.White,
            Padding = new Padding(12)
        };

        panelDetalle.Controls.Add(new Label
        {
            Text = "Respuesta JSON persistida y detalle del analisis seleccionado",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(12, 6)
        });

        _txtDetalleIa.Location = new Point(12, 28);
        _txtDetalleIa.Size = new Size(1080, 146);
        _txtDetalleIa.Multiline = true;
        _txtDetalleIa.ReadOnly = true;
        _txtDetalleIa.ScrollBars = ScrollBars.Both;
        _txtDetalleIa.WordWrap = false;
        _txtDetalleIa.BorderStyle = BorderStyle.FixedSingle;
        _txtDetalleIa.BackColor = Color.FromArgb(248, 249, 250);
        _txtDetalleIa.Font = new Font("Consolas", 9F);
        _txtDetalleIa.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        panelDetalle.Controls.Add(_txtDetalleIa);

        pagina.Controls.Add(_grillaIa);
        pagina.Controls.Add(panelDetalle);
        pagina.Controls.Add(filtros);

        return pagina;
    }

    private void MostrarDetalleIa(AnalisisIa fila)
    {
        var texto =
            $"Reserva ......... {fila.ReservaCodigo}" + Environment.NewLine
            + $"Proveedor ....... {fila.Proveedor}" + Environment.NewLine
            + $"Modelo .......... {fila.Modelo}" + Environment.NewLine
            + $"Version prompt .. {fila.PromptVersion}" + Environment.NewLine
            + $"Resultado ....... {(fila.Exitoso ? "Correcto" : "Fallido")}" + Environment.NewLine
            + $"Fecha ........... {fila.Fecha.ToString("dd/MM/yyyy HH:mm:ss", Cultura)}" + Environment.NewLine
            + $"Usuario ......... {fila.Usuario}";

        if (fila.EsContingenciaManual)
        {
            texto += Environment.NewLine + Environment.NewLine
                   + "CONTINGENCIA MANUAL AUTORIZADA" + Environment.NewLine
                   + "Justificacion: " + fila.JustificacionContingencia;
        }

        if (!string.IsNullOrWhiteSpace(fila.Error))
        {
            texto += Environment.NewLine + Environment.NewLine
                   + "ERROR REGISTRADO:" + Environment.NewLine + fila.Error;
        }

        if (!string.IsNullOrWhiteSpace(fila.RespuestaJson))
        {
            texto += Environment.NewLine + Environment.NewLine
                   + "RESPUESTA JSON:" + Environment.NewLine + FormatearJson(fila.RespuestaJson);
        }

        _txtDetalleIa.Text = texto;
    }

    /// <summary>
    /// Opciones de formato del JSON. Se cachean en un campo estatico porque
    /// crear un JsonSerializerOptions en cada llamada obliga a reconstruir la
    /// cache interna de metadatos del serializador, que es costosa.
    /// UnsafeRelaxedJsonEscaping evita que las tildes y la letra n con virgulilla
    /// aparezcan como secuencias \uXXXX en pantalla.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions OpcionesFormatoJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Indenta el JSON para que sea legible en pantalla y en las capturas.</summary>
    private static string FormatearJson(string json)
    {
        try
        {
            using var documento = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(documento.RootElement, OpcionesFormatoJson);
        }
        catch (System.Text.Json.JsonException)
        {
            // Si por lo que fuera no se pudiera formatear, se muestra tal cual.
            return json;
        }
    }

    private async Task CargarAnalisisAsync()
    {
        if (_cancelacion is null) { return; }

        NivelRiesgo? nivel = null;

        if (_cboNivelRiesgo.SelectedIndex > 0
            && TextosEnumeracion.TryNivelRiesgo(_cboNivelRiesgo.SelectedItem!.ToString(), out var convertido))
        {
            nivel = convertido;
        }

        var filtro = new FiltroAuditoriaIa
        {
            Codigo = string.IsNullOrWhiteSpace(_txtCodigoIa.Text) ? null : _txtCodigoIa.Text.Trim(),
            NivelRiesgo = nivel,
            SoloErrores = _chkSoloErroresIa.Checked,
            MaximoFilas = 300
        };

        var lista = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ConsultarAnalisisAsync(filtro, _cancelacion.Token),
            "No se pudo consultar la auditoria de analisis de IA.");

        if (lista is null) { return; }

        _grillaIa.DataSource = lista.ToList();

        var fallidos = lista.Count(a => !a.Exitoso);
        var contingencias = lista.Count(a => a.EsContingenciaManual);

        _lblResumenIa.Text =
            $"{lista.Count} analisis, {fallidos} fallido(s), {contingencias} contingencia(s) manual(es).";
    }

    // =====================================================================
    // CONFIGURACION VIGENTE
    // =====================================================================

    /// <summary>
    /// Muestra la configuracion ACTIVA de las integraciones, sin secretos.
    ///
    /// Sirve para diagnosticar en la defensa: permite comprobar a que servidor
    /// SMTP y a que modelo de IA esta apuntando la aplicacion, sin revelar
    /// jamas la contrasena ni la API key. De la clave solo se indica SI existe
    /// o no, nunca su valor.
    /// </summary>
    private TabPage ConstruirPestanaConfiguracion()
    {
        var pagina = new TabPage("Configuracion vigente")
        {
            BackColor = Color.White,
            Padding = new Padding(24)
        };

        var texto = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Consolas", 10F),
            Text = string.Join(Environment.NewLine,
            [
                "CONFIGURACION VIGENTE DE LAS INTEGRACIONES",
                "==========================================",
                "",
                "CORREO (SMTP)",
                $"  Servidor ................ {_correo.DescripcionServidor}",
                "  Credenciales ............ no se muestran ni se almacenan en la base de datos",
                "",
                "ANALISIS DE IA (Responses API)",
                $"  Proveedor ............... {_ia.Proveedor}",
                $"  Modelo .................. {_ia.Modelo}",
                $"  Version del prompt ...... {_ia.PromptVersion}",
                $"  Clave configurada ....... {(_ia.EstaConfigurado ? "SI" : "NO")}",
                "  Valor de la clave ....... nunca se muestra ni se persiste",
                "",
                "REGISTRO LOCAL",
                $"  Archivo ................. {_registro.ArchivoActual}",
                "  Redaccion ............... claves, tokens y cadenas de conexion se enmascaran",
                "                            automaticamente antes de escribirse en disco",
                "",
                "NOTA PARA LA DEFENSA",
                "  Los errores tecnicos de esta pantalla se pueden mostrar con tranquilidad",
                "  porque los secretos nunca llegaron a la base de datos: de SMTP solo se",
                "  guarda host y puerto, y de OpenAI solo el proveedor y el modelo."
            ])
        };

        pagina.Controls.Add(texto);
        return pagina;
    }

    // =====================================================================
    // APOYO
    // =====================================================================

    private static Label EtiquetaFiltro(string texto, int x, int y) => new()
    {
        Text = texto,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = AyudasUi.Paleta.TextoSuave,
        Font = new Font("Segoe UI", 8.5F)
    };

    private static void AgregarColumna(
        DataGridView grilla, string nombre, string titulo, string propiedad, int peso,
        string? formato = null,
        DataGridViewContentAlignment alineacion = DataGridViewContentAlignment.MiddleLeft)
    {
        var columna = new DataGridViewTextBoxColumn
        {
            Name = nombre,
            HeaderText = titulo,
            DataPropertyName = propiedad,
            FillWeight = peso,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alineacion }
        };

        if (formato is not null)
        {
            columna.DefaultCellStyle.Format = formato;
        }

        grilla.Columns.Add(columna);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancelacion?.Cancel();
            _cancelacion?.Dispose();
        }

        base.Dispose(disposing);
    }
}
