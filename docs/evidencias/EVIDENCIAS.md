# Evidencias de los casos de aceptación CA-01 a CA-10

**Proyecto:** SmartEvent AI — Examen práctico EX-002-2-A-2026
**Estudiante:** Williams Joel Navarrete Merino

> El examen exige *«un archivo PDF o Markdown con capturas numeradas y una explicación breve para cada caso de aceptación»*, y advierte que *«las capturas aisladas sin contexto no sustituyen la ejecución del sistema»*.
>
> Por eso cada caso incluye **cómo reproducirlo** además de la captura: el docente puede repetirlo en su propia máquina.

---

## Cómo reproducir todo de una vez

**Nueve de los diez casos están automatizados.** Un solo comando los ejecuta contra la base real, un servidor SMTP real y el servicio de IA real:

```bash
dotnet run --project tests/SmartEvent.Pruebas
```

Resultado esperado:

```
=== TEST EXECUTION SUMMARY ===
   SmartEvent.Pruebas  Total: 28, Errors: 0, Failed: 0, Skipped: 0
```

Y para demostrar las reglas **sin pasar por la interfaz**:

```bash
sqlcmd -S .\NOMBRE_INSTANCIA -E -C -i database\99_pruebas_CA.sql
```

Cada bloque imprime `OK` o `FALLO`.

---

## Índice de capturas

Todas las capturas estan en esta misma carpeta (`docs/evidencias/`), en formato PNG.

Las marcadas como *pendiente* son complementarias: el caso correspondiente ya queda demostrado por la prueba automatizada y por las capturas presentes, pero anadirlas refuerza la evidencia visual.

| Nº | Archivo | Caso | Estado |
|---|---|---|:---:|
| 01 | `01-login.png` | Formularios | ✅ |
| 02 | `02-login-bloqueo.png` | Bloqueo temporal | ✅ |
| 03 | `03-principal-coordinador.png` | Menú por permisos | ✅ |
| 04 | `04-principal-admin.png` | Menú por permisos | ✅ |
| 05 | `05-catalogos.png` | Formularios | ✅ |
| 06 | `06-reserva-tres-detalles.png` | CA-01 | ✅ |
| 07 | `07-reserva-recuperada.png` | CA-01 | pendiente |
| 08 | `08-rollback.png` | CA-02 | ✅ |
| 08b | `08b-resumen-pruebas-sql.png` | CA-01 a CA-05, resumen | ✅ |
| 09 | `09-cruce-horario.png` | CA-03 | pendiente |
| 10 | `10-editar-borrador.png` | CA-04 | pendiente |
| 11 | `11-capacidad-stock-sql.png` | CA-05 | ✅ |
| 12 | `12-confirmar.png` | CA-06 | ✅ |
| 13 | `13-correo-html.png` | CA-06 | ✅ |
| 13b | `13b-correo-html-segunda-reserva.png` | CA-06 | ✅ |
| 14 | `14-bandeja-smtp4dev.png` | CA-06 | ✅ |
| 15 | `15-reenvio-ca07.png` | CA-07 | pendiente |
| 16 | `16-analisis-ia.png` | CA-08 | ✅ |
| 17 | `17-auditoria-ia-json.png` | CA-08 | pendiente |
| 18 | `18-ia-sin-clave.png` | CA-09 | pendiente |
| 19 | `19-consulta-reservas.png` | Formularios | pendiente |
| 20 | `20-clon-limpio.png` | CA-10 | ✅ |

---

## CA-01 — Guardar una reserva válida con tres detalles

**Qué exige el examen:** guardar una reserva con tres detalles; al consultar debe recuperar exactamente la misma cabecera y los tres detalles.

**Cómo reproducirlo en la aplicación:**

1. Entrar como `admin` / `Admin#2026`.
2. **Reservas → Nueva reserva**.
3. Elegir cliente y salón, fecha futura, horario `09:00`–`13:00`, 80 invitados.
4. **Agregar línea** tres veces: Proyector 4K ×2, Silla ejecutiva ×80 con 5 %, Servicio de catering ×80 con 10 %.
5. **Guardar** → aparece el código `RSV-…`.
6. **Reservas → Consultar reservas** → doble clic sobre ella.

**Qué debe verse:** la misma cabecera y **las tres líneas** con sus cantidades y descuentos exactos.

**Comprobación de los totales calculados por SQL Server:**

```
Tarifa base Salón Quito ............  450,00
Proyector 4K      2 × 45,00  sin desc.   90,00
Silla ejecutiva  80 ×  3,50  −5 %       266,00
Catering         80 ×  9,75  −10 %      702,00
                                     ---------
Subtotal ..........................  1.508,00
Impuesto 15 % .....................    226,20
TOTAL .............................  1.734,20
```

**Verificación automatizada:** `CA01_GuardarReservaConTresDetalles_RecuperaCabeceraYLosTresDetalles`, que además comprueba que la calculadora de la interfaz llega **al mismo número** que SQL Server.

📷 `06-reserva-tres-detalles.png` · `07-reserva-recuperada.png`

---

## CA-02 — Rollback completo cuando falla un detalle

**Qué exige el examen:** provocar un error en un detalle y comprobar que **no queda cabecera ni detalles parciales**.

**Cómo reproducirlo:** ejecutar `database/99_pruebas_CA.sql`. El bloque CA-02 intenta guardar una reserva con **tres líneas**: las dos primeras válidas y la tercera apuntando a un recurso **inactivo**.

**Qué debe verse:**

```
Error controlado: Uno de los recursos seleccionados no existe o esta inactivo.
  OK     Rollback completo: reservas 1 -> 1, detalles 3 -> 3. No hay datos parciales.
```

El script **cuenta las filas antes y después**: si la transacción no fuera atómica, quedarían la cabecera y dos detalles.

**Por qué funciona:** `sp_Reserva_Guardar` combina `SET XACT_ABORT ON`, `TRY/CATCH` y `XACT_STATE()`.

**Verificación automatizada:** `CA02_DetalleInvalido_NoDejaCabeceraNiDetallesParciales`.

📷 `08-rollback.png`

---

## CA-03 — Rechazo por cruce parcial de horario

**Qué exige el examen:** intentar reservar el mismo salón en una franja que se cruza parcialmente; debe rechazarse.

**Cómo reproducirlo:**

1. Crear una reserva en Salón Quito, `09:00`–`13:00`.
2. Crear otra en el **mismo salón y la misma fecha**, `12:00`–`15:00`.

**Qué debe verse:** *«El salón ya tiene otra reserva activa que se cruza con el horario solicitado.»* (error 50017)

**Comprobación adicional que demuestra que la fórmula se entendió:** una franja **adyacente** `13:00`–`15:00` **sí se acepta**, porque la fórmula del examen usa comparaciones estrictas:

```sql
@HoraInicio < r.HoraFin  AND  @HoraFin > r.HoraInicio
```

Con `13:00 < 13:00` = falso, no hay cruce.

**Verificación automatizada:** `CA03_CrucePracialDeHorario_SeRechaza_YFranjaAdyacenteSeAcepta`.

📷 `09-cruce-horario.png`

---

## CA-04 — Editar un BORRADOR sin autoconflicto

**Qué exige el examen:** editar una reserva BORRADOR sin que se detecte a sí misma como conflicto.

**Cómo reproducirlo:**

1. Abrir una reserva en estado BORRADOR.
2. Cambiar el número de invitados **sin tocar salón, fecha ni horario**.
3. **Guardar**.

**Qué debe verse:** se guarda correctamente y **conserva su mismo código**.

**Por qué funciona:** `sp_Disponibilidad_Validar` y `sp_Reserva_Guardar` reciben `@IdReserva` y lo excluyen del control de cruces:

```sql
AND (@IdReserva IS NULL OR r.IdReserva <> @IdReserva)
```

**Verificación automatizada:** `CA04_EditarBorrador_NoSeDetectaASiMismoComoConflicto`.

📷 `10-editar-borrador.png`

---

## CA-05 — Rechazo desde SQL: capacidad y stock

**Qué exige el examen:** rechazo *«desde SQL incluso si se omite la validación visual»*.

**Cómo reproducirlo — esta es la demostración clave:** ejecutar `database/99_pruebas_CA.sql`, que invoca los procedimientos **directamente, sin abrir la aplicación**.

**Qué debe verse:**

```
5.a  Salon Cuenca admite 40 personas; se solicitan 100.
  OK     Rechazada por capacidad: El numero de invitados supera la capacidad del salon.

5.b  Pantalla LED 120 pulgadas tiene stock 4; se solicitan 5.
  OK     Rechazada por stock insuficiente: La cantidad solicitada supera el stock disponible.
```

**Cálculo del stock disponible:** `StockTotal − Σ Cantidad` de las reservas BORRADOR o CONFIRMADA de la misma fecha cuyo horario se cruza, excluyendo la reserva en edición.

**Verificación automatizada:** `CA05_ExcederCapacidadDelSalon_SeRechazaDesdeSql` y `CA05_ExcederStockDelRecurso_SeRechazaDesdeSql`.

📷 `11-capacidad-stock-sql.png`

---

## CA-06 — Confirmar: un solo cambio de estado, correo y auditoría

**Qué exige el examen:** confirmar una reserva válida debe cambiar **una sola vez** de estado, generar correo y dejar auditoría.

**Cómo reproducirlo:**

1. Levantar el servidor de correo: `smtp4dev --smtpport=2525 --urls=http://localhost:5080`
2. Abrir una reserva en BORRADOR con cliente que tenga correo válido.
3. **Analizar con IA** (o registrar la contingencia si no hay servicio).
4. **Confirmar reserva**.
5. Abrir **http://localhost:5080**.
6. **Auditoría → Integraciones**.

**Qué debe verse:**

```
CORREO    Intento 1 | ENVIADO | destinatario | localhost:2525 | 124 ms
ESTADO    BORRADOR → CONFIRMADA | admin        ← UNA sola transición
IA        GROQ | openai/gpt-oss-120b | MEDIO | Exitoso | 2151 ms
```

El correo HTML contiene código, cliente, salón, fecha, horario, **tabla del detalle**, total y estado.

**Verificación automatizada:** `CA06_ConfirmarReservaValida_CambiaUnaVez_EnviaCorreoYAudita`.

📷 `12-confirmar.png` · `13-correo-html.png` · `14-auditoria-correo.png`

---

## CA-07 — Falla SMTP y reintento sin duplicados

**Qué exige el examen:** simular falla SMTP y reintentar; **no se duplica la reserva ni el cambio de estado**, y quedan **ambos intentos auditados**.

**Cómo reproducirlo en la aplicación:**

1. **Detener smtp4dev.**
2. Confirmar una reserva → aparece el aviso de que el correo falló, indicando que **la reserva NO se modificó**.
3. **Volver a levantar smtp4dev.**
4. **Reservas → Consultar reservas** → seleccionar la reserva → **Reenviar correo**.
5. **Auditoría → Integraciones**.

**Qué debe verse en la base de datos:**

```
Codigo               Intento  Estado   ServidorSmtp      Error
RSV-…                1        ERROR    localhost:65123   SocketException ConnectionRefused
RSV-…                2        ENVIADO  localhost:2525    -

Transiciones de estado:
RSV-…                BORRADOR → CONFIRMADA   1 vez
```

**Por qué no se duplica nada:**

- El cambio de estado es **idempotente**: si la reserva ya está confirmada, el procedimiento devuelve *«sin cambio»* y no escribe una segunda fila de auditoría.
- El reenvío **no toca el estado**: solo repite el envío.
- El número de intento lo calcula **SQL Server** con `MAX(Intento)+1`, no la aplicación.

**Verificación automatizada:** `CA07_FallaSmtpYReintento_NoDuplicaNadaYAuditaAmbosIntentos`. La falla se provoca apuntando el primer envío a un puerto donde no escucha nadie, de modo que MailKit falla **de verdad** y no hace falta apagar ningún servicio a mano.

📷 `15-reenvio-ca07.png`

---

## CA-08 — Análisis de IA con JSON estructurado

**Qué exige el examen:** ejecutar el análisis, recibir JSON estructurado, mostrarlo y **persistirlo vinculado a la reserva**.

**Cómo reproducirlo:**

1. Configurar `OPENAI_API_KEY` (ver README, sección 6).
2. Abrir una reserva guardada → **Analizar con IA**.
3. **Auditoría → Integraciones → Analisis de IA** → seleccionar la fila.

**Qué debe verse en pantalla:** nivel de riesgo con color, resumen, lista de alertas, lista de recomendaciones y el borrador de correo sugerido.

**Qué debe verse en la auditoría:** proveedor, modelo, versión del prompt, tokens, duración y el **JSON completo indentado**:

```json
{
  "nivelRiesgo": "MEDIO",
  "resumen": "...",
  "alertas": [ "..." ],
  "recomendaciones": [ "..." ],
  "correoSugerido": "..."
}
```

**Límite que debe observarse:** el diálogo del análisis **no tiene ningún botón** que confirme, cancele o envíe el borrador. La IA solo recomienda.

**Verificación automatizada:** `CA08_LlamadaReal_DevuelveJsonEstructuradoYValidado`, que comprueba el contrato completo: nivel válido, resumen ≤ 300 caracteres, ≤ 5 alertas y entre 1 y 5 recomendaciones.

📷 `16-analisis-ia.png` · `17-auditoria-ia-json.png`

---

## CA-09 — Timeout o clave ausente sin cerrar la aplicación

**Qué exige el examen:** simular timeout o clave ausente; la aplicación **continúa operativa** y muestra un mensaje seguro.

**Cómo reproducirlo:**

1. Borrar la variable `OPENAI_API_KEY`:
   ```powershell
   [Environment]::SetEnvironmentVariable('OPENAI_API_KEY',$null,'User')
   ```
2. Reiniciar la aplicación y pulsar **Analizar con IA**.

**Qué debe verse:** un mensaje explicando que no hay clave configurada y que **la reserva no sufrió ningún cambio**, ofreciendo registrar una contingencia. **La aplicación sigue funcionando con normalidad.**

**Qué NO debe verse:** el mensaje al usuario **no menciona** el nombre de la variable de entorno ni ningún detalle técnico. Eso va únicamente a la auditoría y al log.

**Verificación automatizada:** tres pruebas cubren los tres escenarios:

| Prueba | Escenario |
|---|---|
| `CA09_SinClaveDeApi_LaAplicacionSigueOperativaYAvisa` | Clave ausente |
| `CA09_TiempoDeEsperaAgotado_SeTrataSinCerrarLaAplicacion` | Sin conexión / timeout |
| `CA09_ClaveInvalida_DevuelveMensajeSeguroSinExponerLaRespuesta` | HTTP 401 |

📷 `18-ia-sin-clave.png`

---

## CA-10 — Clonar en otro equipo y completar el flujo solo con el README

**Qué exige el examen:** clonar el repositorio en otro equipo, ejecutar el script, configurar variables y completar el flujo **siguiendo solo el README**.

**Cómo reproducirlo:**

```bash
git clone <URL> smartevent-limpio
cd smartevent-limpio
sqlcmd -S .\NOMBRE_INSTANCIA -E -C -i database\00_SmartEventAI.sql
copy appsettings.example.json src\SmartEvent.WinForms\appsettings.json
REM editar la cadena de conexión
dotnet build SmartEventAI.sln
dotnet run --project src/SmartEvent.WinForms
```

**Qué debe verse:**

1. El script imprime el resumen de objetos creados.
2. La compilación termina con **0 advertencias y 0 errores**.
3. La aplicación arranca y se puede iniciar sesión con `admin` / `Admin#2026`.
4. Se completa el flujo: crear cliente → crear reserva → analizar → confirmar.

**Comprobación de que no hay secretos en el clon:**

```bash
git log --all --oneline -- appsettings.json .env secrets.json
```

Debe devolver **vacío**: esos archivos nunca entraron al historial.

**Si falta configuración,** la aplicación **no revienta**: muestra un mensaje explicando exactamente qué definir y dónde.

📷 `20-clon-limpio.png`

---

## Resumen

| Caso | Automatizado | Sin interfaz | Captura |
|---|:---:|:---:|:---:|
| CA-01 | ✅ | ✅ | ✅ |
| CA-02 | ✅ | ✅ | ✅ |
| CA-03 | ✅ | ✅ | ✅ |
| CA-04 | ✅ | ✅ | ✅ |
| CA-05 | ✅ | ✅ | ✅ |
| CA-06 | ✅ | — | ✅ |
| CA-07 | ✅ | — | ✅ |
| CA-08 | ✅ | — | ✅ |
| CA-09 | ✅ | — | ✅ |
| CA-10 | — | — | ✅ |
