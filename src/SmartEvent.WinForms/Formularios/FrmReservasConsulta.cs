using System.Globalization;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.Aplicacion.Sesion;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Consulta historica de reservas.
///
/// Cumple los requisitos F26 a F30 del examen: filtros combinados, carga
/// progresiva, doble clic para abrir el detalle, operaciones asincronicas con
/// CancellationToken y estados visualmente identificables.
///
/// SOBRE LA CARGA PROGRESIVA: el procedimiento almacenado pagina con
/// OFFSET/FETCH y devuelve el total de filas que cumplen el filtro. El boton
/// "Cargar mas" anade la siguiente pagina a la grilla en lugar de reemplazarla,
/// de modo que nunca se traen miles de filas de golpe.
///
/// SOBRE LA CANCELACION: cada nueva busqueda cancela la anterior. Si el usuario
/// escribe rapido y lanza tres busquedas seguidas, solo la ultima llega a
/// pintarse, y las otras dos se abandonan sin bloquear la interfaz.
///
/// Aqui vive tambien el REENVIO DE CORREO, que es lo que permite demostrar
/// CA-07: reintentar el envio sin duplicar la reserva ni el cambio de estado.
/// </summary>
internal sealed class FrmReservasConsulta : Form
{
    private readonly ServicioReservas _reservas;
    private readonly ServicioCatalogos _catalogos;
    private readonly SesionUsuario _sesion;
    private readonly IRegistradorSeguro _registro;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-EC");
    private const int TamanoPagina = 25;

    private CancellationTokenSource? _cancelacion;
    private readonly List<ResumenReserva> _filas = [];
    private int _paginaActual = 1;
    private int _totalFilas;

    // ---------- Filtros ----------
    private readonly TextBox _txtCodigo = new();
    private readonly TextBox _txtCliente = new();
    private readonly ComboBox _cboSalon = new();
    private readonly ComboBox _cboEstado = new();
    private readonly CheckBox _chkUsarFechas = new();
    private readonly DateTimePicker _dtpDesde = new();
    private readonly DateTimePicker _dtpHasta = new();
    private readonly Button _btnBuscar = new();
    private readonly Button _btnLimpiar = new();

    // ---------- Resultados ----------
    private readonly DataGridView _grilla = new();
    private readonly Label _lblResumen = new();
    private readonly Button _btnCargarMas = new();
    private readonly Button _btnAbrir = new();
    private readonly Button _btnReenviarCorreo = new();
    private readonly Button _btnFinalizar = new();
    private readonly Button _btnVerAuditoria = new();

    public FrmReservasConsulta(
        ServicioReservas reservas,
        ServicioCatalogos catalogos,
        SesionUsuario sesion,
        IRegistradorSeguro registro)
    {
        _reservas = reservas ?? throw new ArgumentNullException(nameof(reservas));
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));

        ConstruirInterfaz();
    }

    private void ConstruirInterfaz()
    {
        Text = "Consulta de reservas";
        WindowState = FormWindowState.Maximized;
        BackColor = AyudasUi.Paleta.Fondo;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(1120, 660);

        var contenedor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = AyudasUi.Paleta.Fondo
        };

        contenedor.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); // filtros
        contenedor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // grilla
        contenedor.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // acciones

        contenedor.Controls.Add(ConstruirPanelFiltros(), 0, 0);
        contenedor.Controls.Add(ConstruirPanelGrilla(), 0, 1);
        contenedor.Controls.Add(ConstruirPanelAcciones(), 0, 2);

        Controls.Add(contenedor);

        Shown += FrmReservasConsultaShown;
        FormClosing += (_, _) => AyudasUi.CancelarSeguro(_cancelacion);
    }

    private Panel ConstruirPanelFiltros()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14)
        };

        panel.Controls.Add(new Label
        {
            Text = "Filtros",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(14, 8)
        });

        // ---------- Codigo ----------
        panel.Controls.Add(Etiqueta("Codigo", 14, 38));
        _txtCodigo.Location = new Point(14, 56);
        _txtCodigo.Size = new Size(190, 26);
        _txtCodigo.BorderStyle = BorderStyle.FixedSingle;
        _txtCodigo.PlaceholderText = "RSV-...";
        _txtCodigo.KeyDown += TeclaBuscar;
        panel.Controls.Add(_txtCodigo);

        // ---------- Cliente ----------
        panel.Controls.Add(Etiqueta("Cliente", 214, 38));
        _txtCliente.Location = new Point(214, 56);
        _txtCliente.Size = new Size(230, 26);
        _txtCliente.BorderStyle = BorderStyle.FixedSingle;
        _txtCliente.PlaceholderText = "Nombre o identificacion";
        _txtCliente.KeyDown += TeclaBuscar;
        panel.Controls.Add(_txtCliente);

        // ---------- Salon ----------
        panel.Controls.Add(Etiqueta("Salon", 454, 38));
        _cboSalon.Location = new Point(454, 56);
        _cboSalon.Size = new Size(210, 26);
        _cboSalon.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboSalon.FlatStyle = FlatStyle.Flat;
        panel.Controls.Add(_cboSalon);

        // ---------- Estado ----------
        panel.Controls.Add(Etiqueta("Estado", 674, 38));
        _cboEstado.Location = new Point(674, 56);
        _cboEstado.Size = new Size(160, 26);
        _cboEstado.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboEstado.FlatStyle = FlatStyle.Flat;
        _cboEstado.Items.AddRange(["(todos)", "BORRADOR", "CONFIRMADA", "FINALIZADA", "CANCELADA"]);
        _cboEstado.SelectedIndex = 0;
        panel.Controls.Add(_cboEstado);

        // ---------- Rango de fechas ----------
        _chkUsarFechas.Text = "Filtrar por rango de fechas del evento";
        _chkUsarFechas.Location = new Point(14, 92);
        _chkUsarFechas.AutoSize = true;
        _chkUsarFechas.CheckedChanged += (_, _) =>
        {
            _dtpDesde.Enabled = _chkUsarFechas.Checked;
            _dtpHasta.Enabled = _chkUsarFechas.Checked;
        };
        panel.Controls.Add(_chkUsarFechas);

        _dtpDesde.Location = new Point(264, 90);
        _dtpDesde.Size = new Size(150, 26);
        _dtpDesde.Format = DateTimePickerFormat.Short;
        _dtpDesde.Value = DateTime.Today.AddMonths(-1);
        _dtpDesde.Enabled = false;
        panel.Controls.Add(_dtpDesde);

        panel.Controls.Add(new Label
        {
            Text = "hasta", Location = new Point(422, 94), AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave
        });

        _dtpHasta.Location = new Point(468, 90);
        _dtpHasta.Size = new Size(150, 26);
        _dtpHasta.Format = DateTimePickerFormat.Short;
        _dtpHasta.Value = DateTime.Today.AddMonths(6);
        _dtpHasta.Enabled = false;
        panel.Controls.Add(_dtpHasta);

        // ---------- Botones ----------
        _btnBuscar.Text = "Buscar";
        _btnBuscar.Location = new Point(854, 52);
        _btnBuscar.Size = new Size(130, 34);
        _btnBuscar.EstiloPrimario();
        _btnBuscar.Click += async (_, _) => await BuscarAsync(reiniciar: true);
        panel.Controls.Add(_btnBuscar);

        _btnLimpiar.Text = "Limpiar";
        _btnLimpiar.Location = new Point(994, 52);
        _btnLimpiar.Size = new Size(110, 34);
        _btnLimpiar.EstiloSecundario();
        _btnLimpiar.Click += async (_, _) => await LimpiarFiltrosAsync();
        panel.Controls.Add(_btnLimpiar);

        return panel;
    }

    private Panel ConstruirPanelGrilla()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(1),
            Margin = new Padding(0, 10, 0, 10)
        };

        _grilla.Dock = DockStyle.Fill;
        _grilla.EstiloEstandar();
        _grilla.AutoGenerateColumns = false;

        AgregarColumna("Codigo", "Codigo", nameof(ResumenReserva.Codigo), 70);
        AgregarColumna("ClienteNombres", "Cliente", nameof(ResumenReserva.ClienteNombres), 130);
        AgregarColumna("SalonNombre", "Salon", nameof(ResumenReserva.SalonNombre), 90);
        AgregarColumna("FechaEvento", "Fecha", nameof(ResumenReserva.FechaEvento), 60, "d");
        AgregarColumna("Horario", "Horario", nameof(ResumenReserva.Horario), 60);
        AgregarColumna("NumeroInvitados", "Invit.", nameof(ResumenReserva.NumeroInvitados), 40,
            alineacion: DataGridViewContentAlignment.MiddleRight);
        AgregarColumna("TotalDetalles", "Lineas", nameof(ResumenReserva.TotalDetalles), 40,
            alineacion: DataGridViewContentAlignment.MiddleRight);
        AgregarColumna("Total", "Total", nameof(ResumenReserva.Total), 70, "C2",
            DataGridViewContentAlignment.MiddleRight);
        AgregarColumna("Estado", "Estado", nameof(ResumenReserva.Estado), 70,
            alineacion: DataGridViewContentAlignment.MiddleCenter);

        // Colorea la fila segun el estado: requisito "estados visualmente
        // identificables" del examen.
        _grilla.CellFormatting += GrillaCellFormatting;

        // Doble clic sobre una fila abre el detalle de la reserva.
        _grilla.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                AbrirSeleccionada();
            }
        };

        _grilla.SelectionChanged += (_, _) => ActualizarBotonesSegunSeleccion();

        panel.Controls.Add(_grilla);
        return panel;
    }

    private void AgregarColumna(
        string nombre, string titulo, string propiedad, int peso,
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

        _grilla.Columns.Add(columna);
    }

    private Panel ConstruirPanelAcciones()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = AyudasUi.Paleta.Fondo };

        _btnAbrir.Text = "Abrir reserva";
        _btnAbrir.Location = new Point(0, 12);
        _btnAbrir.Size = new Size(140, 36);
        _btnAbrir.EstiloPrimario();
        _btnAbrir.Enabled = false;
        _btnAbrir.Click += (_, _) => AbrirSeleccionada();
        panel.Controls.Add(_btnAbrir);

        _btnReenviarCorreo.Text = "Reenviar correo";
        _btnReenviarCorreo.Location = new Point(150, 12);
        _btnReenviarCorreo.Size = new Size(150, 36);
        _btnReenviarCorreo.EstiloSecundario();
        _btnReenviarCorreo.Enabled = false;
        _btnReenviarCorreo.Click += async (_, _) => await ReenviarCorreoAsync();
        panel.Controls.Add(_btnReenviarCorreo);

        _btnFinalizar.Text = "Marcar finalizada";
        _btnFinalizar.Location = new Point(310, 12);
        _btnFinalizar.Size = new Size(160, 36);
        _btnFinalizar.EstiloSecundario();
        _btnFinalizar.Enabled = false;
        _btnFinalizar.Click += async (_, _) => await FinalizarAsync();
        panel.Controls.Add(_btnFinalizar);

        _btnVerAuditoria.Text = "Historial de estados";
        _btnVerAuditoria.Location = new Point(480, 12);
        _btnVerAuditoria.Size = new Size(170, 36);
        _btnVerAuditoria.EstiloSecundario();
        _btnVerAuditoria.Enabled = false;
        _btnVerAuditoria.Click += async (_, _) => await VerAuditoriaAsync();
        panel.Controls.Add(_btnVerAuditoria);

        _btnCargarMas.Text = "Cargar mas";
        _btnCargarMas.Location = new Point(660, 12);
        _btnCargarMas.Size = new Size(140, 36);
        _btnCargarMas.EstiloSecundario();
        _btnCargarMas.Visible = false;
        _btnCargarMas.Click += async (_, _) => await BuscarAsync(reiniciar: false);
        panel.Controls.Add(_btnCargarMas);

        _lblResumen.Location = new Point(816, 22);
        _lblResumen.Size = new Size(400, 20);
        _lblResumen.ForeColor = AyudasUi.Paleta.TextoSuave;
        panel.Controls.Add(_lblResumen);

        return panel;
    }

    private static Label Etiqueta(string texto, int x, int y) => new()
    {
        Text = texto,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = AyudasUi.Paleta.TextoSuave,
        Font = new Font("Segoe UI", 8.5F)
    };

    // =====================================================================
    // CARGA Y BUSQUEDA
    // =====================================================================

    private async void FrmReservasConsultaShown(object? sender, EventArgs e)
    {
        _cancelacion = new CancellationTokenSource();

        var salones = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.ConsultarSalonesAsync(null, false, _cancelacion.Token),
            "No se pudieron cargar los salones.");

        var listaSalones = new List<Salon> { new() { IdSalon = 0, Nombre = "(todos)" } };

        if (salones is not null)
        {
            listaSalones.AddRange(salones);
        }

        _cboSalon.DataSource = listaSalones;
        _cboSalon.DisplayMember = nameof(Salon.Nombre);
        _cboSalon.ValueMember = nameof(Salon.IdSalon);

        await BuscarAsync(reiniciar: true);
    }

    private async void TeclaBuscar(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        await BuscarAsync(reiniciar: true);
    }

    private FiltroConsultaReserva ConstruirFiltro(int pagina)
    {
        EstadoReserva? estado = _cboEstado.SelectedIndex > 0
            ? MaquinaEstadosReserva.Desde(_cboEstado.SelectedItem!.ToString()!)
            : null;

        var idSalon = _cboSalon.SelectedValue is int valor && valor > 0 ? valor : (int?)null;

        return new FiltroConsultaReserva
        {
            Codigo = string.IsNullOrWhiteSpace(_txtCodigo.Text) ? null : _txtCodigo.Text.Trim(),
            TextoCliente = string.IsNullOrWhiteSpace(_txtCliente.Text) ? null : _txtCliente.Text.Trim(),
            IdSalon = idSalon,
            Estado = estado,
            FechaDesde = _chkUsarFechas.Checked ? DateOnly.FromDateTime(_dtpDesde.Value) : null,
            FechaHasta = _chkUsarFechas.Checked ? DateOnly.FromDateTime(_dtpHasta.Value) : null,
            Pagina = pagina,
            TamanoPagina = TamanoPagina
        };
    }

    /// <summary>
    /// Ejecuta la consulta.
    ///
    /// Cada llamada CANCELA la anterior: si el usuario pulsa Buscar tres veces
    /// seguidas, las dos primeras se abandonan y solo la ultima pinta datos.
    /// Sin esto, una respuesta lenta podria sobrescribir a una posterior.
    /// </summary>
    private async Task BuscarAsync(bool reiniciar)
    {
        // Se cancela la busqueda anterior y se crea un token nuevo.
        AyudasUi.Liberar(ref _cancelacion);
        _cancelacion = new CancellationTokenSource();

        if (reiniciar)
        {
            _paginaActual = 1;
            _filas.Clear();
        }
        else
        {
            _paginaActual++;
        }

        _btnBuscar.Enabled = false;
        _btnCargarMas.Enabled = false;
        _lblResumen.Text = "Consultando...";

        var pagina = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ConsultarAsync(ConstruirFiltro(_paginaActual), _cancelacion.Token),
            "No se pudieron consultar las reservas.");

        _btnBuscar.Enabled = true;

        if (pagina is null)
        {
            _lblResumen.Text = string.Empty;
            return;
        }

        _totalFilas = pagina.TotalFilas;
        _filas.AddRange(pagina.Filas);

        _grilla.DataSource = null;
        _grilla.DataSource = _filas.ToList();

        _lblResumen.Text = _totalFilas == 0
            ? "No se encontraron reservas con esos filtros."
            : $"Mostrando {_filas.Count} de {_totalFilas} reserva(s).";

        _btnCargarMas.Visible = _filas.Count < _totalFilas;
        _btnCargarMas.Enabled = true;

        ActualizarBotonesSegunSeleccion();
    }

    private async Task LimpiarFiltrosAsync()
    {
        _txtCodigo.Clear();
        _txtCliente.Clear();
        _cboEstado.SelectedIndex = 0;
        _chkUsarFechas.Checked = false;

        if (_cboSalon.Items.Count > 0)
        {
            _cboSalon.SelectedIndex = 0;
        }

        await BuscarAsync(reiniciar: true);
    }

    // =====================================================================
    // PRESENTACION
    // =====================================================================

    private void GrillaCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grilla.Rows.Count)
        {
            return;
        }

        if (_grilla.Rows[e.RowIndex].DataBoundItem is not ResumenReserva fila)
        {
            return;
        }

        e.CellStyle!.BackColor = AyudasUi.ColorFondoEstado(fila.Estado);
        e.CellStyle.ForeColor = AyudasUi.ColorTextoEstado(fila.Estado);

        if (_grilla.Columns[e.ColumnIndex].Name == "Estado")
        {
            e.CellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            e.Value = MaquinaEstadosReserva.ATexto(fila.Estado);
            e.FormattingApplied = true;
        }
    }

    private ResumenReserva? FilaSeleccionada =>
        _grilla.CurrentRow?.DataBoundItem as ResumenReserva;

    private void ActualizarBotonesSegunSeleccion()
    {
        var fila = FilaSeleccionada;
        var hay = fila is not null;

        _btnAbrir.Enabled = hay;
        _btnVerAuditoria.Enabled = hay;

        // Solo tiene sentido reenviar un correo de una reserva confirmada o
        // cancelada: son los dos eventos que generan notificacion.
        _btnReenviarCorreo.Enabled = hay
            && fila!.Estado is EstadoReserva.Confirmada or EstadoReserva.Cancelada;

        _btnFinalizar.Enabled = hay
            && fila!.Estado == EstadoReserva.Confirmada
            && _sesion.Tiene(Permiso.GestionarReservas);
    }

    private void AbrirSeleccionada()
    {
        var fila = FilaSeleccionada;

        if (fila is null)
        {
            AyudasUi.MostrarAviso("Seleccione una reserva.");
            return;
        }

        if (MdiParent is FrmPrincipal principal)
        {
            principal.AbrirReserva(fila.IdReserva);
        }
    }

    // =====================================================================
    // ACCIONES
    // =====================================================================

    /// <summary>
    /// Reenvia el correo de la reserva seleccionada.
    ///
    /// ESTE ES EL BOTON DEL CASO CA-07. No toca el estado de la reserva: solo
    /// vuelve a intentar el envio. Cada intento queda auditado con su propio
    /// numero, asi que despues de una falla y un reintento se ven dos filas en
    /// com.CorreoEnviado y una sola transicion en evt.ReservaAuditoria.
    /// </summary>
    private async Task ReenviarCorreoAsync()
    {
        if (_cancelacion is null) { return; }

        var fila = FilaSeleccionada;

        if (fila is null) { return; }

        var tipo = fila.Estado == EstadoReserva.Cancelada
            ? TipoEventoCorreo.Cancelacion
            : TipoEventoCorreo.Confirmacion;

        if (!AyudasUi.Confirmar(
            $"Se reenviara el correo de {TextosEnumeracion.ATexto(tipo).ToLowerInvariant()} "
            + $"de la reserva {fila.Codigo} a {fila.ClienteEmail}."
            + Environment.NewLine + Environment.NewLine
            + "El estado de la reserva NO se modificara. Continuar?"))
        {
            return;
        }

        _btnReenviarCorreo.Enabled = false;

        var resultado = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ReenviarCorreoAsync(fila.IdReserva, tipo, _cancelacion.Token),
            "No se pudo reenviar el correo.");

        _btnReenviarCorreo.Enabled = true;

        if (resultado is null) { return; }

        if (resultado.Exitoso)
        {
            AyudasUi.MostrarInformacion(
                resultado.MensajeUsuario + Environment.NewLine + Environment.NewLine
                + "El intento quedo registrado en la auditoria de integraciones.");
        }
        else
        {
            AyudasUi.MostrarAviso(
                resultado.MensajeUsuario + Environment.NewLine + Environment.NewLine
                + "El intento fallido tambien quedo registrado en la auditoria.");
        }
    }

    private async Task FinalizarAsync()
    {
        if (_cancelacion is null) { return; }

        var fila = FilaSeleccionada;

        if (fila is null) { return; }

        if (!AyudasUi.Confirmar(
            $"Se marcara la reserva {fila.Codigo} como FINALIZADA."
            + Environment.NewLine + Environment.NewLine
            + "FINALIZADA es un estado terminal: despues no admite ningun cambio. Continuar?"))
        {
            return;
        }

        var resultado = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.FinalizarAsync(fila.IdReserva, _cancelacion.Token),
            "No se pudo finalizar la reserva.");

        if (resultado is null) { return; }

        AyudasUi.MostrarInformacion(resultado.Mensaje);
        await BuscarAsync(reiniciar: true);
    }

    /// <summary>Muestra el historial de cambios de estado de la reserva seleccionada.</summary>
    private async Task VerAuditoriaAsync()
    {
        if (_cancelacion is null) { return; }

        var fila = FilaSeleccionada;

        if (fila is null) { return; }

        var historial = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ConsultarCambiosEstadoAsync(fila.IdReserva, _cancelacion.Token),
            "No se pudo consultar el historial de la reserva.");

        if (historial is null) { return; }

        if (historial.Count == 0)
        {
            AyudasUi.MostrarInformacion(
                $"La reserva {fila.Codigo} no ha cambiado de estado desde que se creo.");
            return;
        }

        var texto = string.Join(Environment.NewLine + Environment.NewLine,
            historial.Select(h =>
                $"{h.Fecha.ToString("dd/MM/yyyy HH:mm", Cultura)}  ·  {h.Usuario}"
                + Environment.NewLine
                + $"{MaquinaEstadosReserva.ATexto(h.EstadoAnterior)} → {MaquinaEstadosReserva.ATexto(h.EstadoNuevo)}"
                + (string.IsNullOrWhiteSpace(h.Motivo) ? string.Empty
                    : Environment.NewLine + "Motivo: " + h.Motivo)));

        AyudasUi.MostrarInformacion(
            $"Historial de estados de {fila.Codigo}:" + Environment.NewLine + Environment.NewLine + texto);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AyudasUi.Liberar(ref _cancelacion);
        }

        base.Dispose(disposing);
    }
}
