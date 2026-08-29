/* =============================================================================
   PROYECTO   : SmartEvent AI
   ARCHIVO    : database/00_SmartEventAI.sql
   AUTOR      : Williams Joel Navarrete Merino
   EXAMEN     : EX-002-2-A-2026 - Examen Practico II Parcial Bloque II
   PROPOSITO  : Crear la base de datos COMPLETA desde cero, en el orden
                correcto y sin ninguna intervencion manual (Examen SS5).

   CONTENIDO (en orden de ejecucion):
     1. Creacion de la base de datos
     2. Esquemas            seg / evt / com
     3. Tablas, claves primarias, foraneas y restricciones CHECK
     4. Indices
     5. Secuencia de codigos de reserva
     6. Tipo tabla (TVP) para el detalle de la reserva
     7. Datos semilla (roles, usuarios, clientes, salones, recursos)
     8. Procedimientos almacenados

   >>> ADVERTENCIA <<<
   Si la base de datos SmartEventAI ya existe, este script LA ELIMINA y la
   vuelve a crear vacia. Es intencional: el examen exige que el script sea
   reproducible desde cero. No lo ejecute sobre una base con datos que quiera
   conservar.

   EJECUCION (desde la carpeta raiz del repositorio):
     sqlcmd -S .\NOMBRE_INSTANCIA -E -C -i database\00_SmartEventAI.sql
   o abriendo este archivo en SSMS y presionando Ejecutar.
   ============================================================================= */

SET NOCOUNT ON;
GO

/* =============================================================================
   1. BASE DE DATOS
   ============================================================================= */
USE master;
GO

IF DB_ID(N'SmartEventAI') IS NOT NULL
BEGIN
    PRINT '>> La base de datos SmartEventAI ya existe. Se elimina para recrearla desde cero.';
    ALTER DATABASE SmartEventAI SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SmartEventAI;
END
GO

CREATE DATABASE SmartEventAI;
GO

/* Modelo de recuperacion simple: es una base de trabajo academico, no requiere
   log de transacciones para restauracion punto en el tiempo. */
ALTER DATABASE SmartEventAI SET RECOVERY SIMPLE;
GO

/* READ_COMMITTED_SNAPSHOT reduce el bloqueo entre la consulta de reservas y la
   validacion de disponibilidad, que se ejecutan de forma concurrente desde la
   interfaz asincronica. */
ALTER DATABASE SmartEventAI SET READ_COMMITTED_SNAPSHOT ON;
GO

USE SmartEventAI;
GO

PRINT '>> Base de datos SmartEventAI creada.';
GO

/* =============================================================================
   2. ESQUEMAS
   Separan responsabilidades y permiten otorgar permisos por area.
     seg = seguridad (usuarios, roles, intentos de acceso)
     evt = negocio de eventos (clientes, salones, recursos, reservas, IA)
     com = comunicaciones (correo enviado)
   ============================================================================= */
CREATE SCHEMA seg AUTHORIZATION dbo;
GO
CREATE SCHEMA evt AUTHORIZATION dbo;
GO
CREATE SCHEMA com AUTHORIZATION dbo;
GO

PRINT '>> Esquemas seg, evt y com creados.';
GO

/* =============================================================================
   3. TABLAS
   ============================================================================= */

/* -----------------------------------------------------------------------------
   seg.Rol
   Campos minimos exigidos por el examen: IdRol, Nombre unico.
   ----------------------------------------------------------------------------- */
CREATE TABLE seg.Rol
(
    IdRol           INT             IDENTITY(1,1)   NOT NULL,
    Nombre          VARCHAR(20)                     NOT NULL,
    Descripcion     NVARCHAR(150)                   NULL,
    FechaCreacion   DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_Rol_FechaCreacion DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_Rol            PRIMARY KEY CLUSTERED (IdRol),
    CONSTRAINT UQ_Rol_Nombre     UNIQUE (Nombre),
    CONSTRAINT CK_Rol_Nombre     CHECK (Nombre IN ('ADMINISTRADOR', 'COORDINADOR'))
);
GO

/* -----------------------------------------------------------------------------
   seg.Usuario
   Campos minimos: IdUsuario, NombreUsuario, PasswordHash, IdRol, Estado,
   FechaCreacion.

   Campos agregados y su justificacion:
     IntentosFallidos / BloqueadoHasta -> implementan el bloqueo temporal tras
       intentos fallidos que exige FrmLogin (Examen SS7).
     UltimoAcceso -> trazabilidad de sesion.

   PasswordHash guarda el formato  PBKDF2-SHA256$iteraciones$saltB64$hashB64
   La restriccion CK_Usuario_PasswordHash impide, a nivel de motor, que alguien
   inserte una contrasena en texto plano (Examen SS3: nunca en texto plano).
   ----------------------------------------------------------------------------- */
CREATE TABLE seg.Usuario
(
    IdUsuario           INT             IDENTITY(1,1)   NOT NULL,
    NombreUsuario       VARCHAR(50)                     NOT NULL,
    PasswordHash        VARCHAR(200)                    NOT NULL,
    NombreCompleto      NVARCHAR(120)                   NOT NULL,
    IdRol               INT                             NOT NULL,
    Estado              BIT                             NOT NULL
        CONSTRAINT DF_Usuario_Estado DEFAULT (1),
    IntentosFallidos    TINYINT                         NOT NULL
        CONSTRAINT DF_Usuario_IntentosFallidos DEFAULT (0),
    BloqueadoHasta      DATETIME2(0)                    NULL,
    UltimoAcceso        DATETIME2(0)                    NULL,
    FechaCreacion       DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_Usuario_FechaCreacion DEFAULT (SYSDATETIME()),
    FechaModificacion   DATETIME2(0)                    NULL,

    CONSTRAINT PK_Usuario                PRIMARY KEY CLUSTERED (IdUsuario),
    CONSTRAINT UQ_Usuario_NombreUsuario  UNIQUE (NombreUsuario),
    CONSTRAINT FK_Usuario_Rol            FOREIGN KEY (IdRol) REFERENCES seg.Rol (IdRol),
    CONSTRAINT CK_Usuario_NombreUsuario  CHECK (LEN(NombreUsuario) >= 4),
    CONSTRAINT CK_Usuario_Intentos       CHECK (IntentosFallidos >= 0),
    CONSTRAINT CK_Usuario_PasswordHash   CHECK (PasswordHash LIKE 'PBKDF2-SHA256$%$%$%')
);
GO

/* -----------------------------------------------------------------------------
   seg.IntentoAcceso
   TABLA AGREGADA (el examen permite agregar tablas).
   Registra cada intento de inicio de sesion para poder auditar el bloqueo
   temporal. No almacena la contrasena ni el hash: solo el resultado.
   ----------------------------------------------------------------------------- */
CREATE TABLE seg.IntentoAcceso
(
    IdIntento       BIGINT          IDENTITY(1,1)   NOT NULL,
    NombreUsuario   VARCHAR(50)                     NOT NULL,
    Exitoso         BIT                             NOT NULL,
    Motivo          VARCHAR(60)                     NOT NULL,
    Estacion        NVARCHAR(80)                    NULL,
    FechaIntento    DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_IntentoAcceso_Fecha DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_IntentoAcceso PRIMARY KEY CLUSTERED (IdIntento)
);
GO

/* -----------------------------------------------------------------------------
   evt.Cliente
   Campos minimos: IdCliente, Identificacion unica, Nombres, Email, Telefono,
   Estado.
   El CHECK de Email es una validacion basica de formato en el motor; la
   validacion completa se hace ademas en C# (regla D20: confirmar exige email
   valido).
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.Cliente
(
    IdCliente           INT             IDENTITY(1,1)   NOT NULL,
    Identificacion      VARCHAR(20)                     NOT NULL,
    Nombres             NVARCHAR(150)                   NOT NULL,
    Email               VARCHAR(150)                    NOT NULL,
    Telefono            VARCHAR(20)                     NULL,
    Estado              BIT                             NOT NULL
        CONSTRAINT DF_Cliente_Estado DEFAULT (1),
    FechaCreacion       DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_Cliente_FechaCreacion DEFAULT (SYSDATETIME()),
    FechaModificacion   DATETIME2(0)                    NULL,

    CONSTRAINT PK_Cliente                   PRIMARY KEY CLUSTERED (IdCliente),
    CONSTRAINT UQ_Cliente_Identificacion    UNIQUE (Identificacion),
    CONSTRAINT CK_Cliente_Identificacion    CHECK (LEN(Identificacion) >= 5),
    CONSTRAINT CK_Cliente_Nombres           CHECK (LEN(LTRIM(RTRIM(Nombres))) >= 3),
    CONSTRAINT CK_Cliente_Email             CHECK (Email LIKE '_%@_%.__%' AND Email NOT LIKE '% %')
);
GO

/* -----------------------------------------------------------------------------
   evt.Salon
   Campos minimos: IdSalon, Nombre unico, Capacidad, TarifaBase, Estado.
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.Salon
(
    IdSalon             INT             IDENTITY(1,1)   NOT NULL,
    Nombre              NVARCHAR(100)                   NOT NULL,
    Ubicacion           NVARCHAR(150)                   NULL,
    Capacidad           INT                             NOT NULL,
    TarifaBase          DECIMAL(12,2)                   NOT NULL,
    Estado              BIT                             NOT NULL
        CONSTRAINT DF_Salon_Estado DEFAULT (1),
    FechaCreacion       DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_Salon_FechaCreacion DEFAULT (SYSDATETIME()),
    FechaModificacion   DATETIME2(0)                    NULL,

    CONSTRAINT PK_Salon                 PRIMARY KEY CLUSTERED (IdSalon),
    CONSTRAINT UQ_Salon_Nombre          UNIQUE (Nombre),
    CONSTRAINT CK_Salon_Capacidad       CHECK (Capacidad > 0),
    CONSTRAINT CK_Salon_TarifaBase      CHECK (TarifaBase >= 0)
);
GO

/* -----------------------------------------------------------------------------
   evt.Recurso
   Campos minimos: IdRecurso, Nombre unico, Tipo, StockTotal, PrecioUnitario,
   Estado.
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.Recurso
(
    IdRecurso           INT             IDENTITY(1,1)   NOT NULL,
    Nombre              NVARCHAR(100)                   NOT NULL,
    Tipo                NVARCHAR(40)                    NOT NULL,
    StockTotal          INT                             NOT NULL,
    PrecioUnitario      DECIMAL(12,2)                   NOT NULL,
    Estado              BIT                             NOT NULL
        CONSTRAINT DF_Recurso_Estado DEFAULT (1),
    FechaCreacion       DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_Recurso_FechaCreacion DEFAULT (SYSDATETIME()),
    FechaModificacion   DATETIME2(0)                    NULL,

    CONSTRAINT PK_Recurso                   PRIMARY KEY CLUSTERED (IdRecurso),
    CONSTRAINT UQ_Recurso_Nombre            UNIQUE (Nombre),
    CONSTRAINT CK_Recurso_Tipo              CHECK (LEN(LTRIM(RTRIM(Tipo))) >= 3),
    CONSTRAINT CK_Recurso_StockTotal        CHECK (StockTotal >= 0),
    CONSTRAINT CK_Recurso_PrecioUnitario    CHECK (PrecioUnitario >= 0)
);
GO

/* -----------------------------------------------------------------------------
   evt.Reserva  (CABECERA)
   Campos minimos: IdReserva, Codigo unico, IdCliente, IdSalon, FechaEvento,
   HoraInicio, HoraFin, NumeroInvitados, Estado, Subtotal, Descuento, Impuesto,
   Total, Observacion, usuario y fechas de auditoria.

   Campo agregado: PorcentajeDescuentoGlobal.
   Justificacion: el examen exige "Impuesto = 15% sobre la base luego del
   descuento global" pero no define como se obtiene ese descuento. Se modela
   como un porcentaje editable de la cabecera con el mismo rango que el
   descuento de linea (0 a 20%, y mas de 10% solo ADMINISTRADOR). La columna
   Descuento guarda el MONTO resultante, calculado siempre en SQL Server.

   Las reglas D01, D02, D03, D12 y D24 quedan garantizadas por restricciones
   CHECK: no dependen de la interfaz.
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.Reserva
(
    IdReserva                   INT             IDENTITY(1,1)   NOT NULL,
    Codigo                      VARCHAR(24)                     NOT NULL,
    IdCliente                   INT                             NOT NULL,
    IdSalon                     INT                             NOT NULL,
    FechaEvento                 DATE                            NOT NULL,
    HoraInicio                  TIME(0)                         NOT NULL,
    HoraFin                     TIME(0)                         NOT NULL,
    NumeroInvitados             INT                             NOT NULL,
    Estado                      VARCHAR(12)                     NOT NULL
        CONSTRAINT DF_Reserva_Estado DEFAULT ('BORRADOR'),
    Subtotal                    DECIMAL(12,2)                   NOT NULL
        CONSTRAINT DF_Reserva_Subtotal DEFAULT (0),
    PorcentajeDescuentoGlobal   DECIMAL(5,2)                    NOT NULL
        CONSTRAINT DF_Reserva_PorcentajeDescuentoGlobal DEFAULT (0),
    Descuento                   DECIMAL(12,2)                   NOT NULL
        CONSTRAINT DF_Reserva_Descuento DEFAULT (0),
    Impuesto                    DECIMAL(12,2)                   NOT NULL
        CONSTRAINT DF_Reserva_Impuesto DEFAULT (0),
    Total                       DECIMAL(12,2)                   NOT NULL
        CONSTRAINT DF_Reserva_Total DEFAULT (0),
    Observacion                 NVARCHAR(500)                   NULL,
    IdUsuarioCreacion           INT                             NOT NULL,
    FechaCreacion               DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_Reserva_FechaCreacion DEFAULT (SYSDATETIME()),
    IdUsuarioModificacion       INT                             NULL,
    FechaModificacion           DATETIME2(0)                    NULL,

    CONSTRAINT PK_Reserva           PRIMARY KEY CLUSTERED (IdReserva),
    CONSTRAINT UQ_Reserva_Codigo    UNIQUE (Codigo),

    CONSTRAINT FK_Reserva_Cliente               FOREIGN KEY (IdCliente)             REFERENCES evt.Cliente (IdCliente),
    CONSTRAINT FK_Reserva_Salon                 FOREIGN KEY (IdSalon)               REFERENCES evt.Salon (IdSalon),
    CONSTRAINT FK_Reserva_UsuarioCreacion       FOREIGN KEY (IdUsuarioCreacion)     REFERENCES seg.Usuario (IdUsuario),
    CONSTRAINT FK_Reserva_UsuarioModificacion   FOREIGN KEY (IdUsuarioModificacion) REFERENCES seg.Usuario (IdUsuario),

    /* D01: HoraFin posterior a HoraInicio */
    CONSTRAINT CK_Reserva_Horario   CHECK (HoraFin > HoraInicio),
    /* D02: duracion entre 2 y 12 horas (120 y 720 minutos) */
    CONSTRAINT CK_Reserva_Duracion  CHECK (DATEDIFF(MINUTE, HoraInicio, HoraFin) BETWEEN 120 AND 720),
    /* D03: numero de invitados mayor que cero */
    CONSTRAINT CK_Reserva_Invitados CHECK (NumeroInvitados > 0),
    /* D24 / E: solo existen cuatro estados validos */
    CONSTRAINT CK_Reserva_Estado    CHECK (Estado IN ('BORRADOR', 'CONFIRMADA', 'FINALIZADA', 'CANCELADA')),
    /* D12 aplicado al descuento global */
    CONSTRAINT CK_Reserva_DescuentoGlobal CHECK (PorcentajeDescuentoGlobal >= 0 AND PorcentajeDescuentoGlobal <= 20),
    CONSTRAINT CK_Reserva_Montos    CHECK (Subtotal >= 0 AND Descuento >= 0 AND Impuesto >= 0 AND Total >= 0),
    CONSTRAINT CK_Reserva_Codigo    CHECK (LEN(Codigo) >= 8)
);
GO

/* -----------------------------------------------------------------------------
   evt.ReservaDetalle  (DETALLE)
   Campos minimos: IdDetalle, IdReserva, IdRecurso, Cantidad, PrecioUnitario,
   PorcentajeDescuento, SubtotalLinea.

   UQ_ReservaDetalle_Reserva_Recurso implementa la regla D08 ("un recurso no
   puede repetirse en el mismo detalle logico") a nivel de MOTOR: aunque la
   interfaz fallara, la base de datos lo impide.

   ON DELETE CASCADE: al eliminar la cabecera desaparecen sus detalles, nunca
   quedan detalles huerfanos.
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.ReservaDetalle
(
    IdDetalle           INT             IDENTITY(1,1)   NOT NULL,
    IdReserva           INT                             NOT NULL,
    IdRecurso           INT                             NOT NULL,
    Cantidad            INT                             NOT NULL,
    PrecioUnitario      DECIMAL(12,2)                   NOT NULL,
    PorcentajeDescuento DECIMAL(5,2)                    NOT NULL
        CONSTRAINT DF_ReservaDetalle_Descuento DEFAULT (0),
    SubtotalLinea       DECIMAL(12,2)                   NOT NULL,

    CONSTRAINT PK_ReservaDetalle    PRIMARY KEY CLUSTERED (IdDetalle),
    CONSTRAINT FK_ReservaDetalle_Reserva FOREIGN KEY (IdReserva) REFERENCES evt.Reserva (IdReserva) ON DELETE CASCADE,
    CONSTRAINT FK_ReservaDetalle_Recurso FOREIGN KEY (IdRecurso) REFERENCES evt.Recurso (IdRecurso),

    /* D08: un recurso una sola vez por reserva */
    CONSTRAINT UQ_ReservaDetalle_Reserva_Recurso UNIQUE (IdReserva, IdRecurso),
    /* D09 */
    CONSTRAINT CK_ReservaDetalle_Cantidad   CHECK (Cantidad > 0),
    /* D11 */
    CONSTRAINT CK_ReservaDetalle_Precio     CHECK (PrecioUnitario >= 0),
    /* D12 */
    CONSTRAINT CK_ReservaDetalle_Descuento  CHECK (PorcentajeDescuento >= 0 AND PorcentajeDescuento <= 20),
    CONSTRAINT CK_ReservaDetalle_Subtotal   CHECK (SubtotalLinea >= 0)
);
GO

/* -----------------------------------------------------------------------------
   evt.ReservaAuditoria
   TABLA AGREGADA (el examen permite agregar tablas).
   Guarda la traza de cada cambio de estado: quien, cuando y por que.
   Es imprescindible para:
     D23  cancelar exige motivo de al menos 20 caracteres
     CA-06 demostrar que el estado cambio UNA sola vez
     CA-07 demostrar que un reintento de correo NO repite el cambio de estado
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.ReservaAuditoria
(
    IdAuditoria     INT             IDENTITY(1,1)   NOT NULL,
    IdReserva       INT                             NOT NULL,
    EstadoAnterior  VARCHAR(12)                     NOT NULL,
    EstadoNuevo     VARCHAR(12)                     NOT NULL,
    Motivo          NVARCHAR(500)                   NULL,
    IdUsuario       INT                             NOT NULL,
    Fecha           DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_ReservaAuditoria_Fecha DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_ReservaAuditoria          PRIMARY KEY CLUSTERED (IdAuditoria),
    CONSTRAINT FK_ReservaAuditoria_Reserva  FOREIGN KEY (IdReserva) REFERENCES evt.Reserva (IdReserva) ON DELETE CASCADE,
    CONSTRAINT FK_ReservaAuditoria_Usuario  FOREIGN KEY (IdUsuario) REFERENCES seg.Usuario (IdUsuario),
    CONSTRAINT CK_ReservaAuditoria_Estados  CHECK (EstadoAnterior <> EstadoNuevo)
);
GO

/* -----------------------------------------------------------------------------
   evt.AnalisisIA
   Campos minimos: IdAnalisis, IdReserva, Modelo, PromptVersion, RespuestaJson,
   NivelRiesgo, TokensEntrada/Salida, Fecha, Exitoso, Error.

   Campos agregados:
     Proveedor                 -> distingue OPENAI de GITHUB_MODELS; queda
                                  documentado con que backend se genero cada
                                  analisis (transparencia exigida en USO_IA.md).
     EsContingenciaManual      -> regla D22: confirmar exige analisis IA exitoso
     JustificacionContingencia    O una justificacion manual auditada.

   NO se almacena la API key ni el prompt completo con datos del cliente
   (Examen SS10: "No guardar la API key ni datos innecesarios del cliente").
   ----------------------------------------------------------------------------- */
CREATE TABLE evt.AnalisisIA
(
    IdAnalisis                  INT             IDENTITY(1,1)   NOT NULL,
    IdReserva                   INT                             NOT NULL,
    Proveedor                   VARCHAR(30)                     NOT NULL,
    Modelo                      VARCHAR(80)                     NOT NULL,
    PromptVersion               VARCHAR(20)                     NOT NULL,
    RespuestaJson               NVARCHAR(MAX)                   NULL,
    NivelRiesgo                 VARCHAR(6)                      NULL,
    TokensEntrada               INT                             NULL,
    TokensSalida                INT                             NULL,
    DuracionMs                  INT                             NULL,
    Exitoso                     BIT                             NOT NULL
        CONSTRAINT DF_AnalisisIA_Exitoso DEFAULT (0),
    Error                       NVARCHAR(500)                   NULL,
    EsContingenciaManual        BIT                             NOT NULL
        CONSTRAINT DF_AnalisisIA_Contingencia DEFAULT (0),
    JustificacionContingencia   NVARCHAR(500)                   NULL,
    IdUsuario                   INT                             NOT NULL,
    Fecha                       DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_AnalisisIA_Fecha DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_AnalisisIA            PRIMARY KEY CLUSTERED (IdAnalisis),
    CONSTRAINT FK_AnalisisIA_Reserva    FOREIGN KEY (IdReserva) REFERENCES evt.Reserva (IdReserva) ON DELETE CASCADE,
    CONSTRAINT FK_AnalisisIA_Usuario    FOREIGN KEY (IdUsuario) REFERENCES seg.Usuario (IdUsuario),

    CONSTRAINT CK_AnalisisIA_NivelRiesgo CHECK (NivelRiesgo IS NULL OR NivelRiesgo IN ('BAJO', 'MEDIO', 'ALTO')),
    /* La respuesta persistida tiene que ser JSON valido o no guardarse */
    CONSTRAINT CK_AnalisisIA_Json        CHECK (RespuestaJson IS NULL OR ISJSON(RespuestaJson) = 1),
    /* Un analisis exitoso obliga a tener respuesta y nivel de riesgo */
    CONSTRAINT CK_AnalisisIA_Coherencia  CHECK (Exitoso = 0 OR (RespuestaJson IS NOT NULL AND NivelRiesgo IS NOT NULL)),
    /* Una contingencia manual obliga a justificacion de al menos 20 caracteres */
    CONSTRAINT CK_AnalisisIA_Contingencia CHECK
    (
        EsContingenciaManual = 0
        OR (JustificacionContingencia IS NOT NULL AND LEN(LTRIM(RTRIM(JustificacionContingencia))) >= 20)
    )
);
GO

/* -----------------------------------------------------------------------------
   com.CorreoEnviado
   Campos minimos: IdCorreo, IdReserva, Destinatario, Asunto, FechaIntento,
   Estado, Error. El examen indica expresamente NO almacenar credenciales.

   Campos agregados:
     TipoEvento  -> CONFIRMACION o CANCELACION
     Intento     -> numero de intento; permite demostrar en CA-07 que el
                    reenvio quedo auditado como un registro SEPARADO y que la
                    reserva no se duplico.
     ServidorSmtp-> solo host y puerto, JAMAS usuario ni contrasena.
   ----------------------------------------------------------------------------- */
CREATE TABLE com.CorreoEnviado
(
    IdCorreo        INT             IDENTITY(1,1)   NOT NULL,
    IdReserva       INT                             NOT NULL,
    Destinatario    VARCHAR(150)                    NOT NULL,
    Asunto          NVARCHAR(200)                   NOT NULL,
    TipoEvento      VARCHAR(20)                     NOT NULL,
    Intento         SMALLINT                        NOT NULL
        CONSTRAINT DF_CorreoEnviado_Intento DEFAULT (1),
    Estado          VARCHAR(10)                     NOT NULL,
    Error           NVARCHAR(500)                   NULL,
    ServidorSmtp    VARCHAR(120)                    NULL,
    DuracionMs      INT                             NULL,
    IdUsuario       INT                             NOT NULL,
    FechaIntento    DATETIME2(0)                    NOT NULL
        CONSTRAINT DF_CorreoEnviado_Fecha DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_CorreoEnviado         PRIMARY KEY CLUSTERED (IdCorreo),
    CONSTRAINT FK_CorreoEnviado_Reserva FOREIGN KEY (IdReserva) REFERENCES evt.Reserva (IdReserva) ON DELETE CASCADE,
    CONSTRAINT FK_CorreoEnviado_Usuario FOREIGN KEY (IdUsuario) REFERENCES seg.Usuario (IdUsuario),

    CONSTRAINT CK_CorreoEnviado_Estado      CHECK (Estado IN ('ENVIADO', 'ERROR')),
    CONSTRAINT CK_CorreoEnviado_TipoEvento  CHECK (TipoEvento IN ('CONFIRMACION', 'CANCELACION')),
    CONSTRAINT CK_CorreoEnviado_Intento     CHECK (Intento >= 1),
    /* Un registro con Estado ERROR obliga a describir el error */
    CONSTRAINT CK_CorreoEnviado_Coherencia  CHECK (Estado = 'ENVIADO' OR Error IS NOT NULL)
);
GO

PRINT '>> Tablas creadas.';
GO

/* =============================================================================
   4. INDICES
   Se crean solo indices que responden a consultas reales de la aplicacion.
   ============================================================================= */

/* El cruce de horarios (regla D05/D06) es la consulta mas frecuente y la mas
   critica del sistema. Este indice cubre el filtro por salon y fecha, e incluye
   las columnas de horario y estado para resolverse sin acceder a la tabla. */
CREATE NONCLUSTERED INDEX IX_Reserva_Salon_Fecha
    ON evt.Reserva (IdSalon, FechaEvento)
    INCLUDE (HoraInicio, HoraFin, Estado, IdReserva);
GO

/* Filtros de FrmReservasConsulta */
CREATE NONCLUSTERED INDEX IX_Reserva_Cliente_Estado
    ON evt.Reserva (IdCliente, Estado)
    INCLUDE (FechaEvento, Codigo, Total);
GO

CREATE NONCLUSTERED INDEX IX_Reserva_FechaEvento
    ON evt.Reserva (FechaEvento)
    INCLUDE (Estado, IdSalon, IdCliente);
GO

/* Recuperar el detalle de una reserva y calcular el stock comprometido */
CREATE NONCLUSTERED INDEX IX_ReservaDetalle_Recurso
    ON evt.ReservaDetalle (IdRecurso)
    INCLUDE (IdReserva, Cantidad);
GO

/* FrmAuditoriaIntegraciones */
CREATE NONCLUSTERED INDEX IX_CorreoEnviado_Reserva_Fecha
    ON com.CorreoEnviado (IdReserva, FechaIntento DESC);
GO

CREATE NONCLUSTERED INDEX IX_CorreoEnviado_Fecha
    ON com.CorreoEnviado (FechaIntento DESC)
    INCLUDE (Estado, TipoEvento);
GO

CREATE NONCLUSTERED INDEX IX_AnalisisIA_Reserva_Fecha
    ON evt.AnalisisIA (IdReserva, Fecha DESC)
    INCLUDE (Exitoso, NivelRiesgo, EsContingenciaManual);
GO

CREATE NONCLUSTERED INDEX IX_AnalisisIA_Fecha
    ON evt.AnalisisIA (Fecha DESC)
    INCLUDE (Exitoso, NivelRiesgo, Proveedor, Modelo);
GO

CREATE NONCLUSTERED INDEX IX_ReservaAuditoria_Reserva
    ON evt.ReservaAuditoria (IdReserva, Fecha DESC);
GO

CREATE NONCLUSTERED INDEX IX_IntentoAcceso_Usuario_Fecha
    ON seg.IntentoAcceso (NombreUsuario, FechaIntento DESC);
GO

/* Busquedas por nombre en FrmCatalogos */
CREATE NONCLUSTERED INDEX IX_Cliente_Nombres ON evt.Cliente (Nombres) INCLUDE (Identificacion, Email, Estado);
GO

PRINT '>> Indices creados.';
GO

/* =============================================================================
   5. SECUENCIA PARA EL CODIGO DE RESERVA
   El examen no define el formato del codigo. Se usa  RSV-yyyyMMdd-NNNNNN
   donde NNNNNN proviene de una SEQUENCE global: garantiza unicidad incluso con
   varios usuarios guardando al mismo tiempo, sin necesidad de bloquear la
   tabla con MAX(Codigo)+1.
   ============================================================================= */
CREATE SEQUENCE evt.SecuenciaReserva
    AS INT
    START WITH 1
    INCREMENT BY 1
    MINVALUE 1
    NO CYCLE
    CACHE 20;
GO

/* =============================================================================
   6. TIPO TABLA (TVP) PARA EL DETALLE
   Mecanismo exigido por el examen para enviar TODO el detalle en UNA sola
   llamada, dentro de la transaccion del procedimiento almacenado.
   La PRIMARY KEY sobre IdRecurso impide, ya en el propio parametro, que el
   formulario mande dos veces el mismo recurso (regla D08).
   ============================================================================= */
CREATE TYPE evt.ReservaDetalleTipo AS TABLE
(
    IdRecurso           INT             NOT NULL,
    Cantidad            INT             NOT NULL,
    PrecioUnitario      DECIMAL(12,2)   NOT NULL,
    PorcentajeDescuento DECIMAL(5,2)    NOT NULL,

    PRIMARY KEY CLUSTERED (IdRecurso),
    CHECK (Cantidad > 0),
    CHECK (PrecioUnitario >= 0),
    CHECK (PorcentajeDescuento >= 0 AND PorcentajeDescuento <= 20)
);
GO

PRINT '>> Secuencia y tipo tabla (TVP) creados.';
GO

/* =============================================================================
   7. DATOS SEMILLA
   Permiten iniciar sesion y probar el flujo completo inmediatamente despues de
   ejecutar el script (Examen SS17: "los datos semilla permiten iniciar sesion").

   USUARIOS SEMILLA (credenciales de demostracion, documentadas en el README):
     admin        / Admin#2026   -> ADMINISTRADOR
     coordinador  / Coord#2026   -> COORDINADOR

   Los hashes de abajo son PBKDF2-SHA256 con 210000 iteraciones y salt aleatorio
   de 16 bytes por usuario. NO son contrasenas en texto plano y no permiten
   recuperar la original.
   ============================================================================= */

INSERT INTO seg.Rol (Nombre, Descripcion) VALUES
    ('ADMINISTRADOR', N'Acceso total: catalogos, reservas, auditoria de integraciones y descuentos superiores al 10%.'),
    ('COORDINADOR',   N'Operacion de reservas: crear, editar, consultar, analizar con IA, confirmar y cancelar.');
GO

INSERT INTO seg.Usuario (NombreUsuario, PasswordHash, NombreCompleto, IdRol, Estado)
SELECT 'admin',
       'PBKDF2-SHA256$210000$YeHoTpHpZj5pYLIHP58DIQ==$b5Yz0UGaOg3udnKDpj9E1uzeg9s1EPyTl34WorhMKnM=',
       N'Administrador del Sistema',
       IdRol,
       1
FROM seg.Rol WHERE Nombre = 'ADMINISTRADOR';

INSERT INTO seg.Usuario (NombreUsuario, PasswordHash, NombreCompleto, IdRol, Estado)
SELECT 'coordinador',
       'PBKDF2-SHA256$210000$l2FG/bS99rSg2aI1qLpmfw==$4Z/Rn5k1t3c5qzCuAsSkl2LxU16ZBjLYIzqx2xwlrig=',
       N'Coordinador de Eventos',
       IdRol,
       1
FROM seg.Rol WHERE Nombre = 'COORDINADOR';
GO

INSERT INTO evt.Cliente (Identificacion, Nombres, Email, Telefono, Estado) VALUES
    ('1712345678', N'Corporacion Andina S.A.',        'eventos@corporacionandina.ejemplo.com',  '0999111222', 1),
    ('0923456789', N'Textiles del Pacifico Cia. Ltda', 'gerencia@textilespacifico.ejemplo.com',  '0988222333', 1),
    ('1798765432', N'Fundacion Educar Ecuador',        'contacto@educarecuador.ejemplo.org',     '0977333444', 1),
    ('0604567890', N'Consultora Vertice',              'admin@vertice.ejemplo.com',              '0966444555', 1),
    ('1155667788', N'Importadora Norte (inactiva)',    'ventas@importadoranorte.ejemplo.com',    '0955555666', 0);
GO

INSERT INTO evt.Salon (Nombre, Ubicacion, Capacidad, TarifaBase, Estado) VALUES
    (N'Salon Quito',      N'Torre A - Piso 2',  120,  450.00, 1),
    (N'Salon Guayaquil',  N'Torre A - Piso 3',   80,  320.00, 1),
    (N'Salon Cuenca',     N'Torre B - Piso 1',   40,  180.00, 1),
    (N'Auditorio Central',N'Torre B - Piso 5',  300, 1200.00, 1),
    (N'Sala Manta',       N'Torre C - Piso 1',   25,  110.00, 0);
GO

INSERT INTO evt.Recurso (Nombre, Tipo, StockTotal, PrecioUnitario, Estado) VALUES
    (N'Proyector 4K',              N'Equipo',        10,   45.00, 1),
    (N'Pantalla LED 120 pulgadas', N'Equipo',         4,  120.00, 1),
    (N'Microfono inalambrico',     N'Equipo',        20,   15.00, 1),
    (N'Sistema de sonido',         N'Equipo',         6,   90.00, 1),
    (N'Silla ejecutiva',           N'Mobiliario',   300,    3.50, 1),
    (N'Mesa redonda para 10',      N'Mobiliario',    40,   12.00, 1),
    (N'Servicio de catering',      N'Alimentacion', 500,    9.75, 1),
    (N'Estacion de cafe',          N'Alimentacion',  15,   35.00, 1),
    (N'Decoracion floral',         N'Decoracion',    30,   22.00, 1),
    (N'Traduccion simultanea',     N'Servicio',       5,  180.00, 1),
    (N'Kit de senaletica',         N'Servicio',       8,   25.00, 0);
GO

PRINT '>> Datos semilla insertados.';
GO

/* =============================================================================
   8. PROCEDIMIENTOS ALMACENADOS
   -----------------------------------------------------------------------------
   Convencion de errores:
     Todos los errores de REGLA DE NEGOCIO se lanzan con THROW y un numero
     >= 50000 y un mensaje redactado para el usuario final. La capa de datos
     en C# distingue por ese numero: si es >= 50000 muestra el mensaje tal cual
     (es texto nuestro, seguro); si es menor, muestra un mensaje generico y
     registra el detalle tecnico solo en el log local.
     Asi se cumple la regla D25: nunca se filtra SQL interno ni stack traces.

   Catalogo de errores de negocio:
     50001  usuario invalido o inactivo
     50002  rol sin permiso para la operacion
     50010  la reserva no existe
     50011  la reserva no es editable en su estado actual
     50012  la reserva debe tener al menos un detalle
     50013  recurso inexistente o inactivo
     50014  cliente inexistente o inactivo
     50015  salon inexistente o inactivo
     50016  numero de invitados supera la capacidad del salon
     50017  cruce de horario con otra reserva del mismo salon
     50018  stock insuficiente del recurso
     50019  descuento superior al 10% sin rol ADMINISTRADOR
     50020  transicion de estado no permitida
     50021  motivo de cancelacion menor a 20 caracteres
     50022  falta analisis de IA exitoso o justificacion de contingencia
     50023  el cliente no tiene un correo electronico valido
     50024  duplicado en catalogo
   ============================================================================= */
GO

/* Las opciones SET vigentes en el momento de CREAR un procedimiento quedan
   GRABADAS en el, y sqlcmd las deja en OFF por defecto. Se fijan en ON aqui
   para que los procedimientos se comporten igual creados desde sqlcmd, desde
   SSMS o desde Azure Data Studio. */
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* -----------------------------------------------------------------------------
   seg.sp_Usuario_Autenticar
   Responsabilidad (Examen SS5): "Consultar usuario activo y datos de
   autorizacion SIN EXPONER EL HASH a la interfaz."

   Se resuelve en DOS FASES dentro del mismo procedimiento, de modo que el
   PasswordHash almacenado NUNCA sale de SQL Server:

     FASE 1 (@PasswordHashCandidato IS NULL)
       Devuelve unicamente los PARAMETROS DE DERIVACION (algoritmo, numero de
       iteraciones y salt) mas el estado de bloqueo. Con eso la aplicacion
       puede calcular el hash de la contrasena que escribio el usuario.
       Si el usuario no existe o esta inactivo se devuelve un salt SENUELO
       deterministico, para que el atacante no pueda deducir por la respuesta
       ni por el tiempo si el usuario existe (evita enumeracion de usuarios).

     FASE 2 (@PasswordHashCandidato con valor)
       Compara el hash candidato contra el almacenado DENTRO del motor,
       actualiza el contador de intentos fallidos, aplica el bloqueo temporal
       y devuelve los datos de autorizacion (id, nombre, rol) o el rechazo.

   Parametros de bloqueo: 3 intentos fallidos -> 3 minutos de bloqueo.
   Son configurables porque el examen no fija un valor.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE seg.sp_Usuario_Autenticar
    @NombreUsuario          VARCHAR(50),
    @PasswordHashCandidato  VARCHAR(200) = NULL,
    @Estacion               NVARCHAR(80) = NULL,
    @MaximoIntentos         TINYINT      = 3,
    @MinutosBloqueo         INT          = 3
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdUsuario      INT,
            @PasswordHash   VARCHAR(200),
            @Estado         BIT,
            @BloqueadoHasta DATETIME2(0),
            @Intentos       TINYINT,
            @Ahora          DATETIME2(0) = SYSDATETIME();

    SELECT  @IdUsuario      = u.IdUsuario,
            @PasswordHash   = u.PasswordHash,
            @Estado         = u.Estado,
            @BloqueadoHasta = u.BloqueadoHasta,
            @Intentos       = u.IntentosFallidos
    FROM    seg.Usuario AS u
    WHERE   u.NombreUsuario = @NombreUsuario;

    DECLARE @EstaBloqueado   BIT = CASE WHEN @BloqueadoHasta IS NOT NULL AND @BloqueadoHasta > @Ahora THEN 1 ELSE 0 END;
    DECLARE @SegundosBloqueo INT = CASE WHEN @EstaBloqueado = 1 THEN DATEDIFF(SECOND, @Ahora, @BloqueadoHasta) ELSE 0 END;

    /* ---------------- FASE 1: parametros de derivacion ---------------- */
    IF @PasswordHashCandidato IS NULL
    BEGIN
        DECLARE @Iteraciones INT         = 210000;
        DECLARE @SaltBase64  VARCHAR(64);

        IF @IdUsuario IS NOT NULL AND @Estado = 1
        BEGIN
            /* El hash tiene el formato PBKDF2-SHA256$iter$salt$hash.
               Se extraen iteraciones y salt SIN devolver nunca la ultima parte. */
            DECLARE @Resto VARCHAR(200) = SUBSTRING(@PasswordHash, LEN('PBKDF2-SHA256$') + 1, 200);
            SET @Iteraciones = TRY_CAST(LEFT(@Resto, CHARINDEX('$', @Resto) - 1) AS INT);
            SET @Resto       = SUBSTRING(@Resto, CHARINDEX('$', @Resto) + 1, 200);
            SET @SaltBase64  = LEFT(@Resto, CHARINDEX('$', @Resto) - 1);
        END
        ELSE
        BEGIN
            /* Salt senuelo deterministico derivado del nombre de usuario: la
               aplicacion siempre ejecuta el mismo trabajo criptografico, exista
               o no el usuario, y la respuesta final es siempre la misma. Asi no
               se puede deducir por la respuesta ni por el tiempo de proceso si
               la cuenta existe (enumeracion de usuarios).

               Se toman 24 caracteres de la representacion hexadecimal del hash.
               Los digitos hexadecimales (0-9, A-F) son un subconjunto del
               alfabeto Base64, y 24 es multiplo de 4, de modo que el resultado
               es una cadena Base64 sintacticamente valida que la aplicacion
               puede decodificar sin error. No se usan metodos del tipo XML a
               proposito: exigen QUOTED_IDENTIFIER ON, opcion que queda grabada
               al crear el procedimiento y que sqlcmd desactiva por defecto. */
            DECLARE @Semilla VARBINARY(32) =
                HASHBYTES('SHA2_256', 'SmartEvent.Senuelo.' + @NombreUsuario);

            SET @SaltBase64 = LEFT(CONVERT(VARCHAR(64), @Semilla, 2), 24);
        END

        SELECT  Algoritmo       = 'PBKDF2-SHA256',
                Iteraciones     = ISNULL(@Iteraciones, 210000),
                SaltBase64      = @SaltBase64,
                EstaBloqueado   = @EstaBloqueado,
                SegundosBloqueo = @SegundosBloqueo;
        RETURN;
    END

    /* ---------------- FASE 2: verificacion y autorizacion ---------------- */

    /* Cuenta bloqueada: se registra el intento y se rechaza sin comparar. */
    IF @EstaBloqueado = 1
    BEGIN
        INSERT INTO seg.IntentoAcceso (NombreUsuario, Exitoso, Motivo, Estacion)
        VALUES (@NombreUsuario, 0, 'BLOQUEADO', @Estacion);

        SELECT  Autenticado     = CAST(0 AS BIT),
                IdUsuario       = CAST(NULL AS INT),
                NombreUsuario   = CAST(NULL AS VARCHAR(50)),
                NombreCompleto  = CAST(NULL AS NVARCHAR(120)),
                Rol             = CAST(NULL AS VARCHAR(20)),
                SegundosBloqueo = @SegundosBloqueo,
                Mensaje         = CAST(N'La cuenta esta bloqueada temporalmente. Intente nuevamente en '
                                       + CAST((@SegundosBloqueo / 60) + 1 AS NVARCHAR(10)) + N' minuto(s).' AS NVARCHAR(200));
        RETURN;
    END

    /* Usuario inexistente, inactivo o hash distinto: misma respuesta para los
       tres casos, para no revelar cual de ellos ocurrio. */
    IF @IdUsuario IS NULL OR @Estado = 0 OR @PasswordHash <> @PasswordHashCandidato
    BEGIN
        IF @IdUsuario IS NOT NULL AND @Estado = 1
        BEGIN
            SET @Intentos = @Intentos + 1;

            IF @Intentos >= @MaximoIntentos
                UPDATE seg.Usuario
                SET    IntentosFallidos = 0,
                       BloqueadoHasta   = DATEADD(MINUTE, @MinutosBloqueo, @Ahora),
                       FechaModificacion = @Ahora
                WHERE  IdUsuario = @IdUsuario;
            ELSE
                UPDATE seg.Usuario
                SET    IntentosFallidos = @Intentos,
                       FechaModificacion = @Ahora
                WHERE  IdUsuario = @IdUsuario;
        END

        INSERT INTO seg.IntentoAcceso (NombreUsuario, Exitoso, Motivo, Estacion)
        VALUES (@NombreUsuario, 0,
                CASE WHEN @IdUsuario IS NULL THEN 'USUARIO_INEXISTENTE'
                     WHEN @Estado = 0        THEN 'USUARIO_INACTIVO'
                     ELSE                         'CREDENCIAL_INCORRECTA' END,
                @Estacion);

        SELECT  Autenticado     = CAST(0 AS BIT),
                IdUsuario       = CAST(NULL AS INT),
                NombreUsuario   = CAST(NULL AS VARCHAR(50)),
                NombreCompleto  = CAST(NULL AS NVARCHAR(120)),
                Rol             = CAST(NULL AS VARCHAR(20)),
                SegundosBloqueo = 0,
                Mensaje         = CAST(N'Usuario o contrasena incorrectos.' AS NVARCHAR(200));
        RETURN;
    END

    /* Credenciales correctas */
    UPDATE seg.Usuario
    SET    IntentosFallidos  = 0,
           BloqueadoHasta    = NULL,
           UltimoAcceso      = @Ahora,
           FechaModificacion = @Ahora
    WHERE  IdUsuario = @IdUsuario;

    INSERT INTO seg.IntentoAcceso (NombreUsuario, Exitoso, Motivo, Estacion)
    VALUES (@NombreUsuario, 1, 'OK', @Estacion);

    SELECT  Autenticado     = CAST(1 AS BIT),
            IdUsuario       = u.IdUsuario,
            NombreUsuario   = u.NombreUsuario,
            NombreCompleto  = u.NombreCompleto,
            Rol             = r.Nombre,
            SegundosBloqueo = 0,
            Mensaje         = CAST(N'Autenticacion correcta.' AS NVARCHAR(200))
    FROM    seg.Usuario AS u
            INNER JOIN seg.Rol AS r ON r.IdRol = u.IdRol
    WHERE   u.IdUsuario = @IdUsuario;
END
GO

/* -----------------------------------------------------------------------------
   evt.sp_Disponibilidad_Validar
   Responsabilidad (Examen SS5): "Detectar cruces de horario del salon y
   recursos insuficientes, EXCLUYENDO LA PROPIA RESERVA al editar."

   Devuelve UN result set con los conflictos encontrados. Si viene vacio, la
   reserva es viable. Se eligio devolver un result set en lugar de parametros
   OUTPUT para que la capa de datos pueda leerlo con un DataReader asincronico
   sin el problema clasico de que los OUTPUT solo se llenan al cerrar el lector.

   REGLA DE CRUCE (D06), tal cual la exige el examen:
        inicioNuevo < finExistente  AND  finNuevo > inicioExistente
   Solo se consideran reservas en estado BORRADOR o CONFIRMADA: una CANCELADA o
   FINALIZADA ya no ocupa el salon.

   La exclusion @IdReserva es lo que hace pasar el caso CA-04: al editar una
   reserva BORRADOR, esta no se detecta a si misma como conflicto.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Disponibilidad_Validar
    @IdReserva          INT = NULL,
    @IdSalon            INT,
    @FechaEvento        DATE,
    @HoraInicio         TIME(0),
    @HoraFin            TIME(0),
    @NumeroInvitados    INT,
    @Detalles           evt.ReservaDetalleTipo READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Conflictos TABLE
    (
        Codigo  VARCHAR(30)     NOT NULL,
        Mensaje NVARCHAR(400)   NOT NULL
    );

    /* --- 1. El salon debe existir y estar activo --- */
    DECLARE @Capacidad      INT,
            @SalonEstado    BIT,
            @SalonNombre    NVARCHAR(100);

    SELECT  @Capacidad   = s.Capacidad,
            @SalonEstado = s.Estado,
            @SalonNombre = s.Nombre
    FROM    evt.Salon AS s
    WHERE   s.IdSalon = @IdSalon;

    IF @Capacidad IS NULL
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('SALON_INEXISTENTE', N'El salon seleccionado no existe.');
    ELSE IF @SalonEstado = 0
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('SALON_INACTIVO', N'El salon "' + @SalonNombre + N'" esta inactivo y no admite reservas.');

    /* --- 2. Horario y duracion (D01, D02) --- */
    IF @HoraFin <= @HoraInicio
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('HORARIO_INVALIDO', N'La hora de fin debe ser posterior a la hora de inicio.');
    ELSE IF DATEDIFF(MINUTE, @HoraInicio, @HoraFin) < 120
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('DURACION_MINIMA', N'La duracion minima de un evento es de 2 horas.');
    ELSE IF DATEDIFF(MINUTE, @HoraInicio, @HoraFin) > 720
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('DURACION_MAXIMA', N'La duracion maxima de un evento es de 12 horas.');

    /* --- 3. Numero de invitados (D03, D04) --- */
    IF @NumeroInvitados <= 0
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('INVITADOS_INVALIDO', N'El numero de invitados debe ser mayor que cero.');
    ELSE IF @Capacidad IS NOT NULL AND @NumeroInvitados > @Capacidad
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('CAPACIDAD_EXCEDIDA',
                N'El salon "' + ISNULL(@SalonNombre, N'') + N'" admite hasta ' + CAST(@Capacidad AS NVARCHAR(10))
                + N' invitados y se solicitaron ' + CAST(@NumeroInvitados AS NVARCHAR(10)) + N'.');

    /* --- 4. Cruce de horario del salon (D05, D06) --- */
    INSERT INTO @Conflictos (Codigo, Mensaje)
    SELECT  'CRUCE_HORARIO',
            N'El salon ya esta reservado con el codigo ' + r.Codigo + N' de '
            + CONVERT(NVARCHAR(5), r.HoraInicio, 108) + N' a ' + CONVERT(NVARCHAR(5), r.HoraFin, 108)
            + N' (estado ' + r.Estado + N').'
    FROM    evt.Reserva AS r
    WHERE   r.IdSalon     = @IdSalon
      AND   r.FechaEvento = @FechaEvento
      AND   r.Estado IN ('BORRADOR', 'CONFIRMADA')
      /* Exclusion de la propia reserva al editar: hace posible el caso CA-04 */
      AND   (@IdReserva IS NULL OR r.IdReserva <> @IdReserva)
      /* Formula de cruce EXACTA exigida por el examen */
      AND   @HoraInicio < r.HoraFin
      AND   @HoraFin    > r.HoraInicio;

    /* --- 5. Recursos: existencia, estado y stock disponible (D10) --- */
    INSERT INTO @Conflictos (Codigo, Mensaje)
    SELECT  'RECURSO_INEXISTENTE',
            N'El recurso con identificador ' + CAST(d.IdRecurso AS NVARCHAR(10)) + N' no existe.'
    FROM    @Detalles AS d
    WHERE   NOT EXISTS (SELECT 1 FROM evt.Recurso AS rc WHERE rc.IdRecurso = d.IdRecurso);

    INSERT INTO @Conflictos (Codigo, Mensaje)
    SELECT  'RECURSO_INACTIVO',
            N'El recurso "' + rc.Nombre + N'" esta inactivo y no puede reservarse.'
    FROM    @Detalles AS d
            INNER JOIN evt.Recurso AS rc ON rc.IdRecurso = d.IdRecurso
    WHERE   rc.Estado = 0;

    /* Stock comprometido = suma de cantidades de las reservas activas del mismo
       dia cuyo horario se cruza con el solicitado, excluyendo la propia reserva.
       Disponible = StockTotal - comprometido. */
    INSERT INTO @Conflictos (Codigo, Mensaje)
    SELECT  'STOCK_INSUFICIENTE',
            N'El recurso "' + rc.Nombre + N'" tiene ' + CAST(rc.StockTotal - x.Comprometido AS NVARCHAR(10))
            + N' unidad(es) disponible(s) en ese horario y se solicitaron ' + CAST(d.Cantidad AS NVARCHAR(10)) + N'.'
    FROM    @Detalles AS d
            INNER JOIN evt.Recurso AS rc ON rc.IdRecurso = d.IdRecurso
            CROSS APPLY
            (
                SELECT Comprometido = ISNULL(SUM(det.Cantidad), 0)
                FROM   evt.ReservaDetalle AS det
                       INNER JOIN evt.Reserva AS r ON r.IdReserva = det.IdReserva
                WHERE  det.IdRecurso  = d.IdRecurso
                  AND  r.FechaEvento  = @FechaEvento
                  AND  r.Estado IN ('BORRADOR', 'CONFIRMADA')
                  AND  (@IdReserva IS NULL OR r.IdReserva <> @IdReserva)
                  AND  @HoraInicio < r.HoraFin
                  AND  @HoraFin    > r.HoraInicio
            ) AS x
    WHERE   rc.Estado = 1
      AND   d.Cantidad > (rc.StockTotal - x.Comprometido);

    /* --- 6. Al menos un detalle (D07) --- */
    IF NOT EXISTS (SELECT 1 FROM @Detalles)
        INSERT INTO @Conflictos (Codigo, Mensaje)
        VALUES ('SIN_DETALLE', N'La reserva debe incluir al menos un recurso o servicio.');

    SELECT Codigo, Mensaje FROM @Conflictos ORDER BY Codigo;
END
GO

/* -----------------------------------------------------------------------------
   evt.sp_Reserva_Guardar
   Responsabilidad (Examen SS5): "Insertar o actualizar cabecera y
   reemplazar/sincronizar detalles DENTRO DE UNA SOLA TRANSACCION. Recibira el
   detalle mediante parametro tipo tabla (TVP). Debe retornar IdReserva, Codigo
   y mensaje."

   CONDICION CRITICA DEL EXAMEN: "La cabecera y todos sus detalles deben
   confirmarse o revertirse juntos."
   Se garantiza con SET XACT_ABORT ON + BEGIN TRAN / COMMIT / ROLLBACK dentro de
   TRY...CATCH. Cualquier error, sea de regla de negocio (THROW 50xxx) o de
   restriccion del motor, deja la base exactamente como estaba. Esto es lo que
   demuestra el caso CA-02.

   TOTALES (D15-D18): se recalculan SIEMPRE aqui, ignorando lo que envie la
   interfaz. SQL Server es la fuente definitiva.
        SubtotalLinea = Cantidad * PrecioUnitario * (1 - PorcentajeDescuento/100)
        Subtotal      = TarifaBase del salon + SUM(SubtotalLinea)
        Descuento     = Subtotal * PorcentajeDescuentoGlobal / 100
        BaseNeta      = Subtotal - Descuento
        Impuesto      = BaseNeta * 15%
        Total         = BaseNeta + Impuesto
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Reserva_Guardar
    @IdReserva                  INT = NULL,
    @IdCliente                  INT,
    @IdSalon                    INT,
    @FechaEvento                DATE,
    @HoraInicio                 TIME(0),
    @HoraFin                    TIME(0),
    @NumeroInvitados            INT,
    @Observacion                NVARCHAR(500) = NULL,
    @PorcentajeDescuentoGlobal  DECIMAL(5,2)  = 0,
    @IdUsuario                  INT,
    @Detalles                   evt.ReservaDetalleTipo READONLY,
    @IdReservaResultado         INT           OUTPUT,
    @CodigoResultado            VARCHAR(24)   OUTPUT,
    @Mensaje                    NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    /* XACT_ABORT ON hace que cualquier error en tiempo de ejecucion aborte la
       transaccion completa. Es la red de seguridad que evita datos parciales. */
    SET XACT_ABORT ON;

    /* Tasa de impuesto fijada por el examen: 15%. */
    DECLARE @TasaImpuesto DECIMAL(6,4) = 0.1500;

    BEGIN TRY
        BEGIN TRANSACTION;

        /* ---------- Validacion del usuario y su rol ---------- */
        DECLARE @Rol VARCHAR(20);

        SELECT  @Rol = r.Nombre
        FROM    seg.Usuario AS u
                INNER JOIN seg.Rol AS r ON r.IdRol = u.IdRol
        WHERE   u.IdUsuario = @IdUsuario AND u.Estado = 1;

        IF @Rol IS NULL
            THROW 50001, N'El usuario que intenta guardar no existe o esta inactivo.', 1;

        /* ---------- Validacion del cliente ---------- */
        IF NOT EXISTS (SELECT 1 FROM evt.Cliente WHERE IdCliente = @IdCliente AND Estado = 1)
            THROW 50014, N'El cliente seleccionado no existe o esta inactivo.', 1;

        /* ---------- Validacion del salon ---------- */
        DECLARE @TarifaBase DECIMAL(12,2);

        SELECT  @TarifaBase = s.TarifaBase
        FROM    evt.Salon AS s
        WHERE   s.IdSalon = @IdSalon AND s.Estado = 1;

        IF @TarifaBase IS NULL
            THROW 50015, N'El salon seleccionado no existe o esta inactivo.', 1;

        /* ---------- La reserva debe traer al menos un detalle (D07) ---------- */
        IF NOT EXISTS (SELECT 1 FROM @Detalles)
            THROW 50012, N'La reserva debe incluir al menos un recurso o servicio.', 1;

        /* ---------- Los recursos deben existir y estar activos ---------- */
        IF EXISTS (SELECT 1 FROM @Detalles AS d
                   WHERE NOT EXISTS (SELECT 1 FROM evt.Recurso AS rc
                                     WHERE rc.IdRecurso = d.IdRecurso AND rc.Estado = 1))
            THROW 50013, N'Uno de los recursos seleccionados no existe o esta inactivo.', 1;

        /* ---------- Autorizacion de descuentos (D13) ----------
           Solo ADMINISTRADOR puede superar el 10%, tanto en una linea como en
           el descuento global. La comprobacion vive aqui, no en el formulario:
           aunque se llame al procedimiento directamente, la regla se aplica. */
        IF @Rol <> 'ADMINISTRADOR'
        BEGIN
            IF EXISTS (SELECT 1 FROM @Detalles WHERE PorcentajeDescuento > 10)
                THROW 50019, N'Solo un usuario con rol ADMINISTRADOR puede aplicar descuentos superiores al 10% en una linea del detalle.', 1;

            IF @PorcentajeDescuentoGlobal > 10
                THROW 50019, N'Solo un usuario con rol ADMINISTRADOR puede aplicar un descuento global superior al 10%.', 1;
        END

        /* ---------- Si es edicion: la reserva debe existir y ser editable ---------- */
        DECLARE @EstadoActual VARCHAR(12);

        IF @IdReserva IS NOT NULL
        BEGIN
            SELECT  @EstadoActual   = r.Estado,
                    @CodigoResultado = r.Codigo
            FROM    evt.Reserva AS r WITH (UPDLOCK, HOLDLOCK)
            WHERE   r.IdReserva = @IdReserva;

            IF @EstadoActual IS NULL
                THROW 50010, N'La reserva que intenta modificar no existe.', 1;

            /* D19: una reserva CONFIRMADA no puede editar cliente, salon,
               fecha, horario ni detalles. Solo BORRADOR es editable. */
            IF @EstadoActual <> 'BORRADOR'
                THROW 50011, N'Solo se puede modificar una reserva en estado BORRADOR. Esta reserva ya fue confirmada, finalizada o cancelada.', 1;
        END

        /* ---------- Disponibilidad: capacidad, cruce de horario y stock ----------
           Se repiten aqui las mismas reglas que expone sp_Disponibilidad_Validar,
           pero DENTRO de la transaccion y con bloqueo, para que dos usuarios que
           guarden al mismo tiempo no puedan crear un cruce (condicion de
           carrera). El procedimiento de disponibilidad sirve para avisar al
           usuario mientras escribe; este bloque es el que realmente decide. */

        DECLARE @Capacidad INT = (SELECT Capacidad FROM evt.Salon WHERE IdSalon = @IdSalon);

        IF @NumeroInvitados > @Capacidad
            THROW 50016, N'El numero de invitados supera la capacidad del salon seleccionado.', 1;

        IF EXISTS
        (
            SELECT  1
            FROM    evt.Reserva AS r WITH (UPDLOCK, HOLDLOCK)
            WHERE   r.IdSalon     = @IdSalon
              AND   r.FechaEvento = @FechaEvento
              AND   r.Estado IN ('BORRADOR', 'CONFIRMADA')
              AND   (@IdReserva IS NULL OR r.IdReserva <> @IdReserva)
              AND   @HoraInicio < r.HoraFin
              AND   @HoraFin    > r.HoraInicio
        )
            THROW 50017, N'El salon ya tiene otra reserva activa que se cruza con el horario solicitado.', 1;

        IF EXISTS
        (
            SELECT  1
            FROM    @Detalles AS d
                    INNER JOIN evt.Recurso AS rc ON rc.IdRecurso = d.IdRecurso
                    CROSS APPLY
                    (
                        SELECT Comprometido = ISNULL(SUM(det.Cantidad), 0)
                        FROM   evt.ReservaDetalle AS det
                               INNER JOIN evt.Reserva AS r ON r.IdReserva = det.IdReserva
                        WHERE  det.IdRecurso  = d.IdRecurso
                          AND  r.FechaEvento  = @FechaEvento
                          AND  r.Estado IN ('BORRADOR', 'CONFIRMADA')
                          AND  (@IdReserva IS NULL OR r.IdReserva <> @IdReserva)
                          AND  @HoraInicio < r.HoraFin
                          AND  @HoraFin    > r.HoraInicio
                    ) AS x
            WHERE   d.Cantidad > (rc.StockTotal - x.Comprometido)
        )
            THROW 50018, N'La cantidad solicitada de uno de los recursos supera el stock disponible para esa fecha y horario.', 1;

        /* ---------- Calculo de totales: SQL Server es la fuente definitiva ---------- */
        DECLARE @SumaLineas DECIMAL(14,2) =
        (
            SELECT ISNULL(SUM(ROUND(d.Cantidad * d.PrecioUnitario * (1 - d.PorcentajeDescuento / 100.0), 2)), 0)
            FROM   @Detalles AS d
        );

        DECLARE @Subtotal  DECIMAL(12,2) = ROUND(@TarifaBase + @SumaLineas, 2);
        DECLARE @Descuento DECIMAL(12,2) = ROUND(@Subtotal * (@PorcentajeDescuentoGlobal / 100.0), 2);
        DECLARE @BaseNeta  DECIMAL(12,2) = ROUND(@Subtotal - @Descuento, 2);
        DECLARE @Impuesto  DECIMAL(12,2) = ROUND(@BaseNeta * @TasaImpuesto, 2);
        DECLARE @Total     DECIMAL(12,2) = ROUND(@BaseNeta + @Impuesto, 2);

        /* ---------- Insercion o actualizacion de la CABECERA ---------- */
        IF @IdReserva IS NULL
        BEGIN
            DECLARE @Secuencia INT = NEXT VALUE FOR evt.SecuenciaReserva;
            SET @CodigoResultado = 'RSV-' + CONVERT(CHAR(8), @FechaEvento, 112)
                                 + '-' + RIGHT('000000' + CAST(@Secuencia AS VARCHAR(10)), 6);

            INSERT INTO evt.Reserva
            (
                Codigo, IdCliente, IdSalon, FechaEvento, HoraInicio, HoraFin,
                NumeroInvitados, Estado, Subtotal, PorcentajeDescuentoGlobal,
                Descuento, Impuesto, Total, Observacion, IdUsuarioCreacion, FechaCreacion
            )
            VALUES
            (
                @CodigoResultado, @IdCliente, @IdSalon, @FechaEvento, @HoraInicio, @HoraFin,
                @NumeroInvitados, 'BORRADOR', @Subtotal, @PorcentajeDescuentoGlobal,
                @Descuento, @Impuesto, @Total, @Observacion, @IdUsuario, SYSDATETIME()
            );

            SET @IdReservaResultado = CAST(SCOPE_IDENTITY() AS INT);
            SET @Mensaje = N'Reserva creada correctamente con el codigo ' + @CodigoResultado + N'.';
        END
        ELSE
        BEGIN
            UPDATE  evt.Reserva
            SET     IdCliente                 = @IdCliente,
                    IdSalon                   = @IdSalon,
                    FechaEvento               = @FechaEvento,
                    HoraInicio                = @HoraInicio,
                    HoraFin                   = @HoraFin,
                    NumeroInvitados           = @NumeroInvitados,
                    Subtotal                  = @Subtotal,
                    PorcentajeDescuentoGlobal = @PorcentajeDescuentoGlobal,
                    Descuento                 = @Descuento,
                    Impuesto                  = @Impuesto,
                    Total                     = @Total,
                    Observacion               = @Observacion,
                    IdUsuarioModificacion     = @IdUsuario,
                    FechaModificacion         = SYSDATETIME()
            WHERE   IdReserva = @IdReserva;

            SET @IdReservaResultado = @IdReserva;
            SET @Mensaje = N'Reserva ' + @CodigoResultado + N' actualizada correctamente.';
        END

        /* ---------- Sincronizacion del DETALLE ----------
           Se reemplaza el detalle completo: se borra el anterior y se inserta el
           que llego en el TVP, todo dentro de la MISMA transaccion. Es una sola
           sentencia INSERT para todas las filas, no un INSERT por fila desde el
           formulario (prohibicion expresa del examen). */
        DELETE FROM evt.ReservaDetalle
        WHERE  IdReserva = @IdReservaResultado;

        INSERT INTO evt.ReservaDetalle
        (
            IdReserva, IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento, SubtotalLinea
        )
        SELECT  @IdReservaResultado,
                d.IdRecurso,
                d.Cantidad,
                d.PrecioUnitario,
                d.PorcentajeDescuento,
                ROUND(d.Cantidad * d.PrecioUnitario * (1 - d.PorcentajeDescuento / 100.0), 2)
        FROM    @Detalles AS d;

        COMMIT TRANSACTION;

        SET @CodigoResultado = ISNULL(@CodigoResultado, '');
        SELECT IdReserva = @IdReservaResultado, Codigo = @CodigoResultado, Mensaje = @Mensaje;
    END TRY
    BEGIN CATCH
        /* Si la transaccion sigue abierta se revierte por completo: no queda
           cabecera sin detalles ni detalles parciales (condicion critica). */
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        SET @IdReservaResultado = NULL;
        SET @CodigoResultado    = NULL;
        SET @Mensaje            = ERROR_MESSAGE();

        THROW;
    END CATCH
END
GO

/* -----------------------------------------------------------------------------
   evt.sp_Reserva_Consultar
   Responsabilidad (Examen SS5): "Filtros opcionales combinables: codigo,
   cliente, rango de fechas, salon y estado. SIN CONCATENAR SQL."

   Tecnica: cada filtro se expresa como (@Parametro IS NULL OR columna = @Parametro).
   No hay EXEC ni sp_executesql con cadenas armadas: es SQL estatico y
   parametrizado, imposible de inyectar.
   OPTION (RECOMPILE) hace que el optimizador genere un plan adaptado a la
   combinacion de filtros realmente usada, que es el precio razonable por no
   recurrir a SQL dinamico.

   Paginacion con OFFSET/FETCH para la carga progresiva de FrmReservasConsulta.
   TotalFilas viaja en cada fila para saber cuantas paginas hay sin una segunda
   consulta.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Reserva_Consultar
    @Codigo         VARCHAR(24)   = NULL,
    @IdCliente      INT           = NULL,
    @TextoCliente   NVARCHAR(150) = NULL,
    @FechaDesde     DATE          = NULL,
    @FechaHasta     DATE          = NULL,
    @IdSalon        INT           = NULL,
    @Estado         VARCHAR(12)   = NULL,
    @Pagina         INT           = 1,
    @TamanoPagina   INT           = 25
AS
BEGIN
    SET NOCOUNT ON;

    IF @Pagina IS NULL OR @Pagina < 1 SET @Pagina = 1;
    IF @TamanoPagina IS NULL OR @TamanoPagina < 1 SET @TamanoPagina = 25;
    IF @TamanoPagina > 200 SET @TamanoPagina = 200;

    SELECT  r.IdReserva,
            r.Codigo,
            r.IdCliente,
            ClienteNombres      = c.Nombres,
            ClienteEmail        = c.Email,
            r.IdSalon,
            SalonNombre         = s.Nombre,
            r.FechaEvento,
            r.HoraInicio,
            r.HoraFin,
            r.NumeroInvitados,
            r.Estado,
            r.Subtotal,
            r.PorcentajeDescuentoGlobal,
            r.Descuento,
            r.Impuesto,
            r.Total,
            r.Observacion,
            r.FechaCreacion,
            r.FechaModificacion,
            UsuarioCreacion     = uc.NombreUsuario,
            TotalDetalles       = (SELECT COUNT(*) FROM evt.ReservaDetalle AS d WHERE d.IdReserva = r.IdReserva),
            TotalFilas          = COUNT(*) OVER ()
    FROM    evt.Reserva AS r
            INNER JOIN evt.Cliente AS c  ON c.IdCliente = r.IdCliente
            INNER JOIN evt.Salon   AS s  ON s.IdSalon   = r.IdSalon
            INNER JOIN seg.Usuario AS uc ON uc.IdUsuario = r.IdUsuarioCreacion
    WHERE   (@Codigo       IS NULL OR r.Codigo       = @Codigo)
      AND   (@IdCliente    IS NULL OR r.IdCliente    = @IdCliente)
      AND   (@TextoCliente IS NULL OR c.Nombres LIKE N'%' + @TextoCliente + N'%'
                                   OR c.Identificacion LIKE '%' + @TextoCliente + '%')
      AND   (@FechaDesde   IS NULL OR r.FechaEvento >= @FechaDesde)
      AND   (@FechaHasta   IS NULL OR r.FechaEvento <= @FechaHasta)
      AND   (@IdSalon      IS NULL OR r.IdSalon      = @IdSalon)
      AND   (@Estado       IS NULL OR r.Estado       = @Estado)
    ORDER BY r.FechaEvento DESC, r.IdReserva DESC
    OFFSET (@Pagina - 1) * @TamanoPagina ROWS
    FETCH NEXT @TamanoPagina ROWS ONLY
    OPTION (RECOMPILE);
END
GO

/* -----------------------------------------------------------------------------
   evt.sp_Reserva_ObtenerPorId
   Responsabilidad (Examen SS5): "Retornar DOS CONJUNTOS: cabecera y detalle
   completo."
   Es el procedimiento que permite demostrar CA-01: guardar una reserva con tres
   detalles y recuperar exactamente la misma cabecera y los tres detalles.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Reserva_ObtenerPorId
    @IdReserva INT
AS
BEGIN
    SET NOCOUNT ON;

    /* --- Conjunto 1: CABECERA --- */
    SELECT  r.IdReserva,
            r.Codigo,
            r.IdCliente,
            ClienteIdentificacion = c.Identificacion,
            ClienteNombres        = c.Nombres,
            ClienteEmail          = c.Email,
            ClienteTelefono       = c.Telefono,
            r.IdSalon,
            SalonNombre           = s.Nombre,
            SalonCapacidad        = s.Capacidad,
            SalonTarifaBase       = s.TarifaBase,
            r.FechaEvento,
            r.HoraInicio,
            r.HoraFin,
            r.NumeroInvitados,
            r.Estado,
            r.Subtotal,
            r.PorcentajeDescuentoGlobal,
            r.Descuento,
            r.Impuesto,
            r.Total,
            r.Observacion,
            r.IdUsuarioCreacion,
            UsuarioCreacion       = uc.NombreUsuario,
            r.FechaCreacion,
            r.IdUsuarioModificacion,
            r.FechaModificacion
    FROM    evt.Reserva AS r
            INNER JOIN evt.Cliente AS c  ON c.IdCliente  = r.IdCliente
            INNER JOIN evt.Salon   AS s  ON s.IdSalon    = r.IdSalon
            INNER JOIN seg.Usuario AS uc ON uc.IdUsuario = r.IdUsuarioCreacion
    WHERE   r.IdReserva = @IdReserva;

    /* --- Conjunto 2: DETALLE --- */
    SELECT  d.IdDetalle,
            d.IdReserva,
            d.IdRecurso,
            RecursoNombre = rc.Nombre,
            RecursoTipo   = rc.Tipo,
            RecursoStock  = rc.StockTotal,
            d.Cantidad,
            d.PrecioUnitario,
            d.PorcentajeDescuento,
            d.SubtotalLinea
    FROM    evt.ReservaDetalle AS d
            INNER JOIN evt.Recurso AS rc ON rc.IdRecurso = d.IdRecurso
    WHERE   d.IdReserva = @IdReserva
    ORDER BY d.IdDetalle;
END
GO

/* -----------------------------------------------------------------------------
   evt.sp_Reserva_CambiarEstado
   Responsabilidad (Examen SS5): "Validar transiciones permitidas, registrar
   usuario/fecha y rechazar cambios invalidos."

   MAQUINA DE ESTADOS:
        BORRADOR   -> CONFIRMADA | CANCELADA
        CONFIRMADA -> FINALIZADA | CANCELADA
        FINALIZADA -> (terminal)
        CANCELADA  -> (terminal)

   REQUISITOS ADICIONALES AL CONFIRMAR (D20, D21, D22):
        - el cliente debe tener un correo electronico valido
        - la disponibilidad debe seguir vigente (puede haber cambiado desde que
          se guardo el borrador)
        - debe existir un analisis de IA exitoso, o bien una contingencia manual
          debidamente justificada y auditada

   IDEMPOTENCIA (CA-06 y CA-07): si la reserva YA esta en el estado solicitado,
   el procedimiento no vuelve a cambiarla ni escribe una segunda fila de
   auditoria; devuelve Resultado = 1 ("sin cambio"). Gracias a esto, reintentar
   el envio de correo despues de una falla SMTP no puede duplicar el cambio de
   estado.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Reserva_CambiarEstado
    @IdReserva      INT,
    @EstadoNuevo    VARCHAR(12),
    @Motivo         NVARCHAR(500) = NULL,
    @IdUsuario      INT,
    @Resultado      INT           OUTPUT,
    @Mensaje        NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE IdUsuario = @IdUsuario AND Estado = 1)
            THROW 50001, N'El usuario que intenta cambiar el estado no existe o esta inactivo.', 1;

        DECLARE @EstadoActual   VARCHAR(12),
                @Codigo         VARCHAR(24),
                @IdCliente      INT,
                @IdSalon        INT,
                @FechaEvento    DATE,
                @HoraInicio     TIME(0),
                @HoraFin        TIME(0),
                @NumeroInvitados INT;

        SELECT  @EstadoActual    = r.Estado,
                @Codigo          = r.Codigo,
                @IdCliente       = r.IdCliente,
                @IdSalon         = r.IdSalon,
                @FechaEvento     = r.FechaEvento,
                @HoraInicio      = r.HoraInicio,
                @HoraFin         = r.HoraFin,
                @NumeroInvitados = r.NumeroInvitados
        FROM    evt.Reserva AS r WITH (UPDLOCK, HOLDLOCK)
        WHERE   r.IdReserva = @IdReserva;

        IF @EstadoActual IS NULL
            THROW 50010, N'La reserva indicada no existe.', 1;

        /* --- Idempotencia: ya esta en el estado solicitado --- */
        IF @EstadoActual = @EstadoNuevo
        BEGIN
            SET @Resultado = 1;
            SET @Mensaje   = N'La reserva ' + @Codigo + N' ya se encuentra en estado ' + @EstadoNuevo + N'. No se realizo ningun cambio.';
            COMMIT TRANSACTION;
            SELECT Resultado = @Resultado, Mensaje = @Mensaje, Estado = @EstadoActual;
            RETURN;
        END

        /* --- Estados terminales (D24) --- */
        IF @EstadoActual IN ('FINALIZADA', 'CANCELADA')
            THROW 50020, N'La reserva esta en un estado terminal y ya no admite cambios de estado.', 1;

        /* --- Transiciones permitidas (E01, E02, E03) --- */
        IF NOT
        (
            (@EstadoActual = 'BORRADOR'   AND @EstadoNuevo IN ('CONFIRMADA', 'CANCELADA'))
         OR (@EstadoActual = 'CONFIRMADA' AND @EstadoNuevo IN ('FINALIZADA', 'CANCELADA'))
        )
            THROW 50020, N'La transicion de estado solicitada no esta permitida.', 1;

        /* --- Requisitos para CANCELAR (D23) --- */
        IF @EstadoNuevo = 'CANCELADA'
        BEGIN
            IF @Motivo IS NULL OR LEN(LTRIM(RTRIM(@Motivo))) < 20
                THROW 50021, N'Para cancelar una reserva debe indicar un motivo de al menos 20 caracteres.', 1;
        END

        /* --- Requisitos para CONFIRMAR (D20, D21, D22) --- */
        IF @EstadoNuevo = 'CONFIRMADA'
        BEGIN
            /* D20: correo electronico valido del cliente */
            IF NOT EXISTS
            (
                SELECT 1 FROM evt.Cliente
                WHERE  IdCliente = @IdCliente
                  AND  Estado = 1
                  AND  Email LIKE '_%@_%.__%'
                  AND  Email NOT LIKE '% %'
            )
                THROW 50023, N'No se puede confirmar: el cliente no tiene un correo electronico valido o esta inactivo.', 1;

            /* D07: sigue siendo obligatorio tener detalle */
            IF NOT EXISTS (SELECT 1 FROM evt.ReservaDetalle WHERE IdReserva = @IdReserva)
                THROW 50012, N'No se puede confirmar: la reserva no tiene ningun recurso asociado.', 1;

            /* D21: la disponibilidad debe seguir vigente. Otra reserva pudo
               haber ocupado el salon mientras este borrador esperaba. */
            IF EXISTS
            (
                SELECT  1
                FROM    evt.Reserva AS r
                WHERE   r.IdSalon     = @IdSalon
                  AND   r.FechaEvento = @FechaEvento
                  AND   r.Estado IN ('BORRADOR', 'CONFIRMADA')
                  AND   r.IdReserva  <> @IdReserva
                  AND   @HoraInicio < r.HoraFin
                  AND   @HoraFin    > r.HoraInicio
            )
                THROW 50017, N'No se puede confirmar: otra reserva activa ocupa el salon en ese horario.', 1;

            /* D04: la capacidad pudo haber cambiado en el catalogo */
            IF EXISTS (SELECT 1 FROM evt.Salon WHERE IdSalon = @IdSalon AND (Estado = 0 OR Capacidad < @NumeroInvitados))
                THROW 50016, N'No se puede confirmar: el salon esta inactivo o su capacidad ya no cubre el numero de invitados.', 1;

            /* D22: analisis de IA exitoso O contingencia manual justificada */
            IF NOT EXISTS
            (
                SELECT 1
                FROM   evt.AnalisisIA
                WHERE  IdReserva = @IdReserva
                  AND  (Exitoso = 1
                        OR (EsContingenciaManual = 1
                            AND JustificacionContingencia IS NOT NULL
                            AND LEN(LTRIM(RTRIM(JustificacionContingencia))) >= 20))
            )
                THROW 50022, N'No se puede confirmar: la reserva requiere un analisis de IA exitoso, o bien registrar una justificacion de contingencia de al menos 20 caracteres.', 1;
        END

        /* --- Aplicar el cambio y registrar la auditoria --- */
        UPDATE  evt.Reserva
        SET     Estado                = @EstadoNuevo,
                IdUsuarioModificacion = @IdUsuario,
                FechaModificacion     = SYSDATETIME()
        WHERE   IdReserva = @IdReserva;

        INSERT INTO evt.ReservaAuditoria (IdReserva, EstadoAnterior, EstadoNuevo, Motivo, IdUsuario)
        VALUES (@IdReserva, @EstadoActual, @EstadoNuevo, @Motivo, @IdUsuario);

        COMMIT TRANSACTION;

        SET @Resultado = 0;
        SET @Mensaje   = N'La reserva ' + @Codigo + N' paso de ' + @EstadoActual + N' a ' + @EstadoNuevo + N'.';

        SELECT Resultado = @Resultado, Mensaje = @Mensaje, Estado = @EstadoNuevo;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        SET @Resultado = -1;
        SET @Mensaje   = ERROR_MESSAGE();

        THROW;
    END CATCH
END
GO

PRINT '>> Procedimientos obligatorios creados.';
GO

/* =============================================================================
   8b. PROCEDIMIENTOS DE APOYO
   El examen exige seis procedimientos; estos adicionales existen para que la
   aplicacion NUNCA arme SQL en C#: absolutamente todo el acceso a datos pasa
   por procedimientos parametrizados.
   ============================================================================= */

/* ---------------------------- CATALOGO: CLIENTES ---------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Cliente_Consultar
    @Texto          NVARCHAR(150) = NULL,
    @SoloActivos    BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  c.IdCliente, c.Identificacion, c.Nombres, c.Email, c.Telefono,
            c.Estado, c.FechaCreacion, c.FechaModificacion
    FROM    evt.Cliente AS c
    WHERE   (@Texto IS NULL OR c.Nombres LIKE N'%' + @Texto + N'%'
                            OR c.Identificacion LIKE '%' + @Texto + '%'
                            OR c.Email LIKE '%' + @Texto + '%')
      AND   (@SoloActivos = 0 OR c.Estado = 1)
    ORDER BY c.Nombres
    OPTION (RECOMPILE);
END
GO

CREATE OR ALTER PROCEDURE evt.sp_Cliente_Guardar
    @IdCliente      INT = NULL,
    @Identificacion VARCHAR(20),
    @Nombres        NVARCHAR(150),
    @Email          VARCHAR(150),
    @Telefono       VARCHAR(20) = NULL,
    @IdResultado    INT           OUTPUT,
    @Mensaje        NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        /* Deteccion de duplicados exigida por el examen (SS3) */
        IF EXISTS (SELECT 1 FROM evt.Cliente
                   WHERE Identificacion = @Identificacion
                     AND (@IdCliente IS NULL OR IdCliente <> @IdCliente))
            THROW 50024, N'Ya existe un cliente registrado con esa identificacion.', 1;

        IF @IdCliente IS NULL
        BEGIN
            INSERT INTO evt.Cliente (Identificacion, Nombres, Email, Telefono, Estado)
            VALUES (@Identificacion, @Nombres, @Email, @Telefono, 1);

            SET @IdResultado = CAST(SCOPE_IDENTITY() AS INT);
            SET @Mensaje = N'Cliente registrado correctamente.';
        END
        ELSE
        BEGIN
            UPDATE evt.Cliente
            SET    Identificacion = @Identificacion,
                   Nombres        = @Nombres,
                   Email          = @Email,
                   Telefono       = @Telefono,
                   FechaModificacion = SYSDATETIME()
            WHERE  IdCliente = @IdCliente;

            IF @@ROWCOUNT = 0
                THROW 50010, N'El cliente que intenta modificar no existe.', 1;

            SET @IdResultado = @IdCliente;
            SET @Mensaje = N'Cliente actualizado correctamente.';
        END

        SELECT IdCliente = @IdResultado, Mensaje = @Mensaje;
    END TRY
    BEGIN CATCH
        SET @IdResultado = NULL;
        SET @Mensaje = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE evt.sp_Cliente_CambiarEstado
    @IdCliente  INT,
    @Estado     BIT,
    @Mensaje    NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    /* Inactivacion LOGICA: nunca se borra un cliente, para no romper el
       historial de reservas (Examen SS3: "activacion/inactivacion"). */
    UPDATE evt.Cliente
    SET    Estado = @Estado,
           FechaModificacion = SYSDATETIME()
    WHERE  IdCliente = @IdCliente;

    IF @@ROWCOUNT = 0
        THROW 50010, N'El cliente indicado no existe.', 1;

    SET @Mensaje = CASE WHEN @Estado = 1 THEN N'Cliente activado.' ELSE N'Cliente inactivado.' END;
    SELECT Mensaje = @Mensaje;
END
GO

/* ---------------------------- CATALOGO: SALONES ---------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Salon_Consultar
    @Texto          NVARCHAR(150) = NULL,
    @SoloActivos    BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  s.IdSalon, s.Nombre, s.Ubicacion, s.Capacidad, s.TarifaBase,
            s.Estado, s.FechaCreacion, s.FechaModificacion
    FROM    evt.Salon AS s
    WHERE   (@Texto IS NULL OR s.Nombre LIKE N'%' + @Texto + N'%'
                            OR s.Ubicacion LIKE N'%' + @Texto + N'%')
      AND   (@SoloActivos = 0 OR s.Estado = 1)
    ORDER BY s.Nombre
    OPTION (RECOMPILE);
END
GO

CREATE OR ALTER PROCEDURE evt.sp_Salon_Guardar
    @IdSalon        INT = NULL,
    @Nombre         NVARCHAR(100),
    @Ubicacion      NVARCHAR(150) = NULL,
    @Capacidad      INT,
    @TarifaBase     DECIMAL(12,2),
    @IdResultado    INT           OUTPUT,
    @Mensaje        NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF EXISTS (SELECT 1 FROM evt.Salon
                   WHERE Nombre = @Nombre AND (@IdSalon IS NULL OR IdSalon <> @IdSalon))
            THROW 50024, N'Ya existe un salon registrado con ese nombre.', 1;

        IF @IdSalon IS NULL
        BEGIN
            INSERT INTO evt.Salon (Nombre, Ubicacion, Capacidad, TarifaBase, Estado)
            VALUES (@Nombre, @Ubicacion, @Capacidad, @TarifaBase, 1);

            SET @IdResultado = CAST(SCOPE_IDENTITY() AS INT);
            SET @Mensaje = N'Salon registrado correctamente.';
        END
        ELSE
        BEGIN
            UPDATE evt.Salon
            SET    Nombre     = @Nombre,
                   Ubicacion  = @Ubicacion,
                   Capacidad  = @Capacidad,
                   TarifaBase = @TarifaBase,
                   FechaModificacion = SYSDATETIME()
            WHERE  IdSalon = @IdSalon;

            IF @@ROWCOUNT = 0
                THROW 50010, N'El salon que intenta modificar no existe.', 1;

            SET @IdResultado = @IdSalon;
            SET @Mensaje = N'Salon actualizado correctamente.';
        END

        SELECT IdSalon = @IdResultado, Mensaje = @Mensaje;
    END TRY
    BEGIN CATCH
        SET @IdResultado = NULL;
        SET @Mensaje = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE evt.sp_Salon_CambiarEstado
    @IdSalon    INT,
    @Estado     BIT,
    @Mensaje    NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    /* No se permite inactivar un salon que tenga reservas activas: dejaria
       reservas confirmadas apuntando a un salon fuera de servicio. */
    IF @Estado = 0 AND EXISTS (SELECT 1 FROM evt.Reserva
                               WHERE IdSalon = @IdSalon AND Estado IN ('BORRADOR', 'CONFIRMADA'))
        THROW 50011, N'No se puede inactivar el salon porque tiene reservas en estado BORRADOR o CONFIRMADA.', 1;

    UPDATE evt.Salon
    SET    Estado = @Estado,
           FechaModificacion = SYSDATETIME()
    WHERE  IdSalon = @IdSalon;

    IF @@ROWCOUNT = 0
        THROW 50010, N'El salon indicado no existe.', 1;

    SET @Mensaje = CASE WHEN @Estado = 1 THEN N'Salon activado.' ELSE N'Salon inactivado.' END;
    SELECT Mensaje = @Mensaje;
END
GO

/* ---------------------------- CATALOGO: RECURSOS ---------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_Recurso_Consultar
    @Texto          NVARCHAR(150) = NULL,
    @SoloActivos    BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  rc.IdRecurso, rc.Nombre, rc.Tipo, rc.StockTotal, rc.PrecioUnitario,
            rc.Estado, rc.FechaCreacion, rc.FechaModificacion
    FROM    evt.Recurso AS rc
    WHERE   (@Texto IS NULL OR rc.Nombre LIKE N'%' + @Texto + N'%'
                            OR rc.Tipo LIKE N'%' + @Texto + N'%')
      AND   (@SoloActivos = 0 OR rc.Estado = 1)
    ORDER BY rc.Tipo, rc.Nombre
    OPTION (RECOMPILE);
END
GO

CREATE OR ALTER PROCEDURE evt.sp_Recurso_Guardar
    @IdRecurso      INT = NULL,
    @Nombre         NVARCHAR(100),
    @Tipo           NVARCHAR(40),
    @StockTotal     INT,
    @PrecioUnitario DECIMAL(12,2),
    @IdResultado    INT           OUTPUT,
    @Mensaje        NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF EXISTS (SELECT 1 FROM evt.Recurso
                   WHERE Nombre = @Nombre AND (@IdRecurso IS NULL OR IdRecurso <> @IdRecurso))
            THROW 50024, N'Ya existe un recurso registrado con ese nombre.', 1;

        IF @IdRecurso IS NULL
        BEGIN
            INSERT INTO evt.Recurso (Nombre, Tipo, StockTotal, PrecioUnitario, Estado)
            VALUES (@Nombre, @Tipo, @StockTotal, @PrecioUnitario, 1);

            SET @IdResultado = CAST(SCOPE_IDENTITY() AS INT);
            SET @Mensaje = N'Recurso registrado correctamente.';
        END
        ELSE
        BEGIN
            /* No se puede reducir el stock por debajo de lo ya comprometido en
               reservas activas: dejaria reservas imposibles de cumplir. */
            DECLARE @MaximoComprometido INT =
            (
                SELECT ISNULL(MAX(x.Total), 0)
                FROM (
                    SELECT Total = SUM(d.Cantidad)
                    FROM   evt.ReservaDetalle AS d
                           INNER JOIN evt.Reserva AS r ON r.IdReserva = d.IdReserva
                    WHERE  d.IdRecurso = @IdRecurso
                      AND  r.Estado IN ('BORRADOR', 'CONFIRMADA')
                    GROUP BY r.FechaEvento
                ) AS x
            );

            IF @StockTotal < @MaximoComprometido
                THROW 50018, N'No se puede reducir el stock: hay reservas activas que comprometen mas unidades de las indicadas.', 1;

            UPDATE evt.Recurso
            SET    Nombre         = @Nombre,
                   Tipo           = @Tipo,
                   StockTotal     = @StockTotal,
                   PrecioUnitario = @PrecioUnitario,
                   FechaModificacion = SYSDATETIME()
            WHERE  IdRecurso = @IdRecurso;

            IF @@ROWCOUNT = 0
                THROW 50010, N'El recurso que intenta modificar no existe.', 1;

            SET @IdResultado = @IdRecurso;
            SET @Mensaje = N'Recurso actualizado correctamente.';
        END

        SELECT IdRecurso = @IdResultado, Mensaje = @Mensaje;
    END TRY
    BEGIN CATCH
        SET @IdResultado = NULL;
        SET @Mensaje = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE evt.sp_Recurso_CambiarEstado
    @IdRecurso  INT,
    @Estado     BIT,
    @Mensaje    NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @Estado = 0 AND EXISTS (SELECT 1
                               FROM   evt.ReservaDetalle AS d
                                      INNER JOIN evt.Reserva AS r ON r.IdReserva = d.IdReserva
                               WHERE  d.IdRecurso = @IdRecurso
                                 AND  r.Estado IN ('BORRADOR', 'CONFIRMADA'))
        THROW 50011, N'No se puede inactivar el recurso porque forma parte de reservas en estado BORRADOR o CONFIRMADA.', 1;

    UPDATE evt.Recurso
    SET    Estado = @Estado,
           FechaModificacion = SYSDATETIME()
    WHERE  IdRecurso = @IdRecurso;

    IF @@ROWCOUNT = 0
        THROW 50010, N'El recurso indicado no existe.', 1;

    SET @Mensaje = CASE WHEN @Estado = 1 THEN N'Recurso activado.' ELSE N'Recurso inactivado.' END;
    SELECT Mensaje = @Mensaje;
END
GO

/* ------------------------- AUDITORIA: ANALISIS DE IA ------------------------- */
CREATE OR ALTER PROCEDURE evt.sp_AnalisisIA_Registrar
    @IdReserva                  INT,
    @Proveedor                  VARCHAR(30),
    @Modelo                     VARCHAR(80),
    @PromptVersion              VARCHAR(20),
    @RespuestaJson              NVARCHAR(MAX) = NULL,
    @NivelRiesgo                VARCHAR(6)    = NULL,
    @TokensEntrada              INT           = NULL,
    @TokensSalida               INT           = NULL,
    @DuracionMs                 INT           = NULL,
    @Exitoso                    BIT,
    @Error                      NVARCHAR(500) = NULL,
    @EsContingenciaManual       BIT           = 0,
    @JustificacionContingencia  NVARCHAR(500) = NULL,
    @IdUsuario                  INT,
    @IdAnalisis                 INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    /* Se persiste TANTO el exito como el fallo: el examen exige guardar el
       modelo utilizado, el resultado y el error cuando corresponda. */
    INSERT INTO evt.AnalisisIA
    (
        IdReserva, Proveedor, Modelo, PromptVersion, RespuestaJson, NivelRiesgo,
        TokensEntrada, TokensSalida, DuracionMs, Exitoso, Error,
        EsContingenciaManual, JustificacionContingencia, IdUsuario
    )
    VALUES
    (
        @IdReserva, @Proveedor, @Modelo, @PromptVersion, @RespuestaJson, @NivelRiesgo,
        @TokensEntrada, @TokensSalida, @DuracionMs, @Exitoso, @Error,
        @EsContingenciaManual, @JustificacionContingencia, @IdUsuario
    );

    SET @IdAnalisis = CAST(SCOPE_IDENTITY() AS INT);
    SELECT IdAnalisis = @IdAnalisis;
END
GO

CREATE OR ALTER PROCEDURE evt.sp_AnalisisIA_Consultar
    @IdReserva      INT           = NULL,
    @Codigo         VARCHAR(24)   = NULL,
    @FechaDesde     DATE          = NULL,
    @FechaHasta     DATE          = NULL,
    @SoloErrores    BIT           = 0,
    @NivelRiesgo    VARCHAR(6)    = NULL,
    @MaximoFilas    INT           = 200
AS
BEGIN
    SET NOCOUNT ON;

    IF @MaximoFilas IS NULL OR @MaximoFilas < 1 SET @MaximoFilas = 200;
    IF @MaximoFilas > 1000 SET @MaximoFilas = 1000;

    SELECT  TOP (@MaximoFilas)
            a.IdAnalisis, a.IdReserva, ReservaCodigo = r.Codigo,
            a.Proveedor, a.Modelo, a.PromptVersion, a.NivelRiesgo,
            a.TokensEntrada, a.TokensSalida, a.DuracionMs,
            a.Exitoso, a.Error, a.EsContingenciaManual, a.JustificacionContingencia,
            a.RespuestaJson, a.Fecha,
            Usuario = u.NombreUsuario
    FROM    evt.AnalisisIA AS a
            INNER JOIN evt.Reserva AS r ON r.IdReserva = a.IdReserva
            INNER JOIN seg.Usuario AS u ON u.IdUsuario = a.IdUsuario
    WHERE   (@IdReserva   IS NULL OR a.IdReserva = @IdReserva)
      AND   (@Codigo      IS NULL OR r.Codigo    = @Codigo)
      AND   (@FechaDesde  IS NULL OR CAST(a.Fecha AS DATE) >= @FechaDesde)
      AND   (@FechaHasta  IS NULL OR CAST(a.Fecha AS DATE) <= @FechaHasta)
      AND   (@SoloErrores = 0      OR a.Exitoso = 0)
      AND   (@NivelRiesgo IS NULL OR a.NivelRiesgo = @NivelRiesgo)
    ORDER BY a.Fecha DESC, a.IdAnalisis DESC
    OPTION (RECOMPILE);
END
GO

/* --------------------------- AUDITORIA: CORREO ---------------------------- */
CREATE OR ALTER PROCEDURE com.sp_CorreoEnviado_Registrar
    @IdReserva      INT,
    @Destinatario   VARCHAR(150),
    @Asunto         NVARCHAR(200),
    @TipoEvento     VARCHAR(20),
    @Estado         VARCHAR(10),
    @Error          NVARCHAR(500) = NULL,
    @ServidorSmtp   VARCHAR(120)  = NULL,
    @DuracionMs     INT           = NULL,
    @IdUsuario      INT,
    @IdCorreo       INT OUTPUT,
    @Intento        SMALLINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    /* El numero de intento se calcula EN SQL a partir de los intentos previos
       de la misma reserva y tipo de evento. Asi, un reenvio siempre queda como
       un registro nuevo y numerado, que es lo que demuestra CA-07. */
    SELECT @Intento = CAST(ISNULL(MAX(Intento), 0) + 1 AS SMALLINT)
    FROM   com.CorreoEnviado
    WHERE  IdReserva = @IdReserva AND TipoEvento = @TipoEvento;

    INSERT INTO com.CorreoEnviado
    (
        IdReserva, Destinatario, Asunto, TipoEvento, Intento,
        Estado, Error, ServidorSmtp, DuracionMs, IdUsuario
    )
    VALUES
    (
        @IdReserva, @Destinatario, @Asunto, @TipoEvento, @Intento,
        @Estado, @Error, @ServidorSmtp, @DuracionMs, @IdUsuario
    );

    SET @IdCorreo = CAST(SCOPE_IDENTITY() AS INT);
    SELECT IdCorreo = @IdCorreo, Intento = @Intento;
END
GO

CREATE OR ALTER PROCEDURE com.sp_CorreoEnviado_Consultar
    @IdReserva      INT          = NULL,
    @Codigo         VARCHAR(24)  = NULL,
    @Destinatario   VARCHAR(150) = NULL,
    @FechaDesde     DATE         = NULL,
    @FechaHasta     DATE         = NULL,
    @Estado         VARCHAR(10)  = NULL,
    @TipoEvento     VARCHAR(20)  = NULL,
    @MaximoFilas    INT          = 200
AS
BEGIN
    SET NOCOUNT ON;

    IF @MaximoFilas IS NULL OR @MaximoFilas < 1 SET @MaximoFilas = 200;
    IF @MaximoFilas > 1000 SET @MaximoFilas = 1000;

    SELECT  TOP (@MaximoFilas)
            ce.IdCorreo, ce.IdReserva, ReservaCodigo = r.Codigo,
            ce.Destinatario, ce.Asunto, ce.TipoEvento, ce.Intento,
            ce.Estado, ce.Error, ce.ServidorSmtp, ce.DuracionMs,
            ce.FechaIntento, Usuario = u.NombreUsuario
    FROM    com.CorreoEnviado AS ce
            INNER JOIN evt.Reserva AS r ON r.IdReserva = ce.IdReserva
            INNER JOIN seg.Usuario AS u ON u.IdUsuario = ce.IdUsuario
    WHERE   (@IdReserva    IS NULL OR ce.IdReserva    = @IdReserva)
      AND   (@Codigo       IS NULL OR r.Codigo        = @Codigo)
      AND   (@Destinatario IS NULL OR ce.Destinatario LIKE '%' + @Destinatario + '%')
      AND   (@FechaDesde   IS NULL OR CAST(ce.FechaIntento AS DATE) >= @FechaDesde)
      AND   (@FechaHasta   IS NULL OR CAST(ce.FechaIntento AS DATE) <= @FechaHasta)
      AND   (@Estado       IS NULL OR ce.Estado       = @Estado)
      AND   (@TipoEvento   IS NULL OR ce.TipoEvento   = @TipoEvento)
    ORDER BY ce.FechaIntento DESC, ce.IdCorreo DESC
    OPTION (RECOMPILE);
END
GO

/* ------------------------ AUDITORIA: CAMBIOS DE ESTADO ------------------------ */
CREATE OR ALTER PROCEDURE evt.sp_ReservaAuditoria_Consultar
    @IdReserva INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  a.IdAuditoria, a.IdReserva, a.EstadoAnterior, a.EstadoNuevo,
            a.Motivo, a.Fecha, Usuario = u.NombreUsuario
    FROM    evt.ReservaAuditoria AS a
            INNER JOIN seg.Usuario AS u ON u.IdUsuario = a.IdUsuario
    WHERE   a.IdReserva = @IdReserva
    ORDER BY a.Fecha DESC, a.IdAuditoria DESC;
END
GO

PRINT '>> Procedimientos de apoyo creados.';
GO

/* =============================================================================
   VERIFICACION FINAL
   Resume lo creado para confirmar a simple vista que el script se ejecuto
   completo.
   ============================================================================= */
PRINT '';
PRINT '===========================================================';
PRINT ' SmartEvent AI - instalacion de base de datos finalizada';
PRINT '===========================================================';
GO

SELECT  Objeto = 'Tablas',                 Cantidad = COUNT(*) FROM sys.tables
UNION ALL
SELECT  'Procedimientos almacenados',      COUNT(*) FROM sys.procedures
UNION ALL
SELECT  'Restricciones CHECK',             COUNT(*) FROM sys.check_constraints
UNION ALL
SELECT  'Claves foraneas',                 COUNT(*) FROM sys.foreign_keys
UNION ALL
SELECT  'Indices no agrupados propios',    COUNT(*) FROM sys.indexes AS i
                                                    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
                                            WHERE i.type_desc = 'NONCLUSTERED' AND i.is_primary_key = 0
UNION ALL
SELECT  'Tipos tabla (TVP)',               COUNT(*) FROM sys.table_types WHERE is_user_defined = 1
UNION ALL
SELECT  'Usuarios semilla',                COUNT(*) FROM seg.Usuario
UNION ALL
SELECT  'Clientes semilla',                COUNT(*) FROM evt.Cliente
UNION ALL
SELECT  'Salones semilla',                 COUNT(*) FROM evt.Salon
UNION ALL
SELECT  'Recursos semilla',                COUNT(*) FROM evt.Recurso;
GO

PRINT '';
PRINT 'Usuarios semilla para iniciar sesion:';
PRINT '   admin        / Admin#2026    (ADMINISTRADOR)';
PRINT '   coordinador  / Coord#2026    (COORDINADOR)';
PRINT '';
GO
