using System.Security.Cryptography;

namespace SmartEvent.Dominio.Seguridad;

/// <summary>
/// Derivacion y verificacion de contrasenas con PBKDF2-SHA256.
///
/// FORMATO ALMACENADO en seg.Usuario.PasswordHash:
///     PBKDF2-SHA256$210000$saltEnBase64$hashEnBase64
///
/// Por que PBKDF2 y no un hash simple:
///   SHA-256 a secas es rapidisimo, y esa velocidad juega a favor del atacante:
///   permite probar miles de millones de contrasenas por segundo. PBKDF2 repite
///   la funcion 210000 veces, de modo que verificar UNA contrasena cuesta unos
///   milisegundos, pero un ataque por fuerza bruta se vuelve inviable.
///   210000 iteraciones es la cifra que recomienda OWASP para PBKDF2-SHA256.
///
/// Por que un salt por usuario:
///   Sin salt, dos usuarios con la misma contrasena tendrian el mismo hash, y
///   una tabla precalculada (rainbow table) los rompe a los dos de una vez. Con
///   16 bytes aleatorios por usuario, cada contrasena hay que atacarla por
///   separado.
///
/// Por que la comparacion es en tiempo fijo:
///   Comparar dos arreglos byte a byte y salir al primer byte distinto filtra
///   informacion por el tiempo de respuesta. CryptographicOperations.FixedTimeEquals
///   siempre tarda lo mismo.
/// </summary>
public static class HashContrasena
{
    /// <summary>Etiqueta del algoritmo dentro del texto almacenado.</summary>
    public const string Algoritmo = "PBKDF2-SHA256";

    /// <summary>Numero de iteraciones para contrasenas nuevas (recomendacion OWASP).</summary>
    public const int IteracionesPorDefecto = 210_000;

    private const int TamanoSaltBytes = 16;
    private const int TamanoHashBytes = 32;

    /// <summary>
    /// Genera el texto completo a almacenar para una contrasena nueva, con un
    /// salt aleatorio criptograficamente seguro.
    /// </summary>
    public static string Generar(string contrasena, int iteraciones = IteracionesPorDefecto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contrasena);
        ArgumentOutOfRangeException.ThrowIfLessThan(iteraciones, 1000);

        var salt = RandomNumberGenerator.GetBytes(TamanoSaltBytes);
        var hash = Derivar(contrasena, salt, iteraciones);

        return Componer(iteraciones, salt, hash);
    }

    /// <summary>
    /// Deriva el hash de una contrasena usando un salt y un numero de
    /// iteraciones YA CONOCIDOS, y devuelve el texto en el formato almacenado.
    ///
    /// Este es el metodo que usa el inicio de sesion: la primera fase de
    /// seg.sp_Usuario_Autenticar devuelve el salt y las iteraciones (nunca el
    /// hash), aqui se calcula el candidato, y la segunda fase lo compara dentro
    /// de SQL Server.
    /// </summary>
    public static string DerivarConParametros(string contrasena, string saltBase64, int iteraciones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contrasena);
        ArgumentException.ThrowIfNullOrWhiteSpace(saltBase64);
        ArgumentOutOfRangeException.ThrowIfLessThan(iteraciones, 1);

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(saltBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("El salt recibido no tiene un formato Base64 valido.", nameof(saltBase64), ex);
        }

        var hash = Derivar(contrasena, salt, iteraciones);
        return Componer(iteraciones, salt, hash);
    }

    /// <summary>
    /// Verifica una contrasena contra un texto almacenado completo.
    /// Se usa en las pruebas automatizadas y como respaldo; el inicio de sesion
    /// de la aplicacion delega la comparacion en SQL Server para que el hash
    /// almacenado nunca salga del motor.
    /// </summary>
    public static bool Verificar(string contrasena, string almacenado)
    {
        if (string.IsNullOrWhiteSpace(contrasena) || string.IsNullOrWhiteSpace(almacenado))
        {
            return false;
        }

        if (!TryDescomponer(almacenado, out var iteraciones, out var salt, out var hashEsperado))
        {
            return false;
        }

        var hashCandidato = Derivar(contrasena, salt, iteraciones);

        // Comparacion en tiempo fijo: no revela por el tiempo cuantos bytes coincidieron.
        return CryptographicOperations.FixedTimeEquals(hashCandidato, hashEsperado);
    }

    /// <summary>
    /// Extrae el numero de iteraciones y el salt de un texto almacenado, sin
    /// devolver el hash.
    /// </summary>
    public static bool TryLeerParametros(string almacenado, out int iteraciones, out string saltBase64)
    {
        iteraciones = 0;
        saltBase64 = string.Empty;

        if (!TryDescomponer(almacenado, out var iter, out var salt, out _))
        {
            return false;
        }

        iteraciones = iter;
        saltBase64 = Convert.ToBase64String(salt);
        return true;
    }

    private static byte[] Derivar(string contrasena, byte[] salt, int iteraciones) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password: contrasena,
            salt: salt,
            iterations: iteraciones,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: TamanoHashBytes);

    private static string Componer(int iteraciones, byte[] salt, byte[] hash) =>
        string.Join('$',
            Algoritmo,
            iteraciones.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));

    private static bool TryDescomponer(string almacenado, out int iteraciones, out byte[] salt, out byte[] hash)
    {
        iteraciones = 0;
        salt = [];
        hash = [];

        var partes = almacenado.Split('$');
        if (partes.Length != 4 || !string.Equals(partes[0], Algoritmo, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(partes[1], System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out iteraciones)
            || iteraciones < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(partes[2]);
            hash = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && hash.Length > 0;
    }
}
