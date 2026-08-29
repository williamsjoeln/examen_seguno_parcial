# CHECKLIST EXHAUSTIVA — EX-002-2-A-2026 "SmartEvent AI"

Estudiante: Williams Joel Navarrete Merino
Fuente: `Examen_Practico_Windows_Forms_SmartEvent_AI.docx` (5 páginas, leído íntegro)
Estado: FASE 1 — Análisis. 0 / 141 implementados.

## A. TECNOLOGÍAS Y PROHIBICIONES (Ex. §1, §8, §16)

| ID | Requisito | Estado |
|---|---|---|
| A01 | C# / .NET 8 / Windows Forms | [ ] |
| A02 | SQL Server como única persistencia | [ ] |
| A03 | Microsoft.Data.SqlClient con parámetros tipados | [ ] |
| A04 | async/await en SQL, SMTP y OpenAI | [ ] |
| A05 | CancellationToken en operaciones largas | [ ] |
| A06 | MailKit para SMTP | [ ] |
| A07 | OpenAI Responses API + JSON Schema | [ ] |
| A08 | GitHub privado, compartido con el docente | [ ] |
| A09 | PROHIBIDO: ASP.NET / web / WPF / consola principal | [ ] |
| A10 | PROHIBIDO: listas / JSON / archivos como persistencia | [ ] |
| A11 | PROHIBIDO: SQL concatenado | [ ] |
| A12 | PROHIBIDO: password en texto plano | [ ] |
| A13 | PROHIBIDO: API key / connection string real en código | [ ] |
| A14 | PROHIBIDO: `.Result` / `.Wait()` / `Thread.Sleep()` | [ ] |
| A15 | PROHIBIDO: conexión SQL global abierta | [ ] |
| A16 | Compila desde clon limpio siguiendo solo el README | [ ] |

## B. MODELO DE DATOS MÍNIMO (Ex. §4)

| ID | Tabla / campos mínimos | Estado |
|---|---|---|
| B01 | `seg.Rol` — IdRol, Nombre UNIQUE | [ ] |
| B02 | `seg.Usuario` — IdUsuario, NombreUsuario, PasswordHash, IdRol, Estado, FechaCreacion | [ ] |
| B03 | `evt.Cliente` — IdCliente, Identificacion UNIQUE, Nombres, Email, Telefono, Estado | [ ] |
| B04 | `evt.Salon` — IdSalon, Nombre UNIQUE, Capacidad, TarifaBase, Estado | [ ] |
| B05 | `evt.Recurso` — IdRecurso, Nombre UNIQUE, Tipo, StockTotal, PrecioUnitario, Estado | [ ] |
| B06 | `evt.Reserva` — IdReserva, Codigo UNIQUE, IdCliente, IdSalon, FechaEvento, HoraInicio, HoraFin, NumeroInvitados, Estado, Subtotal, Descuento, Impuesto, Total, Observacion, usuario + fechas de auditoría | [ ] |
| B07 | `evt.ReservaDetalle` — IdDetalle, IdReserva, IdRecurso, Cantidad, PrecioUnitario, PorcentajeDescuento, SubtotalLinea | [ ] |
| B08 | `evt.AnalisisIA` — IdAnalisis, IdReserva, Modelo, PromptVersion, RespuestaJson, NivelRiesgo, TokensEntrada/Salida, Fecha, Exitoso, Error | [ ] |
| B09 | `com.CorreoEnviado` — IdCorreo, IdReserva, Destinatario, Asunto, FechaIntento, Estado, Error (sin credenciales) | [ ] |
| B10 | Todas: PK, tipos apropiados, CHECK/UNIQUE, FK, fechas de auditoría | [ ] |
| B11 | Índices pertinentes (Salon+Fecha, Codigo, Estado, FKs) | [ ] |

## C. SCRIPT SQL + PROCEDIMIENTOS ALMACENADOS (Ex. §5)

| ID | Requisito | Estado |
|---|---|---|
| C01 | `/database/00_SmartEventAI.sql` crea todo desde cero, en orden, sin intervención manual | [ ] |
| C02 | Incluye esquemas, tablas, claves, restricciones, índices, semilla y SPs | [ ] |
| C03 | TVP `evt.ReservaDetalleTipo` | [ ] |
| C04 | `evt.sp_Reserva_Guardar` — insert/update cabecera + sincroniza detalles en UNA transacción, recibe TVP, retorna IdReserva/Codigo/Mensaje | [ ] |
| C05 | `evt.sp_Reserva_Consultar` — filtros opcionales combinables, sin SQL dinámico | [ ] |
| C06 | `evt.sp_Reserva_ObtenerPorId` — dos result sets (cabecera + detalle) | [ ] |
| C07 | `evt.sp_Reserva_CambiarEstado` — valida transiciones, registra usuario/fecha, rechaza inválidas | [ ] |
| C08 | `evt.sp_Disponibilidad_Validar` — cruces de horario + recursos insuficientes, excluye la propia reserva | [ ] |
| C09 | `seg.sp_Usuario_Autenticar` — usuario activo + autorización, sin exponer el hash a la UI | [ ] |
| C10 | Atomicidad: si falla un detalle no queda cabecera ni detalles parciales | [ ] |
| C11 | Prohibido un INSERT por fila desde el formulario | [ ] |
| C12 | Datos semilla que permitan iniciar sesión | [ ] |

## D. REGLAS DE NEGOCIO (Ex. §6) — validar en UI **y** en SQL

| ID | Regla | Estado |
|---|---|---|
| D01 | HoraFin > HoraInicio | [ ] |
| D02 | Duración entre 2 y 12 horas | [ ] |
| D03 | NumeroInvitados > 0 | [ ] |
| D04 | NumeroInvitados <= Salon.Capacidad | [ ] |
| D05 | Sin otra reserva BORRADOR/CONFIRMADA del mismo salón+fecha en franja cruzada | [ ] |
| D06 | Fórmula de cruce EXACTA: `inicioNuevo < finExistente AND finNuevo > inicioExistente` | [ ] |
| D07 | Al menos un detalle por reserva | [ ] |
| D08 | Recurso no repetido en el mismo detalle lógico | [ ] |
| D09 | Cantidad > 0 | [ ] |
| D10 | Cantidad <= stock disponible considerando otras reservas activas de la misma fecha/horario | [ ] |
| D11 | PrecioUnitario >= 0 | [ ] |
| D12 | PorcentajeDescuento de línea entre 0 y 20 | [ ] |
| D13 | Solo ADMINISTRADOR puede superar 10% | [ ] |
| D14 | Totales recalculados en la interfaz (tiempo real) | [ ] |
| D15 | Totales recalculados en SQL; SQL es la fuente definitiva | [ ] |
| D16 | Subtotal = TarifaBase del salón + SUM(SubtotalLinea) | [ ] |
| D17 | Impuesto = 15% sobre la base luego del descuento global | [ ] |
| D18 | Total = base neta + impuesto | [ ] |
| D19 | CONFIRMADA no edita cliente / salón / fecha / horario / detalles | [ ] |
| D20 | Confirmar requiere email válido | [ ] |
| D21 | Confirmar requiere disponibilidad vigente | [ ] |
| D22 | Confirmar requiere análisis IA exitoso O justificación de contingencia auditada | [ ] |
| D23 | Cancelar requiere motivo >= 20 caracteres | [ ] |
| D24 | FINALIZADA y CANCELADA son terminales | [ ] |
| D25 | Mensajes comprensibles, sin SQL / connection string / claves / stack traces | [ ] |

## E. MÁQUINA DE ESTADOS

| ID | Transición | Estado |
|---|---|---|
| E01 | BORRADOR → CONFIRMADA → FINALIZADA | [ ] |
| E02 | BORRADOR → CANCELADA | [ ] |
| E03 | CONFIRMADA → CANCELADA | [ ] |
| E04 | Transiciones inválidas rechazadas en `sp_Reserva_CambiarEstado` | [ ] |

## F. WINDOWS FORMS (Ex. §7) — MDI o contenedor con navegación por permisos

| ID | Requisito | Estado |
|---|---|---|
| F01 | FrmLogin: autenticación | [ ] |
| F02 | FrmLogin: bloqueo temporal tras intentos fallidos | [ ] |
| F03 | FrmLogin: mensajes seguros (no revela si el usuario existe) | [ ] |
| F04 | FrmLogin: apertura del menú según rol | [ ] |
| F05 | FrmPrincipal: menú por permisos | [ ] |
| F06 | FrmPrincipal: usuario autenticado visible | [ ] |
| F07 | FrmPrincipal: cierre de sesión | [ ] |
| F08 | FrmPrincipal: estado de conectividad | [ ] |
| F09 | FrmCatalogos: CRUD clientes | [ ] |
| F10 | FrmCatalogos: CRUD salones | [ ] |
| F11 | FrmCatalogos: CRUD recursos | [ ] |
| F12 | FrmCatalogos: búsqueda / filtros | [ ] |
| F13 | FrmCatalogos: validaciones + detección de duplicados | [ ] |
| F14 | FrmCatalogos: activación / inactivación lógica | [ ] |
| F15 | FrmReservaEdicion: cabecera completa | [ ] |
| F16 | FrmReservaEdicion: búsqueda de cliente | [ ] |
| F17 | FrmReservaEdicion: búsqueda de salón | [ ] |
| F18 | FrmReservaEdicion: fecha / hora inicio / hora fin / invitados / observación | [ ] |
| F19 | FrmReservaEdicion: grilla editable de detalles (recurso, cantidad, precio, descuento) | [ ] |
| F20 | FrmReservaEdicion: cálculo en tiempo real | [ ] |
| F21 | FrmReservaEdicion: validaciones | [ ] |
| F22 | FrmReservaEdicion: Guardar | [ ] |
| F23 | FrmReservaEdicion: Analizar con IA | [ ] |
| F24 | FrmReservaEdicion: Confirmar | [ ] |
| F25 | FrmReservaEdicion: Cancelar (con motivo) | [ ] |
| F26 | FrmReservasConsulta: filtros combinados (código, cliente, rango, salón, estado) | [ ] |
| F27 | FrmReservasConsulta: paginación / carga progresiva | [ ] |
| F28 | FrmReservasConsulta: doble clic → detalle | [ ] |
| F29 | FrmReservasConsulta: operaciones asíncronas + CancellationToken | [ ] |
| F30 | FrmReservasConsulta: estados visualmente identificables | [ ] |
| F31 | FrmAuditoriaIntegraciones: intentos de correo | [ ] |
| F32 | FrmAuditoriaIntegraciones: análisis IA | [ ] |
| F33 | FrmAuditoriaIntegraciones: filtros | [ ] |
| F34 | FrmAuditoriaIntegraciones: errores técnicos sin exponer secretos | [ ] |
| F35 | La UI no se congela durante SQL / correo / OpenAI | [ ] |

## G. ARQUITECTURA (Ex. §8)

| ID | Requisito | Estado |
|---|---|---|
| G01 | Capas: Presentación, Dominio, Aplicación, Infraestructura, Integraciones | [ ] |
| G02 | Presentación sin SQL, sin connection string, sin llamadas directas a OpenAI/SMTP | [ ] |
| G03 | `IDisposable` / `await using` en conexiones, comandos y lectores | [ ] |
| G04 | Inyección de dependencias (contenedor) | [ ] |
| G05 | Servicios dependen de abstracciones (interfaces) | [ ] |
| G06 | Manejo centralizado de excepciones | [ ] |
| G07 | Logging local seguro (sin claves, passwords ni datos sensibles) | [ ] |

## H. CORREO SMTP (Ex. §9)

| ID | Requisito | Estado |
|---|---|---|
| H01 | MailKit, correo HTML al confirmar y al cancelar | [ ] |
| H02 | Contenido: código, cliente, salón, fecha, horario, recursos, total, estado | [ ] |
| H03 | Tabla HTML legible del detalle | [ ] |
| H04 | HTML-encoding de todos los valores | [ ] |
| H05 | Registro de cada intento en `com.CorreoEnviado` (ENVIADO / ERROR) | [ ] |
| H06 | Fecha + mensaje técnico controlado | [ ] |
| H07 | Timeout | [ ] |
| H08 | CancellationToken | [ ] |
| H09 | Credenciales por variables de entorno / User Secrets | [ ] |
| H10 | Fallo de correo NO duplica la reserva ni el cambio de estado (idempotencia) | [ ] |
| H11 | Reenvío explícito desde la UI, auditado | [ ] |

## I. OPENAI (Ex. §10)

| ID | Requisito | Estado |
|---|---|---|
| I01 | Responses API encapsulada en un servicio independiente | [ ] |
| I02 | API key desde `OPENAI_API_KEY` o configuración local ignorada por Git | [ ] |
| I03 | Enviar solo los datos necesarios de la reserva | [ ] |
| I04 | JSON Schema / Structured Outputs | [ ] |
| I05 | Contrato: `nivelRiesgo` ∈ {BAJO, MEDIO, ALTO} | [ ] |
| I06 | Contrato: `resumen` <= 300 caracteres | [ ] |
| I07 | Contrato: `alertas` 0..5 | [ ] |
| I08 | Contrato: `recomendaciones` 1..5 | [ ] |
| I09 | Contrato: `correoSugerido` (borrador, nunca se envía automáticamente) | [ ] |
| I10 | Validar + deserializar antes de mostrar o persistir | [ ] |
| I11 | Timeout + CancellationToken + botón cancelar, UI responsiva | [ ] |
| I12 | Manejar rechazo, respuesta vacía, JSON inválido, error HTTP, sin conexión, 429 | [ ] |
| I13 | Persistir auditoría: modelo, resultado, error, tokens | [ ] |
| I14 | No guardar la API key ni datos innecesarios del cliente | [ ] |
| I15 | La IA solo recomienda: no confirma, no cancela, no toca totales, no ejecuta SQL | [ ] |

## J. SEGURIDAD (Ex. §8, §16, rúbrica)

| ID | Requisito | Estado |
|---|---|---|
| J01 | Hash de contraseñas (PBKDF2 con salt por usuario) | [ ] |
| J02 | Cero SQL concatenado | [ ] |
| J03 | Secretos fuera de Git + `.gitignore` correcto antes del primer commit con configuración | [ ] |
| J04 | `appsettings.example.json` / `.env.example` solo con valores ficticios | [ ] |
| J05 | Logging que nunca registra password, API key ni connection string | [ ] |
| J06 | Excepciones controladas, mensajes seguros al usuario | [ ] |

## K. CASOS DE ACEPTACIÓN (Ex. §11)

| ID | Resultado verificable | Estado |
|---|---|---|
| CA-01 | Guardar reserva válida con 3 detalles; al consultar recupera la misma cabecera y los 3 detalles | [ ] |
| CA-02 | Error en un detalle → no queda cabecera ni detalles parciales | [ ] |
| CA-03 | Cruce parcial de franja en el mismo salón → rechazo | [ ] |
| CA-04 | Editar BORRADOR sin autodetectarse como conflicto | [ ] |
| CA-05 | Exceder capacidad o stock concurrente → rechazo desde SQL aunque se omita la UI | [ ] |
| CA-06 | Confirmar válida → un solo cambio de estado + correo + auditoría | [ ] |
| CA-07 | Falla SMTP + reintento → sin duplicados, ambos intentos auditados | [ ] |
| CA-08 | Análisis IA → JSON estructurado mostrado y persistido vinculado a la reserva | [ ] |
| CA-09 | Timeout o API key ausente → app operativa + mensaje seguro | [ ] |
| CA-10 | Clon en otro equipo → script + variables + flujo completo solo con el README | [ ] |

## L. ENTREGA EN GITHUB (Ex. §12)

| ID | Requisito | Estado |
|---|---|---|
| L01 | Solución `.sln` y proyectos con nombres coherentes | [ ] |
| L02 | Sin `bin`, `obj`, `.vs`, paquetes compilados ni temporales | [ ] |
| L03 | `/database/00_SmartEventAI.sql` (+ scripts numerados opcionales) | [ ] |
| L04 | `README.md` completo | [ ] |
| L05 | `docs/modelo-datos.png` o PDF legible | [ ] |
| L06 | `docs`: capturas de los formularios principales | [ ] |
| L07 | `docs`: evidencia de correos y análisis IA con datos ficticios | [ ] |
| L08 | `appsettings.example.json` o `.env.example` sin secretos | [ ] |
| L09 | `.gitignore` correcto antes del primer commit con configuración | [ ] |
| L10 | Mínimo 10 commits reales en >= 3 momentos distintos | [ ] |
| L11 | Tag `v1.0.0` en el commit final | [ ] |
| L12 | README indica el hash corto del commit final | [ ] |
| L13 | `docs/USO_IA.md` honesto (herramientas, prompts, generado, errores, decisiones) | [ ] |
| L14 | `/docs/evidencias` con PDF o MD, capturas numeradas + explicación por cada CA | [ ] |

## M. README — contenido exigido (§12 + petición del estudiante)

| ID | Sección | Estado |
|---|---|---|
| M01 | Descripción y objetivo | [ ] |
| M02 | Tecnologías | [ ] |
| M03 | Arquitectura + diagrama de capas | [ ] |
| M04 | Requisitos previos | [ ] |
| M05 | Instalación desde cero | [ ] |
| M06 | Configuración de SQL Server | [ ] |
| M07 | Configuración de variables de entorno | [ ] |
| M08 | Usuario semilla | [ ] |
| M09 | Cómo ejecutar | [ ] |
| M10 | Estructura del proyecto | [ ] |
| M11 | Explicación de funcionalidades | [ ] |
| M12 | Explicación de seguridad | [ ] |
| M13 | Explicación de OpenAI | [ ] |
| M14 | Explicación del correo | [ ] |
| M15 | Explicación de transacciones | [ ] |
| M16 | Explicación de procedimientos almacenados | [ ] |
| M17 | Diez casos de prueba CA-01..CA-10 | [ ] |
| M18 | Instrucciones de clonado y ejecución | [ ] |
| M19 | Hash corto del commit final | [ ] |
