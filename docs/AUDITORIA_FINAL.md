# Auditoría final contra el enunciado del examen

**Proyecto:** SmartEvent AI — EX-002-2-A-2026
**Estudiante:** Williams Joel Navarrete Merino

Comparación de cada requisito del enunciado con lo entregado, indicando **dónde está** y **cómo se comprueba**.

---

## 1. Tecnologías obligatorias y prohibiciones

| Requisito | Estado | Dónde | Cómo se comprueba |
|---|:---:|---|---|
| C# sobre .NET 8 | ✅ | `Directory.Build.props`, todos los `.csproj` | `net8.0` y `net8.0-windows` |
| Windows Forms | ✅ | `src/SmartEvent.WinForms` | `UseWindowsForms=true`, `OutputType=WinExe` |
| SQL Server como única persistencia | ✅ | `database/00_SmartEventAI.sql` | No existe ningún archivo JSON ni lista como almacén |
| `Microsoft.Data.SqlClient` con parámetros tipados | ✅ | `Infraestructura/Datos/*` | `comando.Agregar(nombre, SqlDbType, tamaño, valor)` |
| `async`/`await` y `CancellationToken` | ✅ | Toda la solución | `grep -rn "\.Result\|\.Wait()\|Thread.Sleep" src/` → sin resultados |
| MailKit | ✅ | `Integraciones/Correo/ServicioCorreoMailKit.cs` | Única clase que abre SMTP |
| Responses API con JSON Schema | ✅ | `Integraciones/Ia/*` | `text.format.type = json_schema`, `strict: true` |
| Sin ASP.NET, web, WPF ni consola principal | ✅ | — | El único ejecutable de aplicación es WinForms |
| Sin SQL concatenado | ✅ | — | Todos los comandos son `CommandType.StoredProcedure` |
| Sin contraseñas en texto plano | ✅ | `Dominio/Seguridad/HashContrasena.cs` | `CHECK CK_Usuario_PasswordHash` lo impide en el motor |
| Sin claves ni cadenas reales en Git | ✅ | `.gitignore` (primer commit) | `git log --all -- appsettings.json .env` → vacío |
| Compila desde clon limpio | ✅ | — | Verificado: clon nuevo → 0 advertencias, 0 errores |

---

## 2. Modelo de datos

Las **9 tablas obligatorias** con todos sus campos mínimos, más **2 agregadas** que el enunciado permite.

| Tabla | Estado | Observación |
|---|:---:|---|
| `seg.Rol` | ✅ | `Nombre` UNIQUE + CHECK de valores |
| `seg.Usuario` | ✅ | + `IntentosFallidos`, `BloqueadoHasta` para el bloqueo temporal |
| `evt.Cliente` | ✅ | `Identificacion` UNIQUE, CHECK de formato de correo |
| `evt.Salon` | ✅ | `Nombre` UNIQUE, CHECK de capacidad y tarifa |
| `evt.Recurso` | ✅ | `Nombre` UNIQUE, CHECK de stock y precio |
| `evt.Reserva` | ✅ | + `PorcentajeDescuentoGlobal` (decisión documentada) |
| `evt.ReservaDetalle` | ✅ | UNIQUE `(IdReserva, IdRecurso)` = regla D08 en el motor |
| `evt.AnalisisIA` | ✅ | + `Proveedor`, `EsContingenciaManual`, `JustificacionContingencia` |
| `com.CorreoEnviado` | ✅ | + `TipoEvento`, `Intento`. Sin credenciales |
| `evt.ReservaAuditoria` | ➕ | Agregada: traza de cambios de estado |
| `seg.IntentoAcceso` | ➕ | Agregada: auditoría del bloqueo temporal |

**Totales del script:** 11 tablas · 35 restricciones CHECK · 13 claves foráneas · 18 índices propios · 1 TVP · 1 SEQUENCE · 20 procedimientos.

---

## 3. Procedimientos almacenados obligatorios

| Procedimiento | Estado | Verificación |
|---|:---:|---|
| `evt.sp_Reserva_Guardar` | ✅ | TVP + transacción única. Prueba `CA01`, `CA02` |
| `evt.sp_Reserva_Consultar` | ✅ | Filtros combinables sin SQL dinámico + paginación |
| `evt.sp_Reserva_ObtenerPorId` | ✅ | Dos conjuntos de resultados |
| `evt.sp_Reserva_CambiarEstado` | ✅ | Transiciones + idempotencia. Prueba `Estados_ConfirmarDosVeces…` |
| `evt.sp_Disponibilidad_Validar` | ✅ | Cruces + stock, excluyendo la propia reserva. Prueba `CA04` |
| `seg.sp_Usuario_Autenticar` | ✅ | Dos fases, el hash no sale del motor. 3 pruebas de autenticación |

---

## 4. Reglas de negocio

Las 25 reglas del enunciado. La columna **SQL** indica si la regla está protegida en el motor, no solo en la interfaz.

| Regla | SQL | Dónde |
|---|:---:|---|
| D01 `HoraFin > HoraInicio` | ✅ | `CHECK CK_Reserva_Horario` |
| D02 Duración 2–12 h | ✅ | `CHECK CK_Reserva_Duracion` |
| D03 Invitados > 0 | ✅ | `CHECK CK_Reserva_Invitados` |
| D04 Invitados ≤ capacidad | ✅ | `sp_Reserva_Guardar` → 50016 |
| D05/D06 Cruce con la fórmula exacta | ✅ | `sp_Reserva_Guardar` con `UPDLOCK` → 50017 |
| D07 Al menos un detalle | ✅ | `sp_Reserva_Guardar` → 50012 |
| D08 Recurso no repetido | ✅ | `UNIQUE` + PK del TVP |
| D09 Cantidad > 0 | ✅ | `CHECK` + PK del TVP |
| D10 Cantidad ≤ stock disponible | ✅ | `sp_Reserva_Guardar` → 50018 |
| D11 Precio ≥ 0 | ✅ | `CHECK CK_ReservaDetalle_Precio` |
| D12 Descuento 0–20 % | ✅ | `CHECK` en tabla y en TVP |
| D13 > 10 % solo ADMINISTRADOR | ✅ | `sp_Reserva_Guardar` consulta el rol → 50019 |
| D14 Totales en la interfaz | — | `CalculadoraTotales` + `FrmReservaEdicion` |
| D15 Totales en SQL, fuente definitiva | ✅ | `sp_Reserva_Guardar` recalcula siempre |
| D16 Subtotal = tarifa + Σ líneas | ✅ | `sp_Reserva_Guardar` |
| D17 Impuesto 15 % tras descuento global | ✅ | `sp_Reserva_Guardar` |
| D18 Total = base neta + impuesto | ✅ | `sp_Reserva_Guardar` |
| D19 CONFIRMADA no se edita | ✅ | `sp_Reserva_Guardar` → 50011 |
| D20 Confirmar exige email válido | ✅ | `sp_Reserva_CambiarEstado` → 50023 |
| D21 Confirmar exige disponibilidad vigente | ✅ | `sp_Reserva_CambiarEstado` → 50017 |
| D22 Confirmar exige IA o contingencia | ✅ | `sp_Reserva_CambiarEstado` → 50022 |
| D23 Cancelar exige motivo ≥ 20 | ✅ | `sp_Reserva_CambiarEstado` → 50021 |
| D24 FINALIZADA y CANCELADA terminales | ✅ | `sp_Reserva_CambiarEstado` → 50020 |
| D25 Mensajes sin SQL ni trazas | ✅ | `TraductorErroresSql` + `AyudasUi.EjecutarAsync` |

---

## 5. Formularios

| Formulario | Comportamientos exigidos | Estado |
|---|---|:---:|
| `FrmLogin` | Autenticación · bloqueo temporal · mensajes seguros · apertura por rol | ✅ |
| `FrmPrincipal` | MDI · menú por permisos · usuario visible · cierre de sesión · conectividad | ✅ |
| `FrmCatalogos` | CRUD ×3 · filtros · validaciones · duplicados · inactivación lógica | ✅ |
| `FrmReservaEdicion` | Cabecera · búsquedas · grilla editable · cálculo en tiempo real · guardar · IA · confirmar · cancelar | ✅ |
| `FrmReservasConsulta` | Filtros combinados · paginación · doble clic · estados por color · async con cancelación | ✅ |
| `FrmAuditoriaIntegraciones` | Correos · análisis IA · filtros · errores técnicos sin secretos | ✅ |

Además: `FrmAnalisisIa` (muestra el análisis, **sin botones que actúen**) y `FrmTextoRequerido` (motivo y justificación con contador).

---

## 6. Arquitectura

| Requisito | Estado | Comprobación |
|---|:---:|---|
| Cinco capas separadas | ✅ | 6 proyectos, dependencias hacia adentro |
| Presentación sin SQL, sin cadenas, sin SMTP/OpenAI | ✅ | `SmartEvent.Aplicacion` no referencia `SqlClient` ni `MailKit` |
| `IDisposable` / `await using` | ✅ | Conexiones, comandos y lectores |
| Sin conexión global abierta | ✅ | `FabricaConexionSql` no guarda ninguna |
| Inyección de dependencias | ✅ | `Microsoft.Extensions.DependencyInjection` con `ValidateOnBuild` |
| Servicios dependen de abstracciones | ✅ | Todos los contratos en `Aplicacion/Contratos` |
| Manejo centralizado de excepciones | ✅ | `Program.cs` instala 3 capturas |
| Logging local seguro | ✅ | `RegistradorArchivo` con redacción obligatoria |

---

## 7. Correo y OpenAI

| Requisito | Estado | Verificación |
|---|:---:|---|
| Correo HTML al confirmar y cancelar | ✅ | Prueba `Correo_CuerpoHtml_IncluyeTodosLosDatos…` |
| Tabla HTML del detalle | ✅ | Idem |
| Codificación de valores | ✅ | Prueba `Correo_NombreConEtiquetasHtml_SeCodifica…` |
| Auditoría ENVIADO / ERROR | ✅ | `com.CorreoEnviado` |
| Timeout y cancelación | ✅ | `CancellationTokenSource` enlazado |
| Credenciales fuera de Git | ✅ | Solo `host:puerto` se persiste |
| Reintento sin duplicar | ✅ | Prueba `CA07` |
| Responses API encapsulada | ✅ | `ServicioAnalisisIaResponses`, única clase con HTTP |
| Clave desde `OPENAI_API_KEY` | ✅ | `ContenedorServicios.RegistrarOpciones` |
| JSON Schema estricto | ✅ | `EsquemaAnalisisIa.ConstruirEsquema()` |
| Contrato validado tras deserializar | ✅ | `ResultadoAnalisisIa.EsValido` |
| 11 escenarios de error controlados | ✅ | 3 pruebas `CA09` |
| Auditoría con modelo y resultado | ✅ | `evt.AnalisisIA` |
| La IA no puede actuar | ✅ | El servicio no recibe ningún repositorio |

---

## 8. Casos de aceptación

| Caso | Automatizado | Sin interfaz |
|---|:---:|:---:|
| CA-01 a CA-05 | ✅ | ✅ `99_pruebas_CA.sql` |
| CA-06, CA-07 | ✅ SMTP real | — |
| CA-08 | ✅ llamada real | — |
| CA-09 | ✅ 3 escenarios | — |
| CA-10 | Verificado con clon limpio | — |

**Estado de las pruebas:** 28 de 28 en verde, ejecutadas dos veces seguidas para comprobar que la batería es repetible.

---

## 9. Entrega

| Requisito | Estado |
|---|:---:|
| Solución `.sln` y nombres coherentes | ✅ |
| Sin `bin`, `obj`, `.vs` | ✅ verificado con `git ls-files` |
| `database/00_SmartEventAI.sql` | ✅ |
| README completo | ✅ 18 secciones |
| `docs/modelo-datos.png` | ✅ |
| `appsettings.example.json` y `.env.example` sin secretos | ✅ |
| `.gitignore` antes del primer commit con configuración | ✅ es el commit #1 |
| Mínimo 10 commits en 3 momentos | ✅ 14 commits |
| Etiqueta `v1.0.0` | ✅ |
| `docs/USO_IA.md` honesto | ✅ 10 defectos documentados |
| `/docs/evidencias` con explicación por caso | ✅ |

---

## 10. Lo que depende del estudiante

Estas tareas **no se pueden automatizar** y quedan pendientes de ejecución manual:

| Tarea | Dónde |
|---|---|
| Capturas numeradas de los formularios y de los 10 casos | `docs/capturas/` y `docs/evidencias/` |
| Crear el repositorio privado en GitHub y hacer `push` | — |
| Compartir el repositorio con el docente | — |

La estructura, los nombres de archivo y la explicación de cada captura ya están definidos en `docs/evidencias/EVIDENCIAS.md`.
