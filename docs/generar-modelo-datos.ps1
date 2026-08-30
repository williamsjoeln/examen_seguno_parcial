<#
    ============================================================================
    SmartEvent AI - Generador del diagrama del modelo de datos
    ----------------------------------------------------------------------------
    Dibuja docs/modelo-datos.png a partir de la descripcion de las tablas.

    Por que un script y no una imagen suelta: el diagrama se puede REGENERAR
    cuando cambie el modelo, y queda versionado como codigo en lugar de como un
    binario que nadie sabe de donde salio.

    Uso:
        powershell -ExecutionPolicy Bypass -File docs\generar-modelo-datos.ps1

    Requiere unicamente Windows con .NET, que ya hace falta para el proyecto.
    ============================================================================
#>

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- dimensiones
$ancho = 1760
$alto  = 1190
$salida = Join-Path $PSScriptRoot 'modelo-datos.png'

# ------------------------------------------------------------------- paleta
$colFondo      = [System.Drawing.Color]::FromArgb(248, 249, 250)
$colBorde      = [System.Drawing.Color]::FromArgb(173, 181, 189)
$colTexto      = [System.Drawing.Color]::FromArgb(33, 37, 41)
$colSuave      = [System.Drawing.Color]::FromArgb(108, 117, 125)
$colLinea      = [System.Drawing.Color]::FromArgb(108, 117, 125)
$colSeg        = [System.Drawing.Color]::FromArgb(108, 52, 131)   # esquema seg
$colEvt        = [System.Drawing.Color]::FromArgb(13, 59, 102)    # esquema evt
$colCom        = [System.Drawing.Color]::FromArgb(27, 127, 79)    # esquema com

# ------------------------------------------------------------------- fuentes
$fTitulo   = New-Object System.Drawing.Font('Segoe UI', 20, [System.Drawing.FontStyle]::Bold)
$fSubtitulo= New-Object System.Drawing.Font('Segoe UI', 10)
$fTabla    = New-Object System.Drawing.Font('Segoe UI', 10.5, [System.Drawing.FontStyle]::Bold)
$fCampo    = New-Object System.Drawing.Font('Consolas', 8.5)
$fCampoPk  = New-Object System.Drawing.Font('Consolas', 8.5, [System.Drawing.FontStyle]::Bold)
$fRel      = New-Object System.Drawing.Font('Segoe UI', 8)
$fLeyenda  = New-Object System.Drawing.Font('Segoe UI', 9)

$mapa = New-Object 'System.Collections.Generic.Dictionary[string,object]'

$bmp = New-Object System.Drawing.Bitmap($ancho, $alto)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear($colFondo)

# =============================================================== dibujar tabla
function Draw-Tabla {
    param(
        [string]$Clave, [string]$Nombre, [int]$X, [int]$Y, [int]$W,
        [System.Drawing.Color]$Color, [string[]]$Campos, [string]$Nota = ''
    )

    $altoCab = 30
    $altoFila = 17
    $h = $altoCab + ($Campos.Count * $altoFila) + 8
    if ($Nota) { $h += 16 }

    # sombra
    $sombra = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 0, 0, 0))
    $g.FillRectangle($sombra, ($X + 3), ($Y + 3), $W, $h)
    $sombra.Dispose()

    # cuerpo
    $bBlanco = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillRectangle($bBlanco, $X, $Y, $W, $h)
    $bBlanco.Dispose()

    # cabecera
    $bCab = New-Object System.Drawing.SolidBrush($Color)
    $g.FillRectangle($bCab, $X, $Y, $W, $altoCab)
    $bCab.Dispose()

    $pBorde = New-Object System.Drawing.Pen($colBorde, 1)
    $g.DrawRectangle($pBorde, $X, $Y, $W, $h)
    $pBorde.Dispose()

    $bTitulo = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString($Nombre, $fTabla, $bTitulo, ($X + 9), ($Y + 6))
    $bTitulo.Dispose()

    $bTexto = New-Object System.Drawing.SolidBrush($colTexto)
    $bSuave = New-Object System.Drawing.SolidBrush($colSuave)
    $y = $Y + $altoCab + 4

    foreach ($campo in $Campos) {
        $esClave = $campo.StartsWith('PK') -or $campo.StartsWith('FK') -or $campo.StartsWith('UQ')
        $fuente = if ($esClave) { $fCampoPk } else { $fCampo }
        $brocha = if ($esClave) { $bTexto } else { $bSuave }
        $g.DrawString($campo, $fuente, $brocha, ($X + 9), $y)
        $y += $altoFila
    }

    if ($Nota) {
        $bNota = New-Object System.Drawing.SolidBrush($Color)
        $g.DrawString($Nota, $fRel, $bNota, ($X + 9), ($y + 1))
        $bNota.Dispose()
    }

    $bTexto.Dispose()
    $bSuave.Dispose()

    $mapa[$Clave] = @{ X = $X; Y = $Y; W = $W; H = $h }
}

# ============================================================ dibujar relacion
function Draw-Relacion {
    param([string]$Desde, [string]$Hasta, [string]$Etiqueta = '', [int]$Desvio = 0)

    $a = $mapa[$Desde]
    $b = $mapa[$Hasta]

    $ax = $a.X + $a.W / 2
    $ay = $a.Y + $a.H / 2
    $bx = $b.X + $b.W / 2
    $by = $b.Y + $b.H / 2

    # se sale por el lado mas cercano
    if ([Math]::Abs($bx - $ax) -ge [Math]::Abs($by - $ay)) {
        if ($bx -gt $ax) { $x1 = $a.X + $a.W; $x2 = $b.X } else { $x1 = $a.X; $x2 = $b.X + $b.W }
        $y1 = $ay + $Desvio
        $y2 = $by + $Desvio
    }
    else {
        if ($by -gt $ay) { $y1 = $a.Y + $a.H; $y2 = $b.Y } else { $y1 = $a.Y; $y2 = $b.Y + $b.H }
        $x1 = $ax + $Desvio
        $x2 = $bx + $Desvio
    }

    $pen = New-Object System.Drawing.Pen($colLinea, 1.6)
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $g.DrawLine($pen, [int]$x1, [int]$y1, [int]$x2, [int]$y2)
    $pen.Dispose()

    if ($Etiqueta) {
        $mx = ($x1 + $x2) / 2
        $my = ($y1 + $y2) / 2
        $tam = $g.MeasureString($Etiqueta, $fRel)
        $bFondo = New-Object System.Drawing.SolidBrush($colFondo)
        $g.FillRectangle($bFondo, ($mx - $tam.Width / 2 - 2), ($my - $tam.Height / 2), $tam.Width + 4, $tam.Height)
        $bFondo.Dispose()
        $bTxt = New-Object System.Drawing.SolidBrush($colSuave)
        $g.DrawString($Etiqueta, $fRel, $bTxt, ($mx - $tam.Width / 2), ($my - $tam.Height / 2))
        $bTxt.Dispose()
    }
}

# ==================================================================== titulo
$bT = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(13, 59, 102))
$g.DrawString('SmartEvent AI - Modelo de datos', $fTitulo, $bT, 40, 26)
$bT.Dispose()

$bS = New-Object System.Drawing.SolidBrush($colSuave)
$g.DrawString('Base de datos SmartEventAI   -   11 tablas en 3 esquemas   -   13 claves foraneas   -   35 restricciones CHECK', `
    $fSubtitulo, $bS, 42, 66)
$bS.Dispose()

# ============================================================ esquema seg
Draw-Tabla -Clave 'rol' -Nombre 'seg.Rol' -X 40 -Y 110 -W 250 -Color $colSeg -Campos @(
    'PK IdRol            int identity',
    'UQ Nombre           varchar(20)',
    '   Descripcion      nvarchar(150)',
    '   FechaCreacion    datetime2'
) -Nota 'CHECK: ADMINISTRADOR | COORDINADOR'

Draw-Tabla -Clave 'usuario' -Nombre 'seg.Usuario' -X 40 -Y 260 -W 250 -Color $colSeg -Campos @(
    'PK IdUsuario        int identity',
    'UQ NombreUsuario    varchar(50)',
    '   PasswordHash     varchar(200)',
    '   NombreCompleto   nvarchar(120)',
    'FK IdRol            int',
    '   Estado           bit',
    '   IntentosFallidos tinyint',
    '   BloqueadoHasta   datetime2',
    '   UltimoAcceso     datetime2',
    '   FechaCreacion    datetime2'
) -Nota 'CHECK: PBKDF2-SHA256$...  (nunca texto plano)'

Draw-Tabla -Clave 'intento' -Nombre 'seg.IntentoAcceso' -X 40 -Y 520 -W 250 -Color $colSeg -Campos @(
    'PK IdIntento        bigint identity',
    '   NombreUsuario    varchar(50)',
    '   Exitoso          bit',
    '   Motivo           varchar(60)',
    '   Estacion         nvarchar(80)',
    '   FechaIntento     datetime2'
) -Nota 'Agregada: bloqueo temporal del login'

# ============================================================ esquema evt (catalogos)
Draw-Tabla -Clave 'cliente' -Nombre 'evt.Cliente' -X 360 -Y 110 -W 260 -Color $colEvt -Campos @(
    'PK IdCliente        int identity',
    'UQ Identificacion   varchar(20)',
    '   Nombres          nvarchar(150)',
    '   Email            varchar(150)',
    '   Telefono         varchar(20)',
    '   Estado           bit',
    '   FechaCreacion    datetime2',
    '   FechaModificacion datetime2'
) -Nota 'CHECK de formato de correo'

Draw-Tabla -Clave 'salon' -Nombre 'evt.Salon' -X 360 -Y 340 -W 260 -Color $colEvt -Campos @(
    'PK IdSalon          int identity',
    'UQ Nombre           nvarchar(100)',
    '   Ubicacion        nvarchar(150)',
    '   Capacidad        int',
    '   TarifaBase       decimal(12,2)',
    '   Estado           bit'
) -Nota 'CHECK: Capacidad > 0, Tarifa >= 0'

Draw-Tabla -Clave 'recurso' -Nombre 'evt.Recurso' -X 360 -Y 530 -W 260 -Color $colEvt -Campos @(
    'PK IdRecurso        int identity',
    'UQ Nombre           nvarchar(100)',
    '   Tipo             nvarchar(40)',
    '   StockTotal       int',
    '   PrecioUnitario   decimal(12,2)',
    '   Estado           bit'
) -Nota 'CHECK: Stock >= 0, Precio >= 0'

# ============================================================ cabecera
Draw-Tabla -Clave 'reserva' -Nombre 'evt.Reserva   (CABECERA)' -X 700 -Y 200 -W 320 -Color $colEvt -Campos @(
    'PK IdReserva                 int identity',
    'UQ Codigo                    varchar(24)',
    'FK IdCliente                 int',
    'FK IdSalon                   int',
    '   FechaEvento               date',
    '   HoraInicio                time(0)',
    '   HoraFin                   time(0)',
    '   NumeroInvitados           int',
    '   Estado                    varchar(12)',
    '   Subtotal                  decimal(12,2)',
    '   PorcentajeDescuentoGlobal decimal(5,2)',
    '   Descuento                 decimal(12,2)',
    '   Impuesto                  decimal(12,2)',
    '   Total                     decimal(12,2)',
    '   Observacion               nvarchar(500)',
    'FK IdUsuarioCreacion         int',
    '   FechaCreacion             datetime2',
    'FK IdUsuarioModificacion     int',
    '   FechaModificacion         datetime2'
) -Nota 'CHECK: HoraFin > HoraInicio, duracion 2-12 h'

# ============================================================ detalle y auditorias
Draw-Tabla -Clave 'detalle' -Nombre 'evt.ReservaDetalle   (DETALLE)' -X 1120 -Y 110 -W 320 -Color $colEvt -Campos @(
    'PK IdDetalle           int identity',
    'FK IdReserva           int   CASCADE',
    'FK IdRecurso           int',
    '   Cantidad            int',
    '   PrecioUnitario      decimal(12,2)',
    '   PorcentajeDescuento decimal(5,2)',
    '   SubtotalLinea       decimal(12,2)'
) -Nota 'UQ (IdReserva, IdRecurso): regla D08'

Draw-Tabla -Clave 'analisis' -Nombre 'evt.AnalisisIA' -X 1120 -Y 330 -W 320 -Color $colEvt -Campos @(
    'PK IdAnalisis                int identity',
    'FK IdReserva                 int   CASCADE',
    '   Proveedor / Modelo        varchar',
    '   PromptVersion             varchar(20)',
    '   RespuestaJson             nvarchar(max)',
    '   NivelRiesgo               varchar(6)',
    '   TokensEntrada / Salida    int',
    '   Exitoso / Error           bit / nvarchar',
    '   EsContingenciaManual      bit',
    '   JustificacionContingencia nvarchar(500)',
    'FK IdUsuario                 int'
) -Nota 'CHECK: ISJSON(RespuestaJson) = 1'

Draw-Tabla -Clave 'auditoria' -Nombre 'evt.ReservaAuditoria' -X 1120 -Y 590 -W 320 -Color $colEvt -Campos @(
    'PK IdAuditoria      int identity',
    'FK IdReserva        int   CASCADE',
    '   EstadoAnterior   varchar(12)',
    '   EstadoNuevo      varchar(12)',
    '   Motivo           nvarchar(500)',
    'FK IdUsuario        int',
    '   Fecha            datetime2'
) -Nota 'Agregada: traza de cambios de estado'

Draw-Tabla -Clave 'correo' -Nombre 'com.CorreoEnviado' -X 1120 -Y 790 -W 320 -Color $colCom -Campos @(
    'PK IdCorreo         int identity',
    'FK IdReserva        int   CASCADE',
    '   Destinatario     varchar(150)',
    '   Asunto           nvarchar(200)',
    '   TipoEvento       varchar(20)',
    '   Intento          smallint',
    '   Estado           varchar(10)',
    '   Error            nvarchar(500)',
    '   ServidorSmtp     varchar(120)',
    'FK IdUsuario        int'
) -Nota 'Solo host y puerto. NUNCA credenciales.'

# ================================================================ relaciones
Draw-Relacion -Desde 'usuario'   -Hasta 'rol'      -Etiqueta 'IdRol'
Draw-Relacion -Desde 'reserva'   -Hasta 'cliente'  -Etiqueta 'IdCliente' -Desvio -60
Draw-Relacion -Desde 'reserva'   -Hasta 'salon'    -Etiqueta 'IdSalon'
Draw-Relacion -Desde 'detalle'   -Hasta 'reserva'  -Etiqueta '1 : N'
Draw-Relacion -Desde 'analisis'  -Hasta 'reserva'  -Etiqueta '1 : N'
Draw-Relacion -Desde 'auditoria' -Hasta 'reserva'  -Etiqueta '1 : N'
Draw-Relacion -Desde 'correo'    -Hasta 'reserva'  -Etiqueta '1 : N'
Draw-Relacion -Desde 'detalle'   -Hasta 'recurso'  -Etiqueta 'IdRecurso' -Desvio 150

# =================================================================== leyenda
$yL = 1060
$bL = New-Object System.Drawing.SolidBrush($colTexto)
$g.DrawString('Leyenda', (New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)), $bL, 40, $yL)
$bL.Dispose()

$leyendas = @(
    @{ C = $colSeg; T = 'esquema seg  -  seguridad: usuarios, roles e intentos de acceso' },
    @{ C = $colEvt; T = 'esquema evt  -  negocio: catalogos, reservas cabecera-detalle y analisis de IA' },
    @{ C = $colCom; T = 'esquema com  -  comunicaciones: auditoria de correo enviado' }
)

$x = 130
foreach ($l in $leyendas) {
    $b = New-Object System.Drawing.SolidBrush($l.C)
    $g.FillRectangle($b, $x, ($yL + 24), 14, 14)
    $b.Dispose()
    $bt = New-Object System.Drawing.SolidBrush($colSuave)
    $g.DrawString($l.T, $fLeyenda, $bt, ($x + 20), ($yL + 22))
    $bt.Dispose()
    $x = 130
    $yL += 22
}

$bN = New-Object System.Drawing.SolidBrush($colSuave)
$g.DrawString('PK = clave primaria    FK = clave foranea    UQ = restriccion unica    CASCADE = ON DELETE CASCADE', `
    $fLeyenda, $bN, 130, ($yL + 26))
$g.DrawString('Tipo tabla (TVP): evt.ReservaDetalleTipo, con PRIMARY KEY sobre IdRecurso para impedir recursos repetidos.', `
    $fLeyenda, $bN, 130, ($yL + 46))
$bN.Dispose()

# ==================================================================== guardar
$bmp.Save($salida, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Output "Diagrama generado: $salida"
Write-Output ("Tamano: {0:N0} bytes  ({1} x {2} px)" -f (Get-Item $salida).Length, $ancho, $alto)
