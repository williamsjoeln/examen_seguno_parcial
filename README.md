# SmartEvent AI

**Sistema inteligente de reservas, recursos y comunicación para eventos corporativos.**

Aplicación de escritorio Windows Forms sobre .NET 8 y SQL Server, con notificación por correo mediante MailKit y análisis de riesgo mediante la Responses API de OpenAI.

> Examen práctico del II parcial, bloque II — **EX-002-2-A-2026**
> Desarrollo e Implementación de Aplicaciones de Escritorio
> Instituto Superior Tecnológico Liceo Cristiano
> **Estudiante:** Williams Joel Navarrete Merino
> **Docente:** Ing. Eduardo Manosalvas Núñez

---

## Índice

1. [Objetivo](#1-objetivo)
2. [Tecnologías](#2-tecnologías)
3. [Arquitectura](#3-arquitectura)
4. [Requisitos previos](#4-requisitos-previos)
5. [Instalación desde cero](#5-instalación-desde-cero)
6. [Configuración](#6-configuración)
7. [Cómo ejecutar](#7-cómo-ejecutar)
8. [Usuarios semilla](#8-usuarios-semilla)
9. [Estructura del proyecto](#9-estructura-del-proyecto)
10. [Funcionalidades](#10-funcionalidades)
11. [Procedimientos almacenados](#11-procedimientos-almacenados)
12. [Transacción cabecera-detalle](#12-transacción-cabecera-detalle)
13. [Seguridad](#13-seguridad)
14. [Integración de correo](#14-integración-de-correo)
15. [Integración con OpenAI](#15-integración-con-openai)
16. [Casos de prueba CA-01 a CA-10](#16-casos-de-prueba-ca-01-a-ca-10)
17. [Pruebas automatizadas](#17-pruebas-automatizadas)
18. [Commit de entrega](#18-commit-de-entrega)

---

## 1. Objetivo

La empresa SmartEvent administra reservas de salones y recursos para eventos corporativos. Antes registraba las solicitudes en hojas de cálculo, lo que provocaba **doble asignación de salones**, **cantidades superiores al inventario**, **totales inconsistentes** y **comunicaciones tardías**.

SmartEvent AI centraliza el proceso completo:

- Cada reserva tiene una **cabecera** (cliente, salón, fecha, horario, estado y valores globales) y **múltiples detalles** (recurso, cantidad, precio y descuento).
- Una reserva solo puede confirmarse si **supera todas las validaciones**, **se guarda atómicamente** y **puede notificarse al cliente**.
- Un análisis con IA evalúa el riesgo operativo antes de confirmar, pero **la decisión siempre es del usuario**.

---

## 2. Tecnologías

| Componente | Versión | Uso |
|---|---|---|
| C# | 12 | Lenguaje |
| .NET | **8.0** | Plataforma (`net8.0` y `net8.0-windows`) |
| Windows Forms | .NET 8 | Interfaz de escritorio |
| SQL Server | 2016 o superior | Única persistencia |
| Microsoft.Data.SqlClient | 6.1.6 | Acceso a datos con parámetros tipados |
| MailKit | 4.17.0 | Envío SMTP |
| OpenAI Responses API | — | Análisis estructurado con JSON Schema |
| xUnit v3 | 4.0.0 | Pruebas de integración |

Todas las operaciones de SQL, correo y OpenAI son **`async`/`await` con `CancellationToken`**. No se usa `.Result`, `.Wait()` ni `Thread.Sleep()` en ninguna parte del código.

---

## 3. Arquitectura

Cinco capas en seis proyectos. Las dependencias apuntan siempre **hacia adentro**:

```
┌──────────────────────────────────────────────────────────────────────┐
│  PRESENTACIÓN            SmartEvent.WinForms      (net8.0-windows)   │
│  6 formularios + contenedor MDI + raíz de composición                │
│  NO contiene SQL, ni cadenas de conexión, ni llamadas a SMTP/OpenAI   │
└───────────────┬──────────────────────────────────────────────────────┘
                │  usa solo interfaces
┌───────────────▼──────────────────────────────────────────────────────┐
│  APLICACIÓN              SmartEvent.Aplicacion          (net8.0)     │
│  Contratos (IReservaRepositorio, IServicioCorreo, IServicioAnalisisIa)│
│  Servicios de caso de uso · DTO · Sesión y permisos · Validador       │
└───────┬───────────────────────────────────────┬──────────────────────┘
        │ implementado por                      │ implementado por
┌───────▼─────────────────────────┐   ┌─────────▼────────────────────────┐
│ INFRAESTRUCTURA                 │   │ INTEGRACIONES                    │
│ SmartEvent.Infraestructura      │   │ SmartEvent.Integraciones         │
│ Única capa con SqlClient        │   │ Única capa con MailKit y HTTP    │
│ Repositorios · Logging seguro   │   │ Correo HTML · Responses API      │
└───────┬─────────────────────────┘   └─────────┬────────────────────────┘
        │                                       │
┌───────▼───────────────────────────────────────▼──────────────────────┐
│  DOMINIO                 SmartEvent.Dominio             (net8.0)     │
│  Entidades · Reglas puras · Calculadora de totales · PBKDF2          │
│  CERO dependencias externas                                          │
└──────────────────────────────────────────────────────────────────────┘

         PRUEBAS   tests/SmartEvent.Pruebas   (xUnit v3, net8.0)
```

**Cómo se garantiza que la presentación no accede a datos:**
el proyecto `SmartEvent.Aplicacion` **no referencia** `Microsoft.Data.SqlClient` ni `MailKit`. No es una convención: es imposible que un formulario abra una `SqlConnection` porque el tipo no existe en su cadena de referencias.

`SmartEvent.WinForms` sí referencia Infraestructura e Integraciones, pero **solo** en `Composicion/ContenedorServicios.cs`, que es la *raíz de composición*: el único punto donde se decide qué implementación concreta recibe cada interfaz.

📄 **Diagrama del modelo de datos:** [`docs/modelo-datos.png`](docs/modelo-datos.png)
🔧 Se regenera con `powershell -ExecutionPolicy Bypass -File docs\generar-modelo-datos.ps1`

---

## 4. Requisitos previos

| Requisito | Cómo comprobarlo |
|---|---|
| **Windows 10/11** | La aplicación es Windows Forms |
| **.NET SDK 8.0 o superior** | `dotnet --version` |
| **SQL Server 2016+** (Express, Developer o superior) | Servicio `MSSQL*` en ejecución |
| **sqlcmd** *(opcional)* | Viene con SQL Server; también sirve SSMS |
| **Git** | `git --version` |

> El proyecto compila con cualquier SDK de .NET 8 o posterior. Está desarrollado y probado con el SDK 10.0.302 compilando para `net8.0`.

**Opcional pero recomendado para las evidencias de correo:**

```bash
dotnet tool install -g Rnwood.Smtp4dev
```

---

## 5. Instalación desde cero

### Paso 1 — Clonar

```bash
git clone <URL-DEL-REPOSITORIO> smartevent
cd smartevent
```

### Paso 2 — Crear la base de datos

El script crea **todo** desde cero: esquemas, tablas, claves, restricciones, índices, secuencia, tipo tabla, datos semilla y los 20 procedimientos almacenados.

```bash
sqlcmd -S .\NOMBRE_INSTANCIA -E -C -i database\00_SmartEventAI.sql
```

Reemplace `NOMBRE_INSTANCIA` por su instancia. Ejemplos: `.\SQLEXPRESS`, `(local)`, `.\MSSQLSERVER_2026`.

> Si prefiere SSMS: abra `database/00_SmartEventAI.sql` y pulse **Ejecutar**.

> ⚠️ **El script elimina la base `SmartEventAI` si ya existe** y la vuelve a crear vacía. Es intencional: el examen exige que sea reproducible desde cero.

Al terminar verá un resumen:

```
Objeto                          Cantidad
------------------------------  --------
Tablas                                11
Procedimientos almacenados            20
Restricciones CHECK                   35
Claves foraneas                       13
Indices no agrupados propios          18
Tipos tabla (TVP)                      1
Usuarios semilla                       2
```

### Paso 3 — Configurar la conexión

Ver la sección [Configuración](#6-configuración).

### Paso 4 — Compilar

```bash
dotnet build SmartEventAI.sln
```

Debe terminar con **0 advertencias y 0 errores**.

---

## 6. Configuración

**Ningún secreto está en el repositorio.** La configuración se lee en este orden, y **lo último gana**:

```
appsettings.json  →  User Secrets  →  Variables de entorno
```

### Opción A — Archivo local (la más sencilla)

Copie la plantilla y edite solo la cadena de conexión:

```bash
copy appsettings.example.json src\SmartEvent.WinForms\appsettings.json
```

Abra `src\SmartEvent.WinForms\appsettings.json` y reemplace `SERVIDOR_EJEMPLO`:

```json
"ConnectionStrings": {
  "SmartEventDb": "Server=.\\SQLEXPRESS;Database=SmartEventAI;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;Connect Timeout=15;Application Name=SmartEventAI"
}
```

> `appsettings.json` está en `.gitignore`: **nunca se sube al repositorio.**
> `Trusted_Connection=True` usa la sesión de Windows, así que **no hay contraseña que pueda filtrarse**.

### Opción B — Variables de entorno

En PowerShell (una línea por variable):

```powershell
[Environment]::SetEnvironmentVariable('ConnectionStrings__SmartEventDb','Server=.\SQLEXPRESS;Database=SmartEventAI;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True','User')
```

Cierre y vuelva a abrir la terminal para que surta efecto.

### Variables disponibles

| Variable | Obligatoria | Descripción |
|---|:---:|---|
| `ConnectionStrings__SmartEventDb` | **Sí** | Cadena de conexión a SQL Server |
| `OPENAI_API_KEY` | No | Clave del servicio de análisis. Sin ella la aplicación funciona igual (caso CA-09) |
| `OpenAI__BaseUrl` | No | Por defecto `https://api.openai.com/v1` |
| `OpenAI__Modelo` | No | Por defecto `gpt-5-mini` |
| `Smtp__Host` | No | Servidor SMTP. Sin él, confirmar y cancelar funcionan pero no se notifica |
| `Smtp__Puerto` | No | Puerto SMTP |
| `Smtp__Usuario`, `Smtp__Password` | No | Solo si el servidor exige autenticación |

Consulte [`appsettings.example.json`](appsettings.example.json) y [`.env.example`](.env.example) para la lista completa. **Ambos contienen únicamente valores ficticios.**

### Configurar el correo para las evidencias

La opción recomendada es **smtp4dev**: un servidor SMTP real que corre en su propia máquina, no pide credenciales y muestra los correos en una bandeja web.

```bash
smtp4dev --smtpport=2525 --urls=http://localhost:5080
```

Y en la configuración:

```json
"Smtp": { "Host": "localhost", "Puerto": 2525, "UsarSsl": false, "Usuario": "", "Password": "" }
```

Los correos aparecen en **http://localhost:5080**.

### Configurar el análisis con IA

La clave se lee de `OPENAI_API_KEY`, tal como exige el examen:

```powershell
[Environment]::SetEnvironmentVariable('OPENAI_API_KEY','sk-SU-CLAVE-AQUI','User')
```

> **Proveedor alternativo:** `OpenAI__BaseUrl` es configurable porque la Responses API la implementan también otros proveedores compatibles. Cambiar de uno a otro **no requiere tocar una sola línea de código**. Este proyecto se desarrolló y probó apuntando a `https://api.groq.com/openai/v1` con el modelo `openai/gpt-oss-120b`, que es un modelo abierto de OpenAI servido por un proveedor con nivel gratuito. El detalle está documentado en [`docs/USO_IA.md`](docs/USO_IA.md).

---

## 7. Cómo ejecutar

```bash
dotnet run --project src/SmartEvent.WinForms
```

O abriendo `SmartEventAI.sln` en Visual Studio y pulsando **F5** con `SmartEvent.WinForms` como proyecto de inicio.

Si falta la cadena de conexión, la aplicación **no revienta**: muestra un mensaje explicando exactamente qué configurar.

---

## 8. Usuarios semilla

Creados por el script de base de datos. Las contraseñas están almacenadas con **PBKDF2-SHA256, 210 000 iteraciones y salt aleatorio por usuario**; nunca en texto plano.

| Usuario | Contraseña | Rol |
|---|---|---|
| `admin` | `Admin#2026` | ADMINISTRADOR |
| `coordinador` | `Coord#2026` | COORDINADOR |

### Matriz de permisos

| Permiso | COORDINADOR | ADMINISTRADOR |
|---|:---:|:---:|
| Crear, editar y consultar reservas | ✅ | ✅ |
| Analizar con IA | ✅ | ✅ |
| Confirmar y cancelar reservas | ✅ | ✅ |
| Mantener clientes, salones y recursos | ❌ | ✅ |
| Ver auditoría de integraciones | ❌ | ✅ |
| Aplicar descuentos superiores al 10 % | ❌ | ✅ |

El examen no define esta matriz; se adoptó la más razonable y está documentada aquí y en `PermisosPorRol`.

> El permiso se comprueba en **tres capas independientes**: el menú no crea la opción, el servicio llama a `SesionUsuario.Exigir(...)`, y el procedimiento almacenado recibe `@IdUsuario` y consulta su rol. Aunque alguien invocara el procedimiento directamente, la regla se cumple.

**Bloqueo temporal:** 3 intentos fallidos bloquean la cuenta 3 minutos. El contador vive en `seg.Usuario`, así que **cerrar y reabrir la aplicación no lo reinicia**. Configurable en la sección `Seguridad`.

---

## 9. Estructura del proyecto

```
SmartEventAI.sln
├── Directory.Build.props            Propiedades comunes a todos los proyectos
├── Directory.Packages.props         Versiones de NuGet centralizadas
├── appsettings.example.json         Plantilla SIN secretos
├── .env.example                     Plantilla de variables SIN secretos
├── .gitignore                       Primer commit del repositorio
│
├── database/
│   ├── 00_SmartEventAI.sql          Crea TODA la base desde cero
│   └── 99_pruebas_CA.sql            Demuestra CA-01..CA-05 sin la interfaz
│
├── docs/
│   ├── modelo-datos.png             Diagrama entidad-relación
│   ├── generar-modelo-datos.ps1     Regenera el diagrama
│   ├── CHECKLIST_EXAMEN.md          Los 141 requisitos rastreados
│   ├── USO_IA.md                    Uso honesto de IA en el desarrollo
│   ├── capturas/                    Capturas de los formularios
│   └── evidencias/                  Evidencias de CA-01 a CA-10
│
├── src/
│   ├── SmartEvent.Dominio/          Entidades, reglas puras, PBKDF2
│   ├── SmartEvent.Aplicacion/       Contratos, servicios, DTO, sesión
│   ├── SmartEvent.Infraestructura/  SqlClient, repositorios, logging
│   ├── SmartEvent.Integraciones/    MailKit y Responses API
│   └── SmartEvent.WinForms/         6 formularios + raíz de composición
│
└── tests/
    └── SmartEvent.Pruebas/          28 pruebas de integración (xUnit v3)
```

---

## 10. Funcionalidades

### Formularios

| Formulario | Qué hace |
|---|---|
| **FrmLogin** | Autenticación en dos fases, bloqueo temporal con cuenta atrás, mensajes que no revelan si el usuario existe |
| **FrmPrincipal** | Contenedor **MDI**, menú construido según permisos, usuario y rol visibles, cierre de sesión, estado de conectividad comprobado cada 30 s |
| **FrmCatalogos** | CRUD de clientes, salones y recursos con búsqueda, filtro de activos, validaciones, detección de duplicados e **inactivación lógica** |
| **FrmReservaEdicion** | Cabecera + grilla editable de detalles, **cálculo en tiempo real**, verificar disponibilidad, guardar, analizar con IA, confirmar, cancelar |
| **FrmReservasConsulta** | Filtros combinados, paginación con *Cargar más*, doble clic para abrir, estados por color, **reenvío de correo auditado** |
| **FrmAuditoriaIntegraciones** | Intentos de correo y análisis de IA con filtros, detalle técnico, JSON indentado y configuración vigente sin secretos |

### Reglas de negocio

Todas las reglas del examen están implementadas y **protegidas en SQL Server**, no solo en la interfaz:

| Regla | Dónde se garantiza |
|---|---|
| `HoraFin > HoraInicio` | `CHECK CK_Reserva_Horario` |
| Duración entre 2 y 12 horas | `CHECK CK_Reserva_Duracion` |
| Invitados > 0 | `CHECK CK_Reserva_Invitados` |
| Invitados ≤ capacidad del salón | `sp_Reserva_Guardar` (error 50016) |
| Sin cruce de horario del mismo salón | `sp_Reserva_Guardar` con `UPDLOCK` (error 50017) |
| Al menos un detalle | `sp_Reserva_Guardar` (error 50012) |
| Recurso no repetido | `UNIQUE (IdReserva, IdRecurso)` + PK del TVP |
| Cantidad > 0, precio ≥ 0 | `CHECK` en `evt.ReservaDetalle` |
| Cantidad ≤ stock disponible | `sp_Reserva_Guardar` (error 50018) |
| Descuento entre 0 y 20 % | `CHECK CK_ReservaDetalle_Descuento` |
| Solo ADMINISTRADOR supera el 10 % | `sp_Reserva_Guardar` consulta el rol (error 50019) |
| Totales recalculados en SQL | `sp_Reserva_Guardar`, siempre |
| CONFIRMADA no se edita | `sp_Reserva_Guardar` (error 50011) |
| Confirmar exige email válido | `sp_Reserva_CambiarEstado` (error 50023) |
| Confirmar exige disponibilidad vigente | `sp_Reserva_CambiarEstado` (error 50017) |
| Confirmar exige análisis IA o contingencia | `sp_Reserva_CambiarEstado` (error 50022) |
| Cancelar exige motivo ≥ 20 caracteres | `sp_Reserva_CambiarEstado` (error 50021) |
| FINALIZADA y CANCELADA son terminales | `sp_Reserva_CambiarEstado` (error 50020) |

### Fórmula de cruce de horarios

Implementada **literalmente** como la especifica el examen:

```sql
AND @HoraInicio < r.HoraFin
AND @HoraFin    > r.HoraInicio
```

Las comparaciones son **estrictas** a propósito: dos franjas adyacentes (una termina a las 13:00 y la otra empieza a las 13:00) **no** se cruzan.

### Cálculo de totales

```
SubtotalLinea = Cantidad × PrecioUnitario × (1 − PorcentajeDescuento/100)
Subtotal      = TarifaBase del salón + Σ SubtotalLinea
Descuento     = Subtotal × PorcentajeDescuentoGlobal/100
BaseNeta      = Subtotal − Descuento
Impuesto      = BaseNeta × 15 %
Total         = BaseNeta + Impuesto
```

Se calcula en **dos lugares**: en la interfaz para verlo en tiempo real (`CalculadoraTotales`) y en SQL Server al guardar. **El valor persistido por el procedimiento es la fuente definitiva**; el formulario ni siquiera envía los totales al guardar, y después recarga la reserva desde la base.

Ambos usan `MidpointRounding.AwayFromZero`, el mismo criterio que la función `ROUND` de SQL Server. Con el redondeo bancario por defecto de .NET, pantalla y base podrían discrepar en un centavo.

### Máquina de estados

```
BORRADOR ──► CONFIRMADA ──► FINALIZADA   (terminal)
   │              │
   └──────────────┴──────► CANCELADA     (terminal)
```

---

## 11. Procedimientos almacenados

Los **seis obligatorios**:

| Procedimiento | Responsabilidad |
|---|---|
| `evt.sp_Reserva_Guardar` | Inserta o actualiza cabecera y sincroniza el detalle **en una sola transacción**, recibiendo el detalle por **TVP**. Devuelve `IdReserva`, `Codigo` y mensaje |
| `evt.sp_Reserva_Consultar` | Filtros opcionales combinables por código, cliente, rango de fechas, salón y estado. **Sin SQL dinámico**, con paginación `OFFSET/FETCH` |
| `evt.sp_Reserva_ObtenerPorId` | Devuelve **dos conjuntos**: cabecera y detalle completo |
| `evt.sp_Reserva_CambiarEstado` | Valida transiciones, registra usuario y fecha, rechaza cambios inválidos. **Es idempotente** |
| `evt.sp_Disponibilidad_Validar` | Detecta cruces de horario y recursos insuficientes, **excluyendo la propia reserva** al editar |
| `seg.sp_Usuario_Autenticar` | Autentica **sin exponer el hash a la interfaz** (ver [Seguridad](#13-seguridad)) |

Además hay **14 procedimientos de apoyo** para catálogos y auditoría, de modo que la aplicación **nunca arma SQL en C#**: todo el acceso a datos pasa por procedimientos con parámetros tipados.

### Catálogo de errores de negocio

Los errores de regla se lanzan con `THROW` y un número `≥ 50000`, con mensajes redactados para el usuario final:

| Número | Significado |
|---|---|
| 50001 | Usuario inválido o inactivo |
| 50010 | La reserva no existe |
| 50011 | La reserva no es editable en su estado actual |
| 50012 | La reserva debe tener al menos un detalle |
| 50013 | Recurso inexistente o inactivo |
| 50014 / 50015 | Cliente / salón inexistente o inactivo |
| 50016 | Invitados superan la capacidad |
| 50017 | Cruce de horario |
| 50018 | Stock insuficiente |
| 50019 | Descuento > 10 % sin rol ADMINISTRADOR |
| 50020 | Transición de estado no permitida |
| 50021 | Motivo de cancelación menor a 20 caracteres |
| 50022 | Falta análisis de IA o contingencia |
| 50023 | El cliente no tiene correo válido |
| 50024 | Duplicado en catálogo |

**Cómo se evita filtrar información técnica:** la capa de datos distingue por el número. Si es `≥ 50000` es un `THROW` nuestro y el mensaje se muestra tal cual. Si es menor, es un error interno del motor y se sustituye por un mensaje genérico; **el detalle real va únicamente al archivo de registro**.

---

## 12. Transacción cabecera-detalle

Es el requisito crítico del examen: *"la cabecera y todos sus detalles deben confirmarse o revertirse juntos"*.

```sql
SET XACT_ABORT ON;                    -- cualquier error aborta la transacción
BEGIN TRY
    BEGIN TRANSACTION;
        ...validaciones de negocio con THROW 50xxx...
        ...INSERT o UPDATE de la cabecera...
        DELETE detalle anterior;
        INSERT detalle desde el TVP;  -- UNA sentencia, no una por fila
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
```

**Tres mecanismos combinados:**

1. `SET XACT_ABORT ON` aborta ante cualquier error del motor.
2. `TRY/CATCH` captura también los errores de negocio que lanzamos con `THROW`.
3. `XACT_STATE()` comprueba que la transacción siga viva antes de revertir.

**El detalle viaja completo en un solo parámetro tipo tabla:**

```csharp
var parametroDetalles = comando.Parameters.AddWithValue("@Detalles", tabla);
parametroDetalles.SqlDbType = SqlDbType.Structured;
parametroDetalles.TypeName  = "evt.ReservaDetalleTipo";
```

El TVP tiene `PRIMARY KEY (IdRecurso)`, así que **un recurso repetido se rechaza antes incluso de entrar al procedimiento**.

No se abre ninguna transacción desde C#: la controla el procedimiento almacenado, como exige el examen.

---

## 13. Seguridad

### Contraseñas

Formato almacenado en `seg.Usuario.PasswordHash`:

```
PBKDF2-SHA256$210000$saltEnBase64$hashEnBase64
```

- **PBKDF2 con 210 000 iteraciones** (recomendación OWASP). SHA-256 a secas es demasiado rápido y favorece la fuerza bruta.
- **Salt aleatorio de 16 bytes por usuario**: sin él, dos usuarios con la misma contraseña tendrían el mismo hash y una tabla precalculada los rompería a la vez.
- **Comparación en tiempo fijo** con `CryptographicOperations.FixedTimeEquals`.
- La restricción `CK_Usuario_PasswordHash` impide **a nivel de motor** insertar una contraseña en texto plano.

### Autenticación en dos fases

El examen exige autenticar *"sin exponer el hash a la interfaz"*. `seg.sp_Usuario_Autenticar` trabaja en dos fases:

| Fase | La aplicación envía | SQL Server devuelve |
|---|---|---|
| **1** | Solo el nombre de usuario | Algoritmo, iteraciones y **salt** — nunca el hash |
| **2** | El hash candidato calculado con ese salt | Compara **dentro del motor** y devuelve el rol, o el rechazo |

**El hash almacenado nunca sale de SQL Server.** Ni siquiera llega a la capa de datos.

Si el usuario **no existe**, la fase 1 devuelve un *salt señuelo* determinista derivado del nombre. Así la aplicación hace exactamente el mismo trabajo criptográfico exista o no la cuenta, y no se puede deducir por la respuesta ni por el tiempo qué usuarios son válidos. Es una protección contra **enumeración de usuarios**.

### Acceso a datos

- **Cero SQL concatenado.** Todos los comandos son `CommandType.StoredProcedure` con parámetros tipados (`SqlDbType` y tamaño explícitos).
- **Ninguna conexión global abierta.** Cada operación abre la suya y la cierra con `await using`, apoyándose en el pool de `Microsoft.Data.SqlClient`. Compartir una `SqlConnection` entre tareas asíncronas concurrentes además rompería la aplicación.

### Secretos

- `.gitignore` es el **primer commit del repositorio**, antes de que existiera ningún archivo de configuración.
- `appsettings.json`, `.env` y `secrets.json` están ignorados.
- Solo se versionan `appsettings.example.json` y `.env.example`, **con valores ficticios**.
- De la configuración SMTP solo se persiste **host y puerto**; de OpenAI, solo **proveedor y modelo**. La clave y la contraseña **jamás tocan la base de datos**.

### Registro local

`RegistradorArchivo` **redacta obligatoriamente** antes de escribir en disco: claves de API (`sk-…`, `gsk_…`, `github_pat_…`), cabeceras `Bearer`, pares `password=`/`token=`/`apikey=` y cadenas de conexión completas. No depende de que quien escriba una línea se acuerde de omitir el secreto.

Los archivos van a `logs/`, uno por día, y se eliminan tras 7 días. La carpeta está en `.gitignore`.

### Manejo de errores

`Program.cs` instala tres capturas: `Application.ThreadException`, `AppDomain.UnhandledException` y `TaskScheduler.UnobservedTaskException`. Ningún error no previsto puede mostrar una traza al usuario ni cerrar la aplicación en silencio.

En la interfaz, `AyudasUi.EjecutarAsync` aplica el criterio:

| Excepción | Qué ve el usuario |
|---|---|
| `ExcepcionNegocio` | El mensaje tal cual — es texto nuestro, seguro |
| `OperationCanceledException` | Nada: canceló él mismo |
| Cualquier otra | Mensaje genérico; **el detalle solo al log** |

---

## 14. Integración de correo

Al **confirmar** o **cancelar** una reserva se envía un correo HTML al cliente.

### Contenido

Código, cliente, salón, fecha, horario, **tabla HTML del detalle** con todos los recursos, subtotal, descuento, impuesto, total y estado. Cuando es una cancelación, incluye el motivo. Se envía también una versión en texto plano.

### Codificación HTML

**Todos** los valores dinámicos pasan por `WebUtility.HtmlEncode`. Un cliente registrado como `<script>alert('x')</script> & Cia` aparece en el correo como `&lt;script&gt;…&amp; Cia`, nunca como etiqueta ejecutable. Hay una prueba automatizada que lo verifica.

### Orden de las operaciones

```
Confirmar reserva
  1. sp_Reserva_CambiarEstado   ← dentro de SU transacción
  2. ¿cambió el estado?  NO → fin (idempotente, no reenvía)
                         SÍ ↓
  3. Enviar el correo           ← FUERA de la transacción
  4. Auditar el intento         ← SIEMPRE, salga bien o mal
```

**Por qué el correo va fuera de la transacción:** si estuviera dentro, un servidor SMTP lento mantendría bloqueada la transacción de SQL Server, y un fallo de red obligaría a deshacer un cambio de estado perfectamente válido.

**Por qué reintentar no duplica nada:** el cambio de estado es **idempotente**. Si la reserva ya está confirmada, el procedimiento devuelve *"sin cambio"* y no escribe una segunda fila de auditoría. El reenvío desde `FrmReservasConsulta` **no toca el estado**: solo vuelve a intentar el envío.

### Auditoría

Cada intento se registra en `com.CorreoEnviado` como `ENVIADO` o `ERROR`, con fecha, duración, servidor (**solo host y puerto**) y mensaje técnico controlado. **El número de intento lo calcula SQL Server** con `MAX(Intento)+1`, no la aplicación, y es independiente por tipo de evento.

Aplica **timeout** con un `CancellationTokenSource` enlazado y respeta la cancelación del usuario. Ningún fallo de red se propaga como excepción: se devuelve un resultado marcado como no exitoso.

---

## 15. Integración con OpenAI

### Cómo funciona

Al pulsar **Analizar con IA** se envía a `POST /v1/responses` únicamente lo necesario de la reserva, pidiendo salida estructurada con **JSON Schema estricto**:

```json
{
  "model": "...",
  "input": [ { "role": "system", ... }, { "role": "user", ... } ],
  "text": {
    "format": { "type": "json_schema", "name": "analisis_riesgo_reserva",
                "strict": true, "schema": { ... } }
  }
}
```

### Contrato de la respuesta

| Campo | Regla |
|---|---|
| `nivelRiesgo` | `BAJO`, `MEDIO` o `ALTO` |
| `resumen` | Máximo 300 caracteres |
| `alertas` | Arreglo de 0 a 5 mensajes |
| `recomendaciones` | Arreglo de 1 a 5 acciones concretas |
| `correoSugerido` | Borrador profesional — **nunca se envía automáticamente** |

### Validación

La respuesta se **deserializa y se vuelve a validar** aunque se haya pedido salida estricta. El esquema garantiza la **forma** (que existan los campos y sean del tipo correcto), pero no los **límites de negocio**: los 300 caracteres del resumen, el máximo de 5 alertas, el mínimo de 1 recomendación. Eso lo comprueba `ResultadoAnalisisIa.EsValido`.

### Minimización de datos

Al modelo se le envía el **nombre** del cliente y nada más: **no** su identificación, **no** su correo, **no** su teléfono. El examen lo pide expresamente.

### Control de errores

Once escenarios controlados, **ninguno lanza excepción a la interfaz**: clave ausente, timeout, sin conexión, 401, 403, 404, 429 (límite de uso), 5xx, respuesta vacía, JSON inválido y rechazo explícito del modelo. La aplicación **sigue operativa** y el usuario ve un mensaje comprensible.

### Auditoría

Cada ejecución se persiste en `evt.AnalisisIA` **salga bien o mal**: proveedor, modelo, versión del prompt, JSON de la respuesta, nivel de riesgo, tokens, duración, éxito y error. **No se guarda la API key.**

### Límites de la IA

> **La IA solo recomienda.** No confirma, no cancela, no modifica totales y no ejecuta SQL. Es literalmente incapaz: `ServicioAnalisisIaResponses` no recibe ningún repositorio. El diálogo que muestra el resultado **no tiene ningún botón que actúe** sobre la reserva.

### Contingencia

Si el análisis no está disponible, el examen contempla *"una justificación manual de contingencia guardada en auditoría"*. La aplicación la ofrece al confirmar: se captura un texto de al menos 20 caracteres y se registra en `evt.AnalisisIA` con `EsContingenciaManual = 1`, junto al usuario y la fecha. Así siempre se puede saber qué reservas se confirmaron sin análisis y por qué.

---

## 16. Casos de prueba CA-01 a CA-10

**Nueve de los diez casos están automatizados.** Se ejecutan con un solo comando:

```bash
dotnet run --project tests/SmartEvent.Pruebas
```

| Caso | Qué demuestra | Cómo verificarlo |
|---|---|---|
| **CA-01** | Guardar reserva con 3 detalles y recuperar exactamente cabecera y detalles | Prueba `CA01_GuardarReservaConTresDetalles…` · también `database/99_pruebas_CA.sql` |
| **CA-02** | Error en un detalle → **no queda cabecera ni detalles parciales** | Prueba `CA02_DetalleInvalido…` cuenta filas antes y después |
| **CA-03** | Cruce parcial de franja → rechazo. Y franja **adyacente** → aceptada | Prueba `CA03_CrucePracialDeHorario…` |
| **CA-04** | Editar un BORRADOR sin que se detecte a sí mismo como conflicto | Prueba `CA04_EditarBorrador…` |
| **CA-05** | Exceder capacidad o stock → **rechazo desde SQL** aunque se omita la interfaz | Prueba `CA05_…` y `99_pruebas_CA.sql` invocando el procedimiento directamente |
| **CA-06** | Confirmar → **una sola** transición + correo + auditoría | Prueba `CA06_ConfirmarReservaValida…` con SMTP real |
| **CA-07** | Falla SMTP + reintento → sin duplicados, **ambos intentos auditados** | Prueba `CA07_FallaSmtpYReintento…` |
| **CA-08** | Análisis IA → JSON estructurado mostrado y **persistido** | Prueba `CA08_LlamadaReal…` (se omite sin clave configurada) |
| **CA-09** | Timeout o clave ausente → la aplicación **sigue operativa** | Tres pruebas `CA09_…`: sin clave, sin conexión y clave inválida |
| **CA-10** | Clonar, ejecutar el script, configurar y completar el flujo **solo con este README** | Siga las secciones 5, 6 y 7 en un equipo limpio |

Las evidencias con capturas están en [`docs/evidencias/`](docs/evidencias/).

### CA-05 sin la interfaz

El examen exige que el rechazo ocurra *"desde SQL incluso si se omite la validación visual"*. Por eso existe `database/99_pruebas_CA.sql`, que invoca los procedimientos **directamente**:

```bash
sqlcmd -S .\NOMBRE_INSTANCIA -E -C -i database\99_pruebas_CA.sql
```

Cada bloque imprime `OK` o `FALLO`.

### CA-07 sin apagar nada a mano

La falla de SMTP se provoca apuntando el primer envío a un puerto donde no escucha nadie: MailKit falla de verdad con `ConnectionRefused`. El reintento apunta al servidor real. Resultado verificable en la base:

```
Codigo               Intento  Estado   ServidorSmtp      Error
RSV-…                1        ERROR    localhost:65123   SocketException ConnectionRefused
RSV-…                2        ENVIADO  localhost:2525    -

Transiciones:        BORRADOR → CONFIRMADA   1 vez
```

---

## 17. Pruebas automatizadas

**28 pruebas de integración** contra la base de datos real, un servidor SMTP real y el servicio de IA real.

```bash
dotnet run --project tests/SmartEvent.Pruebas
```

> Se usa `dotnet run` y no `dotnet test` porque xUnit v3 se ejecuta sobre Microsoft.Testing.Platform, y el SDK 10 de .NET retiró el puente con VSTest. `dotnet run` funciona en cualquier SDK.

Requiere la variable `ConnectionStrings__SmartEventDb`. **Si falta, las pruebas se omiten con un mensaje explicativo en lugar de fallar**, de modo que el proyecto se pueda clonar y compilar sin configurar nada.

La batería es **repetible**: limpia su propia franja de fechas antes de empezar.

| Grupo | Pruebas |
|---|---|
| Autenticación | 3 — rol, contraseña incorrecta, enumeración de usuarios |
| Reservas CA-01…CA-05 | 6 |
| Reglas de negocio | 3 — descuentos, estados terminales, idempotencia |
| Catálogos | 5 — alta, duplicados, inactivación lógica |
| Correo CA-06 y CA-07 | 3 |
| Integraciones | 8 — HTML seguro, SMTP caído, CA-08 y CA-09 |

---

## 18. Commit de entrega

| | |
|---|---|
| **Etiqueta** | `v1.0.0` |
| **Hash corto del commit final** | `PENDIENTE` |

```bash
git checkout v1.0.0
dotnet build SmartEventAI.sln
```

---

## Documentación adicional

| Documento | Contenido |
|---|---|
| [`docs/USO_IA.md`](docs/USO_IA.md) | Uso de IA en el desarrollo: herramientas, prompts, errores detectados y decisiones propias |
| [`docs/CHECKLIST_EXAMEN.md`](docs/CHECKLIST_EXAMEN.md) | Los 141 requisitos del examen, rastreados uno a uno |
| [`docs/evidencias/`](docs/evidencias/) | Capturas numeradas y explicación de CA-01 a CA-10 |
| [`docs/modelo-datos.png`](docs/modelo-datos.png) | Diagrama entidad-relación |
