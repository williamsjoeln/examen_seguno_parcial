/* =============================================================================
   PROYECTO   : SmartEvent AI
   ARCHIVO    : database/99_pruebas_CA.sql
   AUTOR      : Williams Joel Navarrete Merino
   PROPOSITO  : Demostrar los casos de aceptacion CA-01 a CA-05 ejecutando los
                procedimientos almacenados DIRECTAMENTE contra SQL Server, sin
                pasar por la aplicacion Windows Forms.

   Por que existe este archivo:
   El caso CA-05 del examen exige literalmente que el rechazo ocurra "desde SQL
   INCLUSO SI SE OMITE LA VALIDACION VISUAL". La unica forma de demostrarlo es
   invocar los procedimientos sin la interfaz. Este script hace precisamente eso
   y sirve como evidencia reproducible.

   REQUISITO: haber ejecutado antes database/00_SmartEventAI.sql

   EJECUCION:
     sqlcmd -S .\NOMBRE_INSTANCIA -E -C -i database\99_pruebas_CA.sql

   El script es REEJECUTABLE: al inicio borra las reservas de prueba.
   Cada bloque imprime OK si el comportamiento es el esperado y FALLO si no.
   ============================================================================= */

SET NOCOUNT ON;
USE SmartEventAI;
GO

PRINT '';
PRINT '#############################################################';
PRINT '#  SmartEvent AI - Pruebas de aceptacion CA-01 a CA-05      #';
PRINT '#  Ejecutadas directamente sobre SQL Server, sin interfaz   #';
PRINT '#############################################################';
PRINT '';

/* --- Limpieza: las FK con ON DELETE CASCADE arrastran detalle, auditoria,
       analisis de IA y correos asociados. --- */
DELETE FROM evt.Reserva;
PRINT '>> Reservas previas eliminadas. Estado inicial limpio.';
PRINT '';
GO

/* --- Identificadores de trabajo --- */
DECLARE @IdAdmin        INT = (SELECT IdUsuario FROM seg.Usuario  WHERE NombreUsuario = 'admin'),
        @IdCoord        INT = (SELECT IdUsuario FROM seg.Usuario  WHERE NombreUsuario = 'coordinador'),
        @IdCliente      INT = (SELECT IdCliente FROM evt.Cliente  WHERE Identificacion = '1712345678'),
        @IdSalonQuito   INT = (SELECT IdSalon   FROM evt.Salon    WHERE Nombre = N'Salon Quito'),
        @IdSalonCuenca  INT = (SELECT IdSalon   FROM evt.Salon    WHERE Nombre = N'Salon Cuenca'),
        @IdProyector    INT = (SELECT IdRecurso FROM evt.Recurso  WHERE Nombre = N'Proyector 4K'),
        @IdSillas       INT = (SELECT IdRecurso FROM evt.Recurso  WHERE Nombre = N'Silla ejecutiva'),
        @IdCatering     INT = (SELECT IdRecurso FROM evt.Recurso  WHERE Nombre = N'Servicio de catering'),
        @IdPantalla     INT = (SELECT IdRecurso FROM evt.Recurso  WHERE Nombre = N'Pantalla LED 120 pulgadas'),
        @IdInactivo     INT = (SELECT IdRecurso FROM evt.Recurso  WHERE Nombre = N'Kit de senaletica'),
        @Fecha          DATE = DATEADD(DAY, 30, CAST(SYSDATETIME() AS DATE));

DECLARE @IdReservaCA01  INT,
        @Codigo         VARCHAR(24),
        @Mensaje        NVARCHAR(300),
        @Resultado      INT;

/* =============================================================================
   CA-01  Guardar una reserva valida con TRES detalles; al consultar debe
          recuperar exactamente la misma cabecera y los tres detalles.
   ============================================================================= */
PRINT '=============================================================';
PRINT ' CA-01  Alta de reserva valida con tres detalles';
PRINT '=============================================================';

DECLARE @Detalles evt.ReservaDetalleTipo;

INSERT INTO @Detalles (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdProyector,  2,  45.00,  0.00),
    (@IdSillas,    80,   3.50,  5.00),
    (@IdCatering,  80,   9.75, 10.00);

EXEC evt.sp_Reserva_Guardar
        @IdReserva                 = NULL,
        @IdCliente                 = @IdCliente,
        @IdSalon                   = @IdSalonQuito,
        @FechaEvento               = @Fecha,
        @HoraInicio                = '09:00',
        @HoraFin                   = '13:00',
        @NumeroInvitados           = 80,
        @Observacion               = N'Reserva de prueba CA-01',
        @PorcentajeDescuentoGlobal = 0,
        @IdUsuario                 = @IdCoord,
        @Detalles                  = @Detalles,
        @IdReservaResultado        = @IdReservaCA01 OUTPUT,
        @CodigoResultado           = @Codigo        OUTPUT,
        @Mensaje                   = @Mensaje       OUTPUT;

DECLARE @Detalles01 INT = (SELECT COUNT(*) FROM evt.ReservaDetalle WHERE IdReserva = @IdReservaCA01);

IF @IdReservaCA01 IS NOT NULL AND @Detalles01 = 3
    PRINT '  OK     Reserva ' + @Codigo + ' creada con ' + CAST(@Detalles01 AS VARCHAR(5)) + ' detalles.';
ELSE
    PRINT '  FALLO  No se creo la reserva o no quedaron los tres detalles.';

PRINT '';
PRINT '        --- Cabecera y detalle recuperados (sp_Reserva_ObtenerPorId) ---';
EXEC evt.sp_Reserva_ObtenerPorId @IdReserva = @IdReservaCA01;

/* Comprobacion de los totales calculados por SQL Server (reglas D16 a D18):
     TarifaBase Salon Quito ............ 450.00
     Proyector 4K    2 x 45.00  sin desc.  90.00
     Silla ejecutiva 80 x 3.50  -5%       266.00
     Catering        80 x 9.75  -10%      702.00
     Subtotal ........................  1508.00
     Descuento global 0% .............     0.00
     Base neta .......................  1508.00
     Impuesto 15% ....................   226.20
     Total ...........................  1734.20                              */
DECLARE @TotalEsperado DECIMAL(12,2) = 1734.20;
DECLARE @TotalReal     DECIMAL(12,2) = (SELECT Total FROM evt.Reserva WHERE IdReserva = @IdReservaCA01);

IF @TotalReal = @TotalEsperado
    PRINT '  OK     Totales recalculados por SQL Server: Total = ' + CAST(@TotalReal AS VARCHAR(20)) + ' (esperado ' + CAST(@TotalEsperado AS VARCHAR(20)) + ').';
ELSE
    PRINT '  FALLO  Total calculado ' + CAST(@TotalReal AS VARCHAR(20)) + ' distinto del esperado ' + CAST(@TotalEsperado AS VARCHAR(20)) + '.';
PRINT '';

/* =============================================================================
   CA-02  Provocar un error en UN detalle y comprobar que no queda cabecera ni
          detalles parciales (atomicidad de la transaccion).
   ============================================================================= */
PRINT '=============================================================';
PRINT ' CA-02  Rollback completo cuando falla un detalle';
PRINT '=============================================================';

DECLARE @ReservasAntes INT = (SELECT COUNT(*) FROM evt.Reserva);
DECLARE @DetallesAntes INT = (SELECT COUNT(*) FROM evt.ReservaDetalle);

DECLARE @DetallesMalos evt.ReservaDetalleTipo;
/* Las dos primeras lineas son perfectamente validas; la TERCERA apunta a un
   recurso inactivo. Si la transaccion no fuera atomica, quedarian la cabecera
   y dos detalles guardados. */
INSERT INTO @DetallesMalos (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdProyector,  1,  45.00, 0.00),
    (@IdSillas,    20,   3.50, 0.00),
    (@IdInactivo,   2,  25.00, 0.00);

DECLARE @IdMalo INT, @CodMalo VARCHAR(24), @MsgMalo NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva                 = NULL,
            @IdCliente                 = @IdCliente,
            @IdSalon                   = @IdSalonCuenca,
            @FechaEvento               = @Fecha,
            @HoraInicio                = '15:00',
            @HoraFin                   = '18:00',
            @NumeroInvitados           = 30,
            @Observacion               = N'Reserva de prueba CA-02',
            @PorcentajeDescuentoGlobal = 0,
            @IdUsuario                 = @IdCoord,
            @Detalles                  = @DetallesMalos,
            @IdReservaResultado        = @IdMalo   OUTPUT,
            @CodigoResultado           = @CodMalo  OUTPUT,
            @Mensaje                   = @MsgMalo  OUTPUT;

    PRINT '  FALLO  El procedimiento acepto un detalle invalido.';
END TRY
BEGIN CATCH
    PRINT '        Error controlado: ' + ERROR_MESSAGE();
END CATCH

DECLARE @ReservasDespues INT = (SELECT COUNT(*) FROM evt.Reserva);
DECLARE @DetallesDespues INT = (SELECT COUNT(*) FROM evt.ReservaDetalle);

IF @ReservasAntes = @ReservasDespues AND @DetallesAntes = @DetallesDespues
    PRINT '  OK     Rollback completo: reservas ' + CAST(@ReservasAntes AS VARCHAR(5)) + ' -> ' + CAST(@ReservasDespues AS VARCHAR(5))
        + ', detalles ' + CAST(@DetallesAntes AS VARCHAR(5)) + ' -> ' + CAST(@DetallesDespues AS VARCHAR(5)) + '. No hay datos parciales.';
ELSE
    PRINT '  FALLO  Quedaron datos parciales tras el error.';
PRINT '';

/* =============================================================================
   CA-03  Intentar reservar el mismo salon en una franja que se cruza
          PARCIALMENTE con otra reserva activa; debe rechazarse.
   ============================================================================= */
PRINT '=============================================================';
PRINT ' CA-03  Rechazo por cruce parcial de horario';
PRINT '=============================================================';
PRINT '        Reserva existente : 09:00 - 13:00 en Salon Quito';
PRINT '        Reserva solicitada: 12:00 - 15:00 en Salon Quito (se cruza 12:00-13:00)';

DECLARE @DetallesCruce evt.ReservaDetalleTipo;
INSERT INTO @DetallesCruce (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdProyector, 1, 45.00, 0.00);

DECLARE @IdCruce INT, @CodCruce VARCHAR(24), @MsgCruce NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva                 = NULL,
            @IdCliente                 = @IdCliente,
            @IdSalon                   = @IdSalonQuito,
            @FechaEvento               = @Fecha,
            @HoraInicio                = '12:00',
            @HoraFin                   = '15:00',
            @NumeroInvitados           = 50,
            @Observacion               = N'Reserva de prueba CA-03',
            @PorcentajeDescuentoGlobal = 0,
            @IdUsuario                 = @IdCoord,
            @Detalles                  = @DetallesCruce,
            @IdReservaResultado        = @IdCruce  OUTPUT,
            @CodigoResultado           = @CodCruce OUTPUT,
            @Mensaje                   = @MsgCruce OUTPUT;

    PRINT '  FALLO  Se acepto una reserva que se cruza con otra existente.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50017
        PRINT '  OK     Rechazada por cruce de horario: ' + ERROR_MESSAGE();
    ELSE
        PRINT '  FALLO  Se rechazo, pero por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH
PRINT '';

/* Comprobacion adicional: una franja ADYACENTE (13:00-15:00) NO se cruza,
   porque la formula del examen usa comparaciones estrictas. */
PRINT '        Comprobacion de la formula: franja adyacente 13:00 - 15:00';
DECLARE @IdAdy INT, @CodAdy VARCHAR(24), @MsgAdy NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva                 = NULL,
            @IdCliente                 = @IdCliente,
            @IdSalon                   = @IdSalonQuito,
            @FechaEvento               = @Fecha,
            @HoraInicio                = '13:00',
            @HoraFin                   = '15:00',
            @NumeroInvitados           = 50,
            @Observacion               = N'Reserva adyacente, no debe cruzarse',
            @PorcentajeDescuentoGlobal = 0,
            @IdUsuario                 = @IdCoord,
            @Detalles                  = @DetallesCruce,
            @IdReservaResultado        = @IdAdy  OUTPUT,
            @CodigoResultado           = @CodAdy OUTPUT,
            @Mensaje                   = @MsgAdy OUTPUT;

    PRINT '  OK     Franja adyacente aceptada (' + @CodAdy + '): inicioNuevo < finExistente es FALSO, no hay cruce.';
END TRY
BEGIN CATCH
    PRINT '  FALLO  Se rechazo una franja adyacente que no se cruza: ' + ERROR_MESSAGE();
END CATCH
PRINT '';

/* =============================================================================
   CA-04  Editar una reserva BORRADOR sin que se detecte a si misma como
          conflicto de horario.
   ============================================================================= */
PRINT '=============================================================';
PRINT ' CA-04  Edicion de BORRADOR sin autoconflicto';
PRINT '=============================================================';
PRINT '        Se reedita la reserva de CA-01 con el MISMO salon, fecha y horario.';

DECLARE @DetallesEdit evt.ReservaDetalleTipo;
INSERT INTO @DetallesEdit (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdProyector,  3,  45.00,  0.00),
    (@IdSillas,    90,   3.50,  5.00),
    (@IdCatering,  90,   9.75, 10.00);

DECLARE @IdEdit INT, @CodEdit VARCHAR(24), @MsgEdit NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva                 = @IdReservaCA01,
            @IdCliente                 = @IdCliente,
            @IdSalon                   = @IdSalonQuito,
            @FechaEvento               = @Fecha,
            @HoraInicio                = '09:00',
            @HoraFin                   = '13:00',
            @NumeroInvitados           = 90,
            @Observacion               = N'Reserva CA-01 editada en CA-04',
            @PorcentajeDescuentoGlobal = 0,
            @IdUsuario                 = @IdCoord,
            @Detalles                  = @DetallesEdit,
            @IdReservaResultado        = @IdEdit  OUTPUT,
            @CodigoResultado           = @CodEdit OUTPUT,
            @Mensaje                   = @MsgEdit OUTPUT;

    DECLARE @InvitadosAhora INT = (SELECT NumeroInvitados FROM evt.Reserva WHERE IdReserva = @IdReservaCA01);
    DECLARE @CodigoAhora    VARCHAR(24) = (SELECT Codigo FROM evt.Reserva WHERE IdReserva = @IdReservaCA01);

    IF @InvitadosAhora = 90 AND @CodigoAhora = @Codigo
        PRINT '  OK     Reserva editada sin autoconflicto. Conserva su codigo ' + @CodigoAhora + ' y ahora tiene 90 invitados.';
    ELSE
        PRINT '  FALLO  La edicion no aplico los cambios esperados.';
END TRY
BEGIN CATCH
    PRINT '  FALLO  La reserva se detecto a si misma como conflicto: ' + ERROR_MESSAGE();
END CATCH
PRINT '';

/* =============================================================================
   CA-05  Exceder la capacidad del salon o el stock concurrente del recurso;
          debe rechazarse DESDE SQL aunque se omita la validacion visual.

          Esta es la demostracion clave: se invoca el procedimiento
          directamente, sin abrir la aplicacion. La regla se cumple igual.
   ============================================================================= */
PRINT '=============================================================';
PRINT ' CA-05  Rechazo desde SQL: capacidad y stock';
PRINT '=============================================================';

/* --- 5.a Capacidad del salon --- */
PRINT '        5.a  Salon Cuenca admite 40 personas; se solicitan 100.';

DECLARE @DetallesCap evt.ReservaDetalleTipo;
INSERT INTO @DetallesCap (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdProyector, 1, 45.00, 0.00);

DECLARE @IdCap INT, @CodCap VARCHAR(24), @MsgCap NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva                 = NULL,
            @IdCliente                 = @IdCliente,
            @IdSalon                   = @IdSalonCuenca,
            @FechaEvento               = @Fecha,
            @HoraInicio                = '08:00',
            @HoraFin                   = '11:00',
            @NumeroInvitados           = 100,
            @Observacion               = N'Prueba CA-05 capacidad',
            @PorcentajeDescuentoGlobal = 0,
            @IdUsuario                 = @IdCoord,
            @Detalles                  = @DetallesCap,
            @IdReservaResultado        = @IdCap  OUTPUT,
            @CodigoResultado           = @CodCap OUTPUT,
            @Mensaje                   = @MsgCap OUTPUT;

    PRINT '  FALLO  Se acepto una reserva que excede la capacidad del salon.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50016
        PRINT '  OK     Rechazada por capacidad: ' + ERROR_MESSAGE();
    ELSE
        PRINT '  FALLO  Rechazada por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH
PRINT '';

/* --- 5.b Stock del recurso --- */
PRINT '        5.b  Pantalla LED 120 pulgadas tiene stock 4; se solicitan 5.';

DECLARE @DetallesStock evt.ReservaDetalleTipo;
INSERT INTO @DetallesStock (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdPantalla, 5, 120.00, 0.00);

DECLARE @IdStock INT, @CodStock VARCHAR(24), @MsgStock NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva                 = NULL,
            @IdCliente                 = @IdCliente,
            @IdSalon                   = @IdSalonCuenca,
            @FechaEvento               = @Fecha,
            @HoraInicio                = '08:00',
            @HoraFin                   = '11:00',
            @NumeroInvitados           = 30,
            @Observacion               = N'Prueba CA-05 stock',
            @PorcentajeDescuentoGlobal = 0,
            @IdUsuario                 = @IdCoord,
            @Detalles                  = @DetallesStock,
            @IdReservaResultado        = @IdStock  OUTPUT,
            @CodigoResultado           = @CodStock OUTPUT,
            @Mensaje                   = @MsgStock OUTPUT;

    PRINT '  FALLO  Se acepto una cantidad superior al stock disponible.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50018
        PRINT '  OK     Rechazada por stock insuficiente: ' + ERROR_MESSAGE();
    ELSE
        PRINT '  FALLO  Rechazada por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH
PRINT '';

/* =============================================================================
   PRUEBAS ADICIONALES DE REGLAS DE NEGOCIO
   No corresponden a un CA numerado, pero demuestran que las reglas viven en el
   motor y no solo en la interfaz.
   ============================================================================= */
PRINT '=============================================================';
PRINT ' Reglas adicionales verificadas en el motor';
PRINT '=============================================================';

/* --- D13: solo ADMINISTRADOR puede superar el 10% de descuento --- */
DECLARE @DetallesDesc evt.ReservaDetalleTipo;
INSERT INTO @DetallesDesc (IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento) VALUES
    (@IdProyector, 1, 45.00, 15.00);

DECLARE @IdDesc INT, @CodDesc VARCHAR(24), @MsgDesc NVARCHAR(300);

BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva = NULL, @IdCliente = @IdCliente, @IdSalon = @IdSalonCuenca,
            @FechaEvento = @Fecha, @HoraInicio = '19:00', @HoraFin = '22:00',
            @NumeroInvitados = 20, @Observacion = N'Prueba descuento 15% como COORDINADOR',
            @PorcentajeDescuentoGlobal = 0, @IdUsuario = @IdCoord, @Detalles = @DetallesDesc,
            @IdReservaResultado = @IdDesc OUTPUT, @CodigoResultado = @CodDesc OUTPUT, @Mensaje = @MsgDesc OUTPUT;

    PRINT '  FALLO  Un COORDINADOR pudo aplicar un descuento del 15%.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50019
        PRINT '  OK     D13 - COORDINADOR rechazado al aplicar 15% de descuento.';
    ELSE
        PRINT '  FALLO  Rechazado por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH

/* El mismo descuento aplicado por un ADMINISTRADOR si debe aceptarse. */
BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva = NULL, @IdCliente = @IdCliente, @IdSalon = @IdSalonCuenca,
            @FechaEvento = @Fecha, @HoraInicio = '19:00', @HoraFin = '22:00',
            @NumeroInvitados = 20, @Observacion = N'Prueba descuento 15% como ADMINISTRADOR',
            @PorcentajeDescuentoGlobal = 0, @IdUsuario = @IdAdmin, @Detalles = @DetallesDesc,
            @IdReservaResultado = @IdDesc OUTPUT, @CodigoResultado = @CodDesc OUTPUT, @Mensaje = @MsgDesc OUTPUT;

    PRINT '  OK     D13 - ADMINISTRADOR si puede aplicar 15% (' + @CodDesc + ').';
END TRY
BEGIN CATCH
    PRINT '  FALLO  El ADMINISTRADOR no pudo aplicar el descuento: ' + ERROR_MESSAGE();
END CATCH

/* --- D22: no se puede confirmar sin analisis de IA ni contingencia --- */
BEGIN TRY
    EXEC evt.sp_Reserva_CambiarEstado
            @IdReserva   = @IdReservaCA01,
            @EstadoNuevo = 'CONFIRMADA',
            @Motivo      = NULL,
            @IdUsuario   = @IdCoord,
            @Resultado   = @Resultado OUTPUT,
            @Mensaje     = @Mensaje   OUTPUT;

    PRINT '  FALLO  Se confirmo una reserva sin analisis de IA.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50022
        PRINT '  OK     D22 - Confirmacion bloqueada por falta de analisis de IA o contingencia.';
    ELSE
        PRINT '  FALLO  Bloqueada por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH

/* Se registra una contingencia manual justificada y ahora si debe confirmar. */
DECLARE @IdAnalisis INT;
EXEC evt.sp_AnalisisIA_Registrar
        @IdReserva                 = @IdReservaCA01,
        @Proveedor                 = 'CONTINGENCIA',
        @Modelo                    = 'N/A',
        @PromptVersion             = 'v1',
        @Exitoso                   = 0,
        @Error                     = N'Servicio de IA no disponible durante la prueba.',
        @EsContingenciaManual      = 1,
        @JustificacionContingencia = N'El servicio de analisis no respondio y el evento requiere confirmacion inmediata por politica comercial.',
        @IdUsuario                 = @IdAdmin,
        @IdAnalisis                = @IdAnalisis OUTPUT;

BEGIN TRY
    EXEC evt.sp_Reserva_CambiarEstado
            @IdReserva = @IdReservaCA01, @EstadoNuevo = 'CONFIRMADA', @Motivo = NULL,
            @IdUsuario = @IdCoord, @Resultado = @Resultado OUTPUT, @Mensaje = @Mensaje OUTPUT;

    PRINT '  OK     D22 - Confirmada con contingencia auditada: ' + @Mensaje;
END TRY
BEGIN CATCH
    PRINT '  FALLO  No se pudo confirmar con contingencia: ' + ERROR_MESSAGE();
END CATCH

/* --- Idempotencia del cambio de estado (base de CA-06 y CA-07) --- */
EXEC evt.sp_Reserva_CambiarEstado
        @IdReserva = @IdReservaCA01, @EstadoNuevo = 'CONFIRMADA', @Motivo = NULL,
        @IdUsuario = @IdCoord, @Resultado = @Resultado OUTPUT, @Mensaje = @Mensaje OUTPUT;

DECLARE @Cambios INT = (SELECT COUNT(*) FROM evt.ReservaAuditoria
                        WHERE IdReserva = @IdReservaCA01 AND EstadoNuevo = 'CONFIRMADA');

IF @Resultado = 1 AND @Cambios = 1
    PRINT '  OK     Idempotencia - segunda confirmacion no cambio nada y la auditoria tiene 1 sola fila.';
ELSE
    PRINT '  FALLO  La reconfirmacion duplico el cambio de estado (' + CAST(@Cambios AS VARCHAR(5)) + ' filas de auditoria).';

/* --- D19: una reserva CONFIRMADA ya no se puede editar --- */
BEGIN TRY
    EXEC evt.sp_Reserva_Guardar
            @IdReserva = @IdReservaCA01, @IdCliente = @IdCliente, @IdSalon = @IdSalonQuito,
            @FechaEvento = @Fecha, @HoraInicio = '09:00', @HoraFin = '13:00',
            @NumeroInvitados = 100, @Observacion = N'Intento de editar una CONFIRMADA',
            @PorcentajeDescuentoGlobal = 0, @IdUsuario = @IdCoord, @Detalles = @DetallesEdit,
            @IdReservaResultado = @IdEdit OUTPUT, @CodigoResultado = @CodEdit OUTPUT, @Mensaje = @MsgEdit OUTPUT;

    PRINT '  FALLO  Se pudo editar una reserva CONFIRMADA.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50011
        PRINT '  OK     D19 - Edicion bloqueada: la reserva ya esta CONFIRMADA.';
    ELSE
        PRINT '  FALLO  Bloqueada por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH

/* --- D23: cancelar exige motivo de al menos 20 caracteres --- */
BEGIN TRY
    EXEC evt.sp_Reserva_CambiarEstado
            @IdReserva = @IdReservaCA01, @EstadoNuevo = 'CANCELADA', @Motivo = N'ya no va',
            @IdUsuario = @IdAdmin, @Resultado = @Resultado OUTPUT, @Mensaje = @Mensaje OUTPUT;

    PRINT '  FALLO  Se cancelo con un motivo de menos de 20 caracteres.';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50021
        PRINT '  OK     D23 - Cancelacion bloqueada por motivo demasiado corto.';
    ELSE
        PRINT '  FALLO  Bloqueada por un motivo distinto: ' + ERROR_MESSAGE();
END CATCH

/* --- E: transicion invalida BORRADOR -> FINALIZADA --- */
DECLARE @IdBorrador INT = (SELECT TOP (1) IdReserva FROM evt.Reserva WHERE Estado = 'BORRADOR' ORDER BY IdReserva);

IF @IdBorrador IS NOT NULL
BEGIN
    BEGIN TRY
        EXEC evt.sp_Reserva_CambiarEstado
                @IdReserva = @IdBorrador, @EstadoNuevo = 'FINALIZADA', @Motivo = NULL,
                @IdUsuario = @IdAdmin, @Resultado = @Resultado OUTPUT, @Mensaje = @Mensaje OUTPUT;

        PRINT '  FALLO  Se permitio pasar de BORRADOR directamente a FINALIZADA.';
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 50020
            PRINT '  OK     E - Transicion BORRADOR -> FINALIZADA rechazada.';
        ELSE
            PRINT '  FALLO  Rechazada por un motivo distinto: ' + ERROR_MESSAGE();
    END CATCH
END

PRINT '';
GO

/* =============================================================================
   RESUMEN FINAL
   ============================================================================= */
PRINT '=============================================================';
PRINT ' Estado final de las reservas de prueba';
PRINT '=============================================================';
GO

SELECT  r.Codigo,
        Salon       = s.Nombre,
        r.FechaEvento,
        r.HoraInicio,
        r.HoraFin,
        r.NumeroInvitados,
        r.Estado,
        r.Subtotal,
        r.Descuento,
        r.Impuesto,
        r.Total,
        Detalles    = (SELECT COUNT(*) FROM evt.ReservaDetalle AS d WHERE d.IdReserva = r.IdReserva)
FROM    evt.Reserva AS r
        INNER JOIN evt.Salon AS s ON s.IdSalon = r.IdSalon
ORDER BY r.IdReserva;
GO

PRINT '';
PRINT 'Fin de las pruebas. Revise que todas las lineas digan OK.';
PRINT '';
GO
