using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Excepciones;

namespace SmartEvent.Aplicacion.Sesion;

/// <summary>
/// Usuario con la sesion abierta en la aplicacion.
///
/// Se registra como singleton en el contenedor de dependencias, de modo que
/// cualquier formulario o servicio puede consultar quien esta trabajando sin
/// pasarlo de parametro en parametro.
///
/// LIMITE IMPORTANTE PARA LA DEFENSA: esta clase sirve para la EXPERIENCIA DE
/// USUARIO (ocultar menus, deshabilitar botones). No es un control de seguridad
/// real: la autorizacion de verdad la aplica SQL Server, que recibe el
/// @IdUsuario y consulta su rol antes de permitir, por ejemplo, un descuento
/// superior al 10%. Aunque alguien manipulara la interfaz, el procedimiento
/// almacenado seguiria rechazando la operacion.
/// </summary>
public sealed class SesionUsuario
{
    private UsuarioAutenticado? _usuario;

    /// <summary>Indica si hay una sesion abierta.</summary>
    public bool HaySesion => _usuario is not null;

    /// <summary>
    /// Usuario actual. Lanza si no hay sesion: acceder aqui sin haber iniciado
    /// sesion siempre es un error de programacion, no una situacion esperable.
    /// </summary>
    public UsuarioAutenticado Usuario =>
        _usuario ?? throw new InvalidOperationException("No hay ninguna sesion de usuario abierta.");

    /// <summary>Identificador del usuario actual, que se envia a los procedimientos almacenados.</summary>
    public int IdUsuario => Usuario.IdUsuario;

    public RolUsuario Rol => Usuario.Rol;

    public bool EsAdministrador => Usuario.EsAdministrador;

    /// <summary>Se invoca desde FrmLogin tras una autenticacion correcta.</summary>
    public void Iniciar(UsuarioAutenticado usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        _usuario = usuario;
    }

    /// <summary>Cierra la sesion. FrmPrincipal lo llama al pulsar "Cerrar sesion".</summary>
    public void Cerrar() => _usuario = null;

    /// <summary>Indica si el usuario actual tiene el permiso indicado.</summary>
    public bool Tiene(Permiso permiso) => HaySesion && Usuario.Tiene(permiso);

    /// <summary>
    /// Exige un permiso y lanza un error de negocio si falta. Se usa como
    /// defensa adicional en los servicios, por si algun formulario olvidara
    /// ocultar una opcion.
    /// </summary>
    public void Exigir(Permiso permiso)
    {
        if (!Tiene(permiso))
        {
            throw new ExcepcionNegocio(
                "Su rol no tiene permiso para realizar esta operacion.");
        }
    }
}
