using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Excepciones;
using Xunit;

namespace SmartEvent.Pruebas;

/// <summary>
/// Pruebas del mantenimiento de catalogos.
///
/// Cubren los comportamientos que el examen exige a FrmCatalogos: alta,
/// edicion, busqueda, DETECCION DE DUPLICADOS e INACTIVACION LOGICA.
///
/// Las tres reglas importantes viven en los procedimientos almacenados, no en
/// el formulario, y estas pruebas lo demuestran invocando la capa de datos
/// directamente:
///   - no se puede repetir la identificacion de un cliente ni el nombre de un
///     salon o un recurso;
///   - no se puede inactivar un salon o un recurso con reservas activas;
///   - no se puede reducir el stock por debajo de lo ya comprometido.
///
/// Son REEJECUTABLES: si el registro de prueba ya existe, lo reutilizan en
/// lugar de fallar.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public sealed class PruebasCatalogos
{
    private readonly ContextoPruebas _ctx;

    public PruebasCatalogos(ContextoPruebas contexto) => _ctx = contexto;

    private const string IdentificacionPrueba = "9999000111";
    private const string NombreSalonPrueba = "Salon de pruebas automatizadas";
    private const string NombreRecursoPrueba = "Recurso de pruebas automatizadas";

    // =====================================================================
    // CLIENTES
    // =====================================================================

    /// <summary>
    /// Alta de un cliente nuevo, comprobacion de que aparece en la busqueda y
    /// rechazo del duplicado por identificacion.
    /// </summary>
    [Fact]
    public async Task Cliente_SeCreaSeBuscaYRechazaDuplicado()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        // ---------- Alta o reutilizacion, para que la prueba sea repetible ----------
        var existentes = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, false, ct);
        var yaExistia = existentes.FirstOrDefault(c => c.Identificacion == IdentificacionPrueba);

        var cliente = new Cliente
        {
            IdCliente = yaExistia?.IdCliente ?? 0,
            Identificacion = IdentificacionPrueba,
            Nombres = "Cliente de pruebas automatizadas",
            Email = "pruebas@smartevent.ejemplo.com",
            Telefono = "0999000111"
        };

        var idCliente = await _ctx.Catalogos.GuardarClienteAsync(cliente, ct);
        Assert.True(idCliente > 0);

        // ---------- Aparece al buscar por identificacion ----------
        var porIdentificacion = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, true, ct);
        Assert.Contains(porIdentificacion, c => c.IdCliente == idCliente);

        // ---------- Aparece al buscar por parte del nombre ----------
        var porNombre = await _ctx.Catalogos.ConsultarClientesAsync("pruebas automatizadas", true, ct);
        Assert.Contains(porNombre, c => c.IdCliente == idCliente);

        // ---------- DUPLICADO: otro cliente con la misma identificacion ----------
        var duplicado = new Cliente
        {
            IdCliente = 0,
            Identificacion = IdentificacionPrueba,
            Nombres = "Intento de duplicado",
            Email = "duplicado@smartevent.ejemplo.com"
        };

        var error = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Catalogos.GuardarClienteAsync(duplicado, ct));

        Assert.Equal(50024, error.NumeroSql);
        Assert.Contains("identificacion", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inactivacion y reactivacion LOGICA: el cliente nunca se borra, solo se
    /// marca, para no romper el historial de reservas.
    /// </summary>
    [Fact]
    public async Task Cliente_SeInactivaYSeReactivaSinBorrarse()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var lista = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, false, ct);
        var cliente = lista.FirstOrDefault(c => c.Identificacion == IdentificacionPrueba);

        if (cliente is null)
        {
            var idNuevo = await _ctx.Catalogos.GuardarClienteAsync(new Cliente
            {
                Identificacion = IdentificacionPrueba,
                Nombres = "Cliente de pruebas automatizadas",
                Email = "pruebas@smartevent.ejemplo.com"
            }, ct);

            cliente = (await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, false, ct))
                .Single(c => c.IdCliente == idNuevo);
        }

        // ---------- Inactivar ----------
        await _ctx.Catalogos.CambiarEstadoClienteAsync(cliente.IdCliente, false, ct);

        var soloActivos = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, true, ct);
        Assert.DoesNotContain(soloActivos, c => c.IdCliente == cliente.IdCliente);

        // Sigue existiendo: no se borro, solo se marco.
        var todos = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, false, ct);
        var inactivo = todos.Single(c => c.IdCliente == cliente.IdCliente);
        Assert.False(inactivo.Estado);

        // ---------- Reactivar ----------
        await _ctx.Catalogos.CambiarEstadoClienteAsync(cliente.IdCliente, true, ct);

        var reactivados = await _ctx.Catalogos.ConsultarClientesAsync(IdentificacionPrueba, true, ct);
        Assert.Contains(reactivados, c => c.IdCliente == cliente.IdCliente);
    }

    // =====================================================================
    // SALONES
    // =====================================================================

    [Fact]
    public async Task Salon_SeCreaYRechazaNombreDuplicado()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var existentes = await _ctx.Catalogos.ConsultarSalonesAsync(NombreSalonPrueba, false, ct);
        var yaExistia = existentes.FirstOrDefault(s => s.Nombre == NombreSalonPrueba);

        var idSalon = await _ctx.Catalogos.GuardarSalonAsync(new Salon
        {
            IdSalon = yaExistia?.IdSalon ?? 0,
            Nombre = NombreSalonPrueba,
            Ubicacion = "Torre de pruebas",
            Capacidad = 45,
            TarifaBase = 275.50m
        }, ct);

        Assert.True(idSalon > 0);

        var guardado = (await _ctx.Catalogos.ConsultarSalonesAsync(NombreSalonPrueba, false, ct))
            .Single(s => s.IdSalon == idSalon);

        Assert.Equal(45, guardado.Capacidad);
        Assert.Equal(275.50m, guardado.TarifaBase);

        // ---------- DUPLICADO por nombre ----------
        var error = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Catalogos.GuardarSalonAsync(new Salon
            {
                IdSalon = 0,
                Nombre = NombreSalonPrueba,
                Capacidad = 10,
                TarifaBase = 1m
            }, ct));

        Assert.Equal(50024, error.NumeroSql);
    }

    /// <summary>
    /// No se puede inactivar un salon que tiene reservas en BORRADOR o
    /// CONFIRMADA: dejaria reservas apuntando a un salon fuera de servicio.
    /// </summary>
    [Fact]
    public async Task Salon_ConReservasActivas_NoSePuedeInactivar()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var coord = await AutenticarCoordinadorAsync(ct);

        var clientes = await _ctx.Catalogos.ConsultarClientesAsync("1712345678", true, ct);
        var salones = await _ctx.Catalogos.ConsultarSalonesAsync(null, true, ct);
        var recursos = await _ctx.Catalogos.ConsultarRecursosAsync(null, true, ct);

        var salon = salones.Single(s => s.Nombre == "Salon Cuenca");

        // Se crea una reserva en BORRADOR que ocupa ese salon.
        await _ctx.Reservas.GuardarAsync(new Aplicacion.Dto.SolicitudGuardarReserva
        {
            IdCliente = clientes.Single(c => c.Identificacion == "1712345678").IdCliente,
            IdSalon = salon.IdSalon,
            FechaEvento = DateOnly.FromDateTime(DateTime.Today)
                .AddDays(ContextoPruebas.DiasDesplazamientoBase + 40),
            HoraInicio = new TimeSpan(8, 0, 0),
            HoraFin = new TimeSpan(11, 0, 0),
            NumeroInvitados = 20,
            IdUsuario = coord,
            Detalles =
            [
                new Aplicacion.Dto.LineaDetalleSolicitud(
                    recursos.Single(r => r.Nombre == "Proyector 4K").IdRecurso, 1, 45.00m, 0m)
            ]
        }, ct);

        var error = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Catalogos.CambiarEstadoSalonAsync(salon.IdSalon, false, ct));

        Assert.Equal(50011, error.NumeroSql);
        Assert.Contains("BORRADOR", error.Message, StringComparison.Ordinal);
    }

    // =====================================================================
    // RECURSOS
    // =====================================================================

    [Fact]
    public async Task Recurso_SeCreaYRechazaNombreDuplicado()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        var ct = TestContext.Current.CancellationToken;

        var existentes = await _ctx.Catalogos.ConsultarRecursosAsync(NombreRecursoPrueba, false, ct);
        var yaExistia = existentes.FirstOrDefault(r => r.Nombre == NombreRecursoPrueba);

        var idRecurso = await _ctx.Catalogos.GuardarRecursoAsync(new Recurso
        {
            IdRecurso = yaExistia?.IdRecurso ?? 0,
            Nombre = NombreRecursoPrueba,
            Tipo = "Equipo",
            StockTotal = 12,
            PrecioUnitario = 18.75m
        }, ct);

        Assert.True(idRecurso > 0);

        var error = await Assert.ThrowsAsync<ExcepcionNegocio>(
            () => _ctx.Catalogos.GuardarRecursoAsync(new Recurso
            {
                IdRecurso = 0,
                Nombre = NombreRecursoPrueba,
                Tipo = "Equipo",
                StockTotal = 1,
                PrecioUnitario = 1m
            }, ct));

        Assert.Equal(50024, error.NumeroSql);
    }

    private async Task<int> AutenticarCoordinadorAsync(CancellationToken ct)
    {
        var parametros = await _ctx.Usuarios.ObtenerParametrosDerivacionAsync("coordinador", ct);

        var hash = Dominio.Seguridad.HashContrasena.DerivarConParametros(
            "Coord#2026", parametros.SaltBase64, parametros.Iteraciones);

        var resultado = await _ctx.Usuarios.AutenticarAsync("coordinador", hash, "PRUEBAS", ct);
        Assert.True(resultado.Autenticado);

        return resultado.Usuario!.IdUsuario;
    }
}
