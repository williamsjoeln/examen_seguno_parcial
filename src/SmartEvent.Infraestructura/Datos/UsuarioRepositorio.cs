using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Infraestructura.Configuracion;

namespace SmartEvent.Infraestructura.Datos;

/// <summary>
/// Acceso a datos de seguridad. Invoca seg.sp_Usuario_Autenticar en sus dos
/// fases, de modo que el hash almacenado nunca sale de SQL Server.
/// </summary>
public sealed class UsuarioRepositorio : IUsuarioRepositorio
{
    private readonly IFabricaConexion _fabrica;
    private readonly IRegistradorSeguro _registro;
    private readonly OpcionesSeguridad _seguridad;

    public UsuarioRepositorio(
        IFabricaConexion fabrica,
        IRegistradorSeguro registro,
        IOptions<OpcionesSeguridad> seguridad)
    {
        ArgumentNullException.ThrowIfNull(seguridad);
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
        _seguridad = seguridad.Value;
    }

    /// <summary>
    /// PRIMERA FASE: pide a SQL Server el algoritmo, las iteraciones y el salt.
    /// El parametro @PasswordHashCandidato se deja en NULL, que es lo que el
    /// procedimiento interpreta como "solo quiero los parametros".
    /// </summary>
    public async Task<ParametrosDerivacion> ObtenerParametrosDerivacionAsync(
        string nombreUsuario,
        CancellationToken cancelacion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreUsuario);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("seg.sp_Usuario_Autenticar", conexion);

            comando.Agregar("@NombreUsuario", SqlDbType.VarChar, 50, nombreUsuario);
            comando.Agregar("@MaximoIntentos", SqlDbType.TinyInt, _seguridad.MaximoIntentosFallidos);
            comando.Agregar("@MinutosBloqueo", SqlDbType.Int, _seguridad.MinutosBloqueo);
            // @PasswordHashCandidato se omite a proposito: activa la fase 1.

            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            if (!await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                throw new Dominio.Excepciones.ExcepcionNegocio(
                    "No se pudo iniciar el proceso de autenticacion. Intente nuevamente.");
            }

            return new ParametrosDerivacion(
                lector.Texto("Algoritmo"),
                lector.Entero("Iteraciones"),
                lector.Texto("SaltBase64"),
                lector.Booleano("EstaBloqueado"),
                lector.Entero("SegundosBloqueo"));
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al obtener los parametros de derivacion del usuario '{nombreUsuario}'.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    /// <summary>
    /// SEGUNDA FASE: envia el hash candidato. SQL Server lo compara con el
    /// almacenado, actualiza los intentos fallidos, aplica el bloqueo temporal
    /// y devuelve los datos de autorizacion o el rechazo.
    /// </summary>
    public async Task<ResultadoAutenticacion> AutenticarAsync(
        string nombreUsuario,
        string hashCandidato,
        string? estacion,
        CancellationToken cancelacion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreUsuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(hashCandidato);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("seg.sp_Usuario_Autenticar", conexion);

            comando.Agregar("@NombreUsuario", SqlDbType.VarChar, 50, nombreUsuario);
            comando.Agregar("@PasswordHashCandidato", SqlDbType.VarChar, 200, hashCandidato);
            comando.Agregar("@Estacion", SqlDbType.NVarChar, 80, estacion);
            comando.Agregar("@MaximoIntentos", SqlDbType.TinyInt, _seguridad.MaximoIntentosFallidos);
            comando.Agregar("@MinutosBloqueo", SqlDbType.Int, _seguridad.MinutosBloqueo);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            if (!await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                return ResultadoAutenticacion.Rechazado("Usuario o contrasena incorrectos.");
            }

            var autenticado = lector.Booleano("Autenticado");
            var mensaje = lector.Texto("Mensaje");
            var segundosBloqueo = lector.Entero("SegundosBloqueo");

            if (!autenticado)
            {
                // El nombre de usuario se registra, pero NUNCA la contrasena ni el hash.
                _registro.Advertencia($"Intento de inicio de sesion rechazado para el usuario '{nombreUsuario}'.");
                return ResultadoAutenticacion.Rechazado(mensaje, segundosBloqueo);
            }

            var usuario = new UsuarioAutenticado
            {
                IdUsuario = lector.Entero("IdUsuario"),
                NombreUsuario = lector.Texto("NombreUsuario"),
                NombreCompleto = lector.Texto("NombreCompleto"),
                Rol = TextosEnumeracion.RolDesde(lector.Texto("Rol"))
            };

            _registro.Informacion($"Inicio de sesion correcto: '{usuario.NombreUsuario}' con rol {usuario.DescripcionRol}.");
            return ResultadoAutenticacion.Correcto(usuario, mensaje);
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al autenticar al usuario '{nombreUsuario}'.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    /// <summary>
    /// Comprueba que la base responde. Alimenta el indicador de conectividad de
    /// FrmPrincipal, por lo que devuelve false en vez de lanzar excepcion.
    /// </summary>
    public async Task<bool> ProbarConexionAsync(CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = new SqlCommand("SELECT 1", conexion) { CommandTimeout = 5 };
            var resultado = await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false);
            return resultado is not null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _registro.Advertencia($"La comprobacion de conectividad con la base de datos fallo: {ex.GetType().Name}.");
            return false;
        }
    }
}
