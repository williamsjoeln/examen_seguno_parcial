using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Aplicacion.Sesion;
using SmartEvent.Dominio.Seguridad;

namespace SmartEvent.Aplicacion.Servicios;

/// <summary>
/// Servicio de inicio de sesion.
///
/// Orquesta la autenticacion EN DOS FASES que expone
/// seg.sp_Usuario_Autenticar:
///
///   FASE 1  Se piden a SQL Server el algoritmo, el numero de iteraciones y el
///           salt del usuario. El hash almacenado NO se devuelve.
///   FASE 2  Con esos parametros se deriva localmente el hash de la contrasena
///           escrita y se envia para que el motor lo compare, actualice el
///           contador de intentos fallidos y aplique el bloqueo temporal.
///
/// Consecuencia que conviene saber explicar: el hash guardado en la base de
/// datos nunca viaja por la red ni llega a la memoria de la aplicacion. Lo
/// unico que sale del motor es el salt, que por definicion no es secreto.
///
/// La contrasena en claro solo existe dentro de este metodo y se descarta en
/// cuanto se deriva el hash; no se guarda en ningun campo ni se registra en el
/// log.
/// </summary>
public sealed class ServicioAutenticacion
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IRegistradorSeguro _registro;
    private readonly SesionUsuario _sesion;

    public ServicioAutenticacion(
        IUsuarioRepositorio usuarios,
        IRegistradorSeguro registro,
        SesionUsuario sesion)
    {
        _usuarios = usuarios ?? throw new ArgumentNullException(nameof(usuarios));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <summary>
    /// Intenta iniciar sesion. Si tiene exito, deja la sesion abierta en
    /// <see cref="SesionUsuario"/>.
    /// </summary>
    public async Task<ResultadoAutenticacion> IniciarSesionAsync(
        string nombreUsuario,
        string contrasena,
        CancellationToken cancelacion)
    {
        // Validacion de entrada. El mensaje es el mismo que el de credenciales
        // incorrectas, para no dar pistas distintas segun el caso.
        if (string.IsNullOrWhiteSpace(nombreUsuario) || string.IsNullOrWhiteSpace(contrasena))
        {
            return ResultadoAutenticacion.Rechazado("Usuario o contrasena incorrectos.");
        }

        var usuario = nombreUsuario.Trim();

        // ---------- FASE 1: parametros de derivacion ----------
        var parametros = await _usuarios
            .ObtenerParametrosDerivacionAsync(usuario, cancelacion)
            .ConfigureAwait(false);

        if (parametros.EstaBloqueado)
        {
            var minutos = (parametros.SegundosBloqueo / 60) + 1;
            _registro.Advertencia($"Intento de acceso a la cuenta bloqueada '{usuario}'.");

            return ResultadoAutenticacion.Rechazado(
                $"La cuenta esta bloqueada temporalmente. Intente nuevamente en {minutos} minuto(s).",
                parametros.SegundosBloqueo);
        }

        // ---------- Derivacion local del hash candidato ----------
        string hashCandidato;
        try
        {
            hashCandidato = HashContrasena.DerivarConParametros(
                contrasena, parametros.SaltBase64, parametros.Iteraciones);
        }
        catch (ArgumentException ex)
        {
            // Solo puede ocurrir si el registro almacenado esta corrupto.
            _registro.Error($"Parametros de derivacion invalidos para el usuario '{usuario}'.", ex);
            return ResultadoAutenticacion.Rechazado("Usuario o contrasena incorrectos.");
        }

        // ---------- FASE 2: comparacion dentro del motor ----------
        var resultado = await _usuarios
            .AutenticarAsync(usuario, hashCandidato, Environment.MachineName, cancelacion)
            .ConfigureAwait(false);

        if (resultado.Autenticado && resultado.Usuario is not null)
        {
            _sesion.Iniciar(resultado.Usuario);
        }

        return resultado;
    }

    /// <summary>Cierra la sesion actual.</summary>
    public void CerrarSesion()
    {
        if (_sesion.HaySesion)
        {
            _registro.Informacion($"Cierre de sesion del usuario '{_sesion.Usuario.NombreUsuario}'.");
        }

        _sesion.Cerrar();
    }

    /// <summary>Comprueba la conectividad con la base de datos.</summary>
    public Task<bool> HayConexionAsync(CancellationToken cancelacion) =>
        _usuarios.ProbarConexionAsync(cancelacion);
}
