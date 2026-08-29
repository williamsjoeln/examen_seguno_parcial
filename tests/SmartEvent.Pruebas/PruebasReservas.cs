using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Excepciones;
using Xunit;

namespace SmartEvent.Pruebas;

/// <summary>
/// Casos de aceptacion CA-01 a CA-05 ejecutados a traves de la capa de datos
/// real, contra SQL Server.
///
/// Complementan a database/99_pruebas_CA.sql: aquel script demuestra que las
/// reglas viven en el motor; estas pruebas demuestran ademas que la aplicacion
/// las invoca correctamente, incluido el envio del detalle como parametro tipo
/// tabla (TVP).
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public sealed class PruebasReservas
{
    private readonly ContextoPruebas _ctx;

    public PruebasReservas(ContextoPruebas contexto) => _ctx = contexto;

    // Identificadores de la semilla creada por database/00_SmartEventAI.sql.
    private const string IdentificacionCliente = "1712345678";
    private const string SalonGrande = "Salon Quito";
    private const string SalonPequeno = "Salon Cuenca";
    private const string RecursoProyector = "Proyector 4K";
    private const string RecursoSillas = "Silla ejecutiva";
    private const string RecursoCatering = "Servicio de catering";
    private const string RecursoInactivo = "Kit de senaletica";
    private const string RecursoStockBajo = "Pantalla LED 120 pulgadas";

    /// <summary>
    /// Fecha de trabajo distinta por prueba, para que las pruebas no compitan
    /// entre si por el mismo salon y horario.
    /// </summary>
    private static DateOnly FechaDePrueba(int desplazamientoDias) =>
        DateOnly.FromDateTime(DateTime.Today).AddDays(400 + desplazamientoDias);

    private async Task<(int IdUsuarioCoord, int IdUsuarioAdmin, int IdCliente, int IdSalonGrande,
                        int IdSalonPequeno, int IdProyector, int IdSillas, int IdCatering,
                        int IdInactivo, int IdStockBajo)>
        ObtenerIdentificadoresAsync(CancellationToken ct)
    {
        // El usuario coordinador se obtiene autenticandose: de paso se prueba
        // el flujo completo de autenticacion en dos fases.
        var coord = await AutenticarAsync("coordinador", "Coord#2026", ct);
        var admin = await AutenticarAsync("admin", "Admin#2026", ct);

        var clientes = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionCliente, true, ct);
        var salones = await _ctx.Catalogos.ConsultarSalonesAsync(null, true, ct);
        var recursos = await _ctx.Catalogos.ConsultarRecursosAsync(null, false, ct);

        return (
            coord, admin,
            clientes.Single(c => c.Identificacion == IdentificacionCliente).IdCliente,
            salones.Single(s => s.Nombre == SalonGrande).IdSalon,
            salones.Single(s => s.Nombre == SalonPequeno).IdSalon,
            recursos.Single(r => r.Nombre == RecursoProyector).IdRecurso,
            recursos.Single(r => r.Nombre == RecursoSillas).IdRecurso,
            recursos.Single(r => r.Nombre == RecursoCatering).IdRecurso,
            recursos.Single(r => r.Nombre == RecursoInactivo).IdRecurso,
            recursos.Single(r => r.Nombre == RecursoStockBajo).IdRecurso);
    }

    /// <summary>
    /// Autentica en dos fases, tal como lo hace FrmLogin: primero se piden los
    /// parametros de derivacion, luego se envia el hash candidato.
    /// </summary>
    private async Task<int> AutenticarAsync(string usuario, string contrasena, CancellationToken ct)
    {
        var parametros = await _ctx.Usuarios.ObtenerParametrosDerivacionAsync(usuario, ct);

        var hash = Dominio.Seguridad.HashContrasena.DerivarConParametros(
            contrasena, parametros.SaltBase64, parametros.Iteraciones);

        var resultado = await _ctx.Usuarios.AutenticarAsync(usuario, hash, "PRUEBAS", ct);

        Assert.True(resultado.Autenticado, $"No se pudo autenticar al usuario semilla '{usuario}': {resultado.Mensaje}");
        Assert.NotNull(resultado.Usuario);

        return resultado.Usuario!.IdUsuario;
    }

    // =====================================================================
    // Autenticacion
    // =====================================================================

    [Fact]
    public async Task Autenticacion_UsuarioSemillaAdmin_DevuelveRolAdministrador()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var parametros = await _ctx.Usuarios.ObtenerParametrosDerivacionAsync("admin", ct);
        var hash = Dominio.Seguridad.HashContrasena.DerivarConParametros(
            "Admin#2026", parametros.SaltBase64, parametros.Iteraciones);

        var resultado = await _ctx.Usuarios.AutenticarAsync("admin", hash, "PRUEBAS", ct);

        Assert.True(resultado.Autenticado);
        Assert.Equal(RolUsuario.Administrador, resultado.Usuario!.Rol);
        Assert.True(resultado.Usuario.Tiene(Permiso.GestionarCatalogos));
        Assert.True(resultado.Usuario.Tiene(Permiso.AplicarDescuentoAlto));
    }

    [Fact]
    public async Task Autenticacion_ContrasenaIncorrecta_RechazaSinRevelarSiExisteElUsuario()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var parametros = await _ctx.Usuarios.ObtenerParametrosDerivacionAsync("admin", ct);
        var hashMalo = Dominio.Seguridad.HashContrasena.DerivarConParametros(
            "contrasena-que-no-es", parametros.SaltBase64, parametros.Iteraciones);

        var resultado = await _ctx.Usuarios.AutenticarAsync("admin", hashMalo, "PRUEBAS", ct);

        Assert.False(resultado.Autenticado);
        Assert.Null(resultado.Usuario);

        // El mensaje es identico al de un usuario inexistente: no filtra si la
        // cuenta existe (proteccion contra enumeracion de usuarios).
        Assert.Equal("Usuario o contrasena incorrectos.", resultado.Mensaje);
    }

    [Fact]
    public async Task Autenticacion_UsuarioInexistente_DevuelveSaltSenueloYMismoMensaje()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var parametros = await _ctx.Usuarios.ObtenerParametrosDerivacionAsync("no-existe-este-usuario", ct);

        // Aunque el usuario no exista, se devuelve un salt valido para que la
        // aplicacion haga exactamente el mismo trabajo criptografico.
        Assert.False(string.IsNullOrWhiteSpace(parametros.SaltBase64));
        Assert.True(parametros.Iteraciones > 0);

        var hash = Dominio.Seguridad.HashContrasena.DerivarConParametros(
            "cualquiera", parametros.SaltBase64, parametros.Iteraciones);

        var resultado = await _ctx.Usuarios.AutenticarAsync("no-existe-este-usuario", hash, "PRUEBAS", ct);

        Assert.False(resultado.Autenticado);
        Assert.Equal("Usuario o contrasena incorrectos.", resultado.Mensaje);
    }

    // =====================================================================
    // CA-01  Guardar una reserva valida con tres detalles y recuperarla igual
    // =====================================================================

    [Fact]
    public async Task CA01_GuardarReservaConTresDetalles_RecuperaCabeceraYLosTresDetalles()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);
        var fecha = FechaDePrueba(1);

        var solicitud = new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(13, 0, 0),
            NumeroInvitados = 80,
            Observacion = "Prueba automatizada CA-01",
            PorcentajeDescuentoGlobal = 0m,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles =
            [
                new LineaDetalleSolicitud(ids.IdProyector,  2, 45.00m,  0m),
                new LineaDetalleSolicitud(ids.IdSillas,    80,  3.50m,  5m),
                new LineaDetalleSolicitud(ids.IdCatering,  80,  9.75m, 10m)
            ]
        };

        var guardado = await _ctx.Reservas.GuardarAsync(solicitud, ct);

        Assert.True(guardado.IdReserva > 0);
        Assert.StartsWith("RSV-", guardado.Codigo, StringComparison.Ordinal);

        var recuperada = await _ctx.Reservas.ObtenerPorIdAsync(guardado.IdReserva, ct);

        Assert.NotNull(recuperada);
        Assert.Equal(guardado.Codigo, recuperada!.Codigo);
        Assert.Equal(EstadoReserva.Borrador, recuperada.Estado);
        Assert.Equal(fecha, recuperada.FechaEvento);
        Assert.Equal(new TimeSpan(9, 0, 0), recuperada.HoraInicio);
        Assert.Equal(new TimeSpan(13, 0, 0), recuperada.HoraFin);
        Assert.Equal(80, recuperada.NumeroInvitados);

        // Los TRES detalles, con sus cantidades y descuentos exactos.
        Assert.Equal(3, recuperada.Detalles.Count);
        Assert.Contains(recuperada.Detalles, d => d.IdRecurso == ids.IdProyector && d.Cantidad == 2);
        Assert.Contains(recuperada.Detalles, d => d.IdRecurso == ids.IdSillas && d.Cantidad == 80 && d.PorcentajeDescuento == 5m);
        Assert.Contains(recuperada.Detalles, d => d.IdRecurso == ids.IdCatering && d.Cantidad == 80 && d.PorcentajeDescuento == 10m);

        // Totales calculados por SQL Server:
        //   450.00 tarifa + 90.00 + 266.00 + 702.00 = 1508.00
        //   impuesto 15% = 226.20   total = 1734.20
        Assert.Equal(1508.00m, recuperada.Subtotal);
        Assert.Equal(226.20m, recuperada.Impuesto);
        Assert.Equal(1734.20m, recuperada.Total);

        // Y la calculadora del dominio llega al MISMO numero: la interfaz y la
        // base de datos no discrepan (regla D14 y D15).
        var totalesUi = recuperada.RecalcularTotales();
        Assert.Equal(recuperada.Total, totalesUi.Total);
    }

    // =====================================================================
    // CA-02  Rollback completo cuando falla un detalle
    // =====================================================================

    [Fact]
    public async Task CA02_DetalleInvalido_NoDejaCabeceraNiDetallesParciales()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);
        var fecha = FechaDePrueba(2);

        var solicitud = new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonPequeno,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(15, 0, 0),
            HoraFin = new TimeSpan(18, 0, 0),
            NumeroInvitados = 30,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles =
            [
                // Las dos primeras lineas son perfectamente validas...
                new LineaDetalleSolicitud(ids.IdProyector, 1, 45.00m, 0m),
                new LineaDetalleSolicitud(ids.IdSillas,   20,  3.50m, 0m),
                // ...y la tercera apunta a un recurso INACTIVO.
                new LineaDetalleSolicitud(ids.IdInactivo,  2, 25.00m, 0m)
            ]
        };

        var excepcion = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.GuardarAsync(solicitud, ct));

        Assert.Equal(50013, excepcion.NumeroSql);

        // No quedo NINGUNA reserva para ese salon y fecha: ni cabecera huerfana
        // ni detalles parciales.
        var consulta = await _ctx.Reservas.ConsultarAsync(new FiltroConsultaReserva
        {
            IdSalon = ids.IdSalonPequeno,
            FechaDesde = fecha,
            FechaHasta = fecha
        }, ct);

        Assert.Empty(consulta.Filas);
    }

    // =====================================================================
    // CA-03  Rechazo por cruce parcial de horario
    // =====================================================================

    [Fact]
    public async Task CA03_CrucePracialDeHorario_SeRechaza_YFranjaAdyacenteSeAcepta()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);
        var fecha = FechaDePrueba(3);

        LineaDetalleSolicitud[] detalleMinimo = [new(ids.IdProyector, 1, 45.00m, 0m)];

        // Reserva base: 09:00 - 13:00
        await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(13, 0, 0),
            NumeroInvitados = 50,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles = detalleMinimo
        }, ct);

        // Cruce parcial 12:00 - 15:00 : se solapa entre 12:00 y 13:00
        var excepcion = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
            {
                IdCliente = ids.IdCliente,
                IdSalon = ids.IdSalonGrande,
                FechaEvento = fecha,
                HoraInicio = new TimeSpan(12, 0, 0),
                HoraFin = new TimeSpan(15, 0, 0),
                NumeroInvitados = 50,
                IdUsuario = ids.IdUsuarioCoord,
                Detalles = detalleMinimo
            }, ct));

        Assert.Equal(50017, excepcion.NumeroSql);

        // Franja ADYACENTE 13:00 - 15:00 : no hay cruce, porque la formula usa
        // comparaciones estrictas (13:00 < 13:00 es falso).
        var adyacente = await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(13, 0, 0),
            HoraFin = new TimeSpan(15, 0, 0),
            NumeroInvitados = 50,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles = detalleMinimo
        }, ct);

        Assert.True(adyacente.IdReserva > 0);
    }

    // =====================================================================
    // CA-04  Editar un BORRADOR sin que se detecte a si mismo como conflicto
    // =====================================================================

    [Fact]
    public async Task CA04_EditarBorrador_NoSeDetectaASiMismoComoConflicto()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);
        var fecha = FechaDePrueba(4);

        LineaDetalleSolicitud[] detalle = [new(ids.IdProyector, 1, 45.00m, 0m)];

        var original = await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(13, 0, 0),
            NumeroInvitados = 50,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles = detalle
        }, ct);

        // Se reedita con EXACTAMENTE el mismo salon, fecha y horario.
        var editada = await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdReserva = original.IdReserva,
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(13, 0, 0),
            NumeroInvitados = 95,
            Observacion = "Editada en CA-04",
            IdUsuario = ids.IdUsuarioCoord,
            Detalles = [new LineaDetalleSolicitud(ids.IdProyector, 3, 45.00m, 0m)]
        }, ct);

        Assert.Equal(original.IdReserva, editada.IdReserva);
        Assert.Equal(original.Codigo, editada.Codigo);

        var recuperada = await _ctx.Reservas.ObtenerPorIdAsync(original.IdReserva, ct);
        Assert.Equal(95, recuperada!.NumeroInvitados);
        Assert.Equal(3, recuperada.Detalles.Single().Cantidad);

        // Y la validacion de disponibilidad tampoco la marca como conflicto.
        var conflictos = await _ctx.Reservas.ValidarDisponibilidadAsync(
            original.IdReserva, ids.IdSalonGrande, fecha,
            new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0), 95,
            [new LineaDetalleSolicitud(ids.IdProyector, 3, 45.00m, 0m)], ct);

        Assert.DoesNotContain(conflictos, c => c.Codigo == "CRUCE_HORARIO");
    }

    // =====================================================================
    // CA-05  Capacidad y stock rechazados desde SQL
    // =====================================================================

    [Fact]
    public async Task CA05_ExcederCapacidadDelSalon_SeRechazaDesdeSql()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);

        var excepcion = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
            {
                IdCliente = ids.IdCliente,
                IdSalon = ids.IdSalonPequeno,   // capacidad 40
                FechaEvento = FechaDePrueba(5),
                HoraInicio = new TimeSpan(8, 0, 0),
                HoraFin = new TimeSpan(11, 0, 0),
                NumeroInvitados = 100,          // muy por encima
                IdUsuario = ids.IdUsuarioCoord,
                Detalles = [new LineaDetalleSolicitud(ids.IdProyector, 1, 45.00m, 0m)]
            }, ct));

        Assert.Equal(50016, excepcion.NumeroSql);
    }

    [Fact]
    public async Task CA05_ExcederStockDelRecurso_SeRechazaDesdeSql()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);

        var excepcion = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
            {
                IdCliente = ids.IdCliente,
                IdSalon = ids.IdSalonPequeno,
                FechaEvento = FechaDePrueba(6),
                HoraInicio = new TimeSpan(8, 0, 0),
                HoraFin = new TimeSpan(11, 0, 0),
                NumeroInvitados = 30,
                IdUsuario = ids.IdUsuarioCoord,
                // La pantalla LED tiene stock 4 y se piden 5.
                Detalles = [new LineaDetalleSolicitud(ids.IdStockBajo, 5, 120.00m, 0m)]
            }, ct));

        Assert.Equal(50018, excepcion.NumeroSql);
    }

    // =====================================================================
    // Reglas de autorizacion de descuentos (D13)
    // =====================================================================

    [Fact]
    public async Task D13_CoordinadorNoPuedeSuperarElDiezPorCiento_PeroElAdministradorSi()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);
        var fecha = FechaDePrueba(7);

        SolicitudGuardarReserva Construir(int idUsuario) => new()
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonPequeno,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(19, 0, 0),
            HoraFin = new TimeSpan(22, 0, 0),
            NumeroInvitados = 20,
            IdUsuario = idUsuario,
            Detalles = [new LineaDetalleSolicitud(ids.IdProyector, 1, 45.00m, 15m)]
        };

        var rechazo = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.GuardarAsync(Construir(ids.IdUsuarioCoord), ct));

        Assert.Equal(50019, rechazo.NumeroSql);

        var aceptada = await _ctx.Reservas.GuardarAsync(Construir(ids.IdUsuarioAdmin), ct);
        Assert.True(aceptada.IdReserva > 0);
    }

    // =====================================================================
    // Maquina de estados e idempotencia (base de CA-06 y CA-07)
    // =====================================================================

    [Fact]
    public async Task Estados_ConfirmarDosVeces_NoDuplicaElCambioNiLaAuditoria()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);
        var fecha = FechaDePrueba(8);

        var reserva = await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = fecha,
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(12, 0, 0),
            NumeroInvitados = 40,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles = [new LineaDetalleSolicitud(ids.IdProyector, 1, 45.00m, 0m)]
        }, ct);

        // Sin analisis de IA la confirmacion se rechaza (regla D22).
        var sinIa = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.CambiarEstadoAsync(
                reserva.IdReserva, EstadoReserva.Confirmada, null, ids.IdUsuarioCoord, ct));

        Assert.Equal(50022, sinIa.NumeroSql);

        // Se registra una contingencia manual justificada y ahora si confirma.
        await _ctx.Auditoria.RegistrarAnalisisIaAsync(new RegistroAnalisisIa
        {
            IdReserva = reserva.IdReserva,
            Proveedor = "CONTINGENCIA",
            Modelo = "N/A",
            PromptVersion = "v1",
            Exitoso = false,
            Error = "Servicio de IA no disponible durante la prueba automatizada.",
            EsContingenciaManual = true,
            JustificacionContingencia =
                "El servicio de analisis no respondio y el evento requiere confirmacion inmediata.",
            IdUsuario = ids.IdUsuarioAdmin
        }, ct);

        var primera = await _ctx.Reservas.CambiarEstadoAsync(
            reserva.IdReserva, EstadoReserva.Confirmada, null, ids.IdUsuarioCoord, ct);

        Assert.True(primera.HuboCambio);

        // Segunda confirmacion: idempotente, no cambia nada.
        var segunda = await _ctx.Reservas.CambiarEstadoAsync(
            reserva.IdReserva, EstadoReserva.Confirmada, null, ids.IdUsuarioCoord, ct);

        Assert.True(segunda.SinCambio);

        // La auditoria tiene UNA sola transicion a CONFIRMADA.
        var auditoria = await _ctx.Auditoria.ConsultarCambiosEstadoAsync(reserva.IdReserva, ct);
        Assert.Single(auditoria, a => a.EstadoNuevo == EstadoReserva.Confirmada);

        // Y una reserva CONFIRMADA ya no se puede editar (regla D19).
        var noEditable = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
            {
                IdReserva = reserva.IdReserva,
                IdCliente = ids.IdCliente,
                IdSalon = ids.IdSalonGrande,
                FechaEvento = fecha,
                HoraInicio = new TimeSpan(9, 0, 0),
                HoraFin = new TimeSpan(12, 0, 0),
                NumeroInvitados = 41,
                IdUsuario = ids.IdUsuarioCoord,
                Detalles = [new LineaDetalleSolicitud(ids.IdProyector, 1, 45.00m, 0m)]
            }, ct));

        Assert.Equal(50011, noEditable.NumeroSql);
    }

    [Fact]
    public async Task Estados_CancelarConMotivoCorto_SeRechaza()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;
        var ids = await ObtenerIdentificadoresAsync(ct);

        var reserva = await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdCliente = ids.IdCliente,
            IdSalon = ids.IdSalonGrande,
            FechaEvento = FechaDePrueba(9),
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(12, 0, 0),
            NumeroInvitados = 40,
            IdUsuario = ids.IdUsuarioCoord,
            Detalles = [new LineaDetalleSolicitud(ids.IdProyector, 1, 45.00m, 0m)]
        }, ct);

        var corto = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.CambiarEstadoAsync(
                reserva.IdReserva, EstadoReserva.Cancelada, "ya no va", ids.IdUsuarioAdmin, ct));

        Assert.Equal(50021, corto.NumeroSql);

        // Con un motivo de al menos 20 caracteres si se cancela.
        var ok = await _ctx.Reservas.CambiarEstadoAsync(
            reserva.IdReserva, EstadoReserva.Cancelada,
            "El cliente desistio del evento por recorte de presupuesto anual.",
            ids.IdUsuarioAdmin, ct);

        Assert.True(ok.HuboCambio);

        // CANCELADA es terminal: no admite mas cambios.
        var terminal = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Reservas.CambiarEstadoAsync(
                reserva.IdReserva, EstadoReserva.Finalizada, null, ids.IdUsuarioAdmin, ct));

        Assert.Equal(50020, terminal.NumeroSql);
    }
}
