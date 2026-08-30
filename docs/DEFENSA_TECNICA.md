# Guía de defensa técnica

**Proyecto:** SmartEvent AI — EX-002-2-A-2026
**Estudiante:** Williams Joel Navarrete Merino

> Preguntas que probablemente haga el docente, con la respuesta que debes poder dar **con tus palabras**. Están ordenadas por el peso que tiene cada criterio en la rúbrica.
>
> **Consejo general:** cuando no sepas algo, dilo y explica dónde lo buscarías. Es mucho mejor que inventar. El examen evalúa que entiendas lo que entregaste, no que lo hayas memorizado.

---

## 1. Base de datos y procedimientos almacenados · 2.00 pts

### «¿Por qué toda la lógica está en procedimientos almacenados y no en C#?»

Por tres razones. Primera, el examen lo exige. Segunda, porque así la regla se cumple **aunque no pases por la aplicación**: si alguien invoca el procedimiento desde SSMS, las validaciones siguen ahí. Y tercera, porque el control de concurrencia —que dos usuarios no reserven el mismo salón a la vez— solo se puede hacer bien dentro de la transacción, con bloqueos del motor.

### «Muéstrame que no hay SQL concatenado.»

Todos los comandos se crean con `CommandType.StoredProcedure` en `FabricaConexionSql.CrearComando`. No existe ni un solo `CommandType.Text` en la solución. Y los parámetros se declaran con tipo y tamaño explícitos:

```csharp
comando.Agregar("@Codigo", SqlDbType.VarChar, 24, filtro.Codigo);
```

### «¿Cómo haces los filtros opcionales sin SQL dinámico?»

Cada filtro es un parámetro que puede venir en null, y la condición es:

```sql
WHERE (@Codigo IS NULL OR r.Codigo = @Codigo)
  AND (@IdSalon IS NULL OR r.IdSalon = @IdSalon)
```

Añado `OPTION (RECOMPILE)` para que el optimizador genere un plan adaptado a la combinación que realmente se usó. Ese es el precio razonable por no recurrir a SQL dinámico.

### «¿Qué índices creaste y por qué?»

El más importante es `IX_Reserva_Salon_Fecha` sobre `(IdSalon, FechaEvento)` incluyendo horario y estado, porque la detección de cruces es la consulta más frecuente y más crítica del sistema. Los demás cubren los filtros de la consulta y las pantallas de auditoría. No creé índices "por si acaso": cada uno responde a una consulta real.

### «¿Por qué usas una SEQUENCE para el código de reserva?»

Porque `MAX(Codigo)+1` obligaría a bloquear la tabla para que dos usuarios simultáneos no obtengan el mismo número. Una `SEQUENCE` garantiza unicidad sin bloqueo.

---

## 2. Cabecera-detalle y transacción · 2.00 pts

### «¿Cómo garantizas que cabecera y detalle se guardan juntos o no se guarda nada?»

Con tres mecanismos combinados dentro de `sp_Reserva_Guardar`:

1. `SET XACT_ABORT ON` — cualquier error en tiempo de ejecución aborta la transacción completa.
2. `TRY/CATCH` — captura también los errores de negocio que lanzo yo con `THROW`.
3. `XACT_STATE()` — comprueba que la transacción siga viva antes de hacer `ROLLBACK`.

Y lo puedo demostrar: el bloque CA-02 de `99_pruebas_CA.sql` **cuenta las filas antes y después** de provocar el error. Si no fuera atómico, quedarían la cabecera y dos detalles.

### «¿Qué es un TVP y por qué lo usas?»

Un parámetro tipo tabla. Me permite enviar **todo el detalle en un solo parámetro y una sola llamada**, en lugar de un `INSERT` por fila desde el formulario, que es justo lo que el examen prohíbe. Además le puse `PRIMARY KEY (IdRecurso)` al tipo, así que un recurso repetido se rechaza **antes incluso de entrar al procedimiento**.

### «¿Dónde se abre la transacción?»

En el procedimiento almacenado, no en C#. Desde la aplicación solo hago `ExecuteNonQueryAsync`. Si la abriera en C#, la transacción viviría mientras dura el viaje por la red, y sería más frágil.

### «Si manipulo el formulario para poner un total menor, ¿qué pasa?»

Nada. El procedimiento **ni siquiera recibe los totales como parámetro**. Los recalcula desde la tarifa del salón y las líneas del TVP. Después de guardar, el formulario recarga la reserva desde la base y muestra lo que devolvió SQL Server.

### «Explícame la fórmula de cruce de horarios.»

```sql
@HoraInicio < r.HoraFin  AND  @HoraFin > r.HoraInicio
```

Es la del enunciado, literal. Las comparaciones son **estrictas** a propósito: si una reserva termina a las 13:00 y otra empieza a las 13:00, **no se cruzan**. Tengo una prueba que lo demuestra: la franja adyacente 13:00–15:00 sí se acepta.

### «¿Y cómo evitas que dos usuarios reserven a la vez el mismo salón?»

La comprobación de cruce dentro de `sp_Reserva_Guardar` usa `WITH (UPDLOCK, HOLDLOCK)`. Eso mantiene el bloqueo hasta el final de la transacción, así que el segundo usuario espera y luego ve la reserva del primero.

### «¿Por qué el subtotal de línea se calcula en dos sitios?»

Porque el examen lo pide: en la interfaz para verlo en tiempo real, y en SQL Server porque **es la fuente definitiva**. Un detalle: uso `MidpointRounding.AwayFromZero` porque .NET redondea «al par» por defecto y SQL Server no. Sin eso, la pantalla y la base podrían diferir en un centavo.

---

## 3. Windows Forms y arquitectura · 1.50 pts

### «Demuéstrame que la presentación no accede a la base de datos.»

Abro `SmartEvent.Aplicacion.csproj`: **no referencia `Microsoft.Data.SqlClient` ni `MailKit`**. No es una convención que yo respete: es imposible que un formulario abra una `SqlConnection` porque el tipo no existe en su cadena de referencias. Y si busco `SqlConnection` en la carpeta `Formularios`, no aparece ni un resultado.

### «Pero WinForms sí referencia Infraestructura. ¿No rompe eso la arquitectura?»

No. Eso se llama **raíz de composición**: es el único punto del programa donde se decide qué implementación concreta recibe cada interfaz, y está en un solo archivo, `Composicion/ContenedorServicios.cs`. Los formularios reciben interfaces por constructor.

### «¿Por qué la interfaz no se congela al consultar?»

Porque todas las operaciones son `async`/`await` y ninguna bloquea el hilo de la interfaz. No hay ni un `.Result`, ni un `.Wait()`, ni un `Thread.Sleep()` en toda la solución. Además, cada nueva búsqueda **cancela la anterior** con su `CancellationToken`.

### «¿Cómo manejas los errores no previstos?»

`Program.cs` instala tres capturas: `Application.ThreadException`, `AppDomain.UnhandledException` y `TaskScheduler.UnobservedTaskException`. Y en la interfaz, `AyudasUi.EjecutarAsync` aplica el criterio: si es `ExcepcionNegocio` muestro el mensaje tal cual porque es texto mío; si es cualquier otra, muestro un mensaje genérico y **el detalle va solo al log**.

**Y esto lo he visto funcionar**: durante el desarrollo aparecieron tres errores reales en la interfaz y en los tres casos la aplicación siguió funcionando, avisó al usuario y guardó la traza en el archivo de registro.

### «¿El permiso solo oculta el menú?»

No, son **tres capas independientes**. El menú no crea la opción. El servicio llama a `SesionUsuario.Exigir(...)`. Y el procedimiento almacenado recibe `@IdUsuario` y consulta su rol. Aunque alguien invocara el procedimiento directamente, la regla de que solo un ADMINISTRADOR puede aplicar más del 10 % de descuento se cumple igual.

### «¿Por qué los formularios no se resuelven del contenedor de dependencias?»

Ese fue un defecto que encontré ejecutando. Los registraba como transitorios y los pedía con `GetRequiredService`. El problema es que el contenedor **rastrea los servicios transitorios que implementan `IDisposable`** para liberarlos al final, y un `Form` lo implementa. Consecuencia: guardaba una referencia a cada ventana abierta durante toda la sesión —una fuga de memoria— y la liberaba **por segunda vez** al cerrar la aplicación, lo que reventaba con `ObjectDisposedException`.

Lo resolví con una `FabricaFormularios` que usa `ActivatorUtilities.CreateInstance`: resuelve las dependencias igual, pero el contenedor no se queda con el objeto. El ciclo de vida queda en manos de Windows Forms, que es quien sabe cuándo se cierra una ventana.

---

## 4. Correo · 1.00 pt

### «¿Por qué el correo se envía fuera de la transacción?»

Por dos razones. Si estuviera dentro, un servidor SMTP lento mantendría bloqueada la transacción de SQL Server. Y si el correo fallara, tendría que deshacer un cambio de estado que era perfectamente válido.

### «Entonces si falla el correo y reintento, ¿no se duplica la reserva?»

No, y es la clave del caso CA-07. El cambio de estado es **idempotente**: si la reserva ya está confirmada, el procedimiento devuelve «sin cambio» y no escribe una segunda fila de auditoría. Y el botón de reenvío **no toca el estado**: solo repite el envío.

Lo puedo demostrar en la base: dos filas en `com.CorreoEnviado` —intento 1 en ERROR, intento 2 en ENVIADO— y **una sola** transición en `evt.ReservaAuditoria`.

### «¿Quién calcula el número de intento?»

SQL Server, con `MAX(Intento)+1` dentro del procedimiento. Si lo calculara la aplicación, dos reenvíos simultáneos podrían obtener el mismo número.

### «¿Cómo evitas que se inyecte HTML en el correo?»

Todos los valores dinámicos pasan por `WebUtility.HtmlEncode`. Tengo una prueba que registra un cliente llamado `<script>alert('x')</script> & Cia` y verifica que en el HTML sale `&lt;script&gt;`, nunca la etiqueta ejecutable.

### «¿Por qué usas un servidor SMTP local y no uno real?»

Porque smtp4dev es un servidor SMTP **de verdad**: MailKit hace el handshake completo, no es una simulación. Me permite ver el correo HTML para las evidencias, no necesita credenciales —así que no hay riesgo de publicar una contraseña— y apagarlo es la forma más limpia de demostrar CA-07. La configuración es la misma para cualquier servidor: basta cambiar host y puerto.

---

## 5. OpenAI · 1.25 pts

### «¿Qué es la Responses API y cómo la consumes?»

Es el punto de entrada `POST /v1/responses`. Le envío el modelo, los mensajes y —lo importante— el bloque `text.format` con `type: "json_schema"` y `strict: true`, que obliga al modelo a devolver exactamente la forma que yo defino.

### «Si el esquema es estricto, ¿para qué validas otra vez?»

Porque el esquema garantiza la **forma** —que existan los campos y sean del tipo correcto— pero **no los límites de negocio**: que el resumen no pase de 300 caracteres, que haya entre 1 y 5 recomendaciones, que el nivel sea uno de los tres válidos. Eso lo compruebo yo en `ResultadoAnalisisIa.EsValido` después de deserializar. Confiar ciegamente en que un modelo respetó el esquema es justo lo que no se debe hacer.

### «¿Qué datos le mandas al modelo?»

Solo lo necesario para evaluar el riesgo operativo: salón, capacidad, fecha, horario, invitados, recursos e importes. Del cliente viaja **únicamente el nombre**: no su identificación, ni su correo, ni su teléfono. El examen pide expresamente enviar solo lo necesario.

### «¿Qué pasa si el servicio falla?»

Nada grave, y está probado. Controlo once escenarios: clave ausente, timeout, sin conexión, 401, 403, 404, 429, 5xx, respuesta vacía, JSON inválido y rechazo del modelo. **Ninguno lanza excepción a la interfaz**: devuelvo una ejecución marcada como no exitosa con un mensaje comprensible, y la aplicación sigue operativa. Además, el fallo **también se audita**, porque el examen pide guardar el error cuando corresponda.

### «¿La IA puede confirmar o cancelar una reserva?»

No, y es imposible por diseño. `ServicioAnalisisIaResponses` **no recibe ningún repositorio**: no tiene forma de tocar la base de datos. Y el diálogo que muestra el resultado no tiene ningún botón que actúe sobre la reserva, solo uno para copiar el borrador de correo. El examen es explícito: la IA solo recomienda.

### «¿Por qué usas Groq y no OpenAI?»

Porque no tenía una clave de pago de OpenAI. Groq **implementa la misma Responses API**, con el mismo contrato de salida estructurada, y sirve los modelos `gpt-oss` que son modelos abiertos **de OpenAI**. Como el protocolo es idéntico, no hizo falta escribir una segunda implementación: **la dirección base es configuración, no código**. Cambiar entre uno y otro es una línea en `appsettings.json`.

Está documentado en `docs/USO_IA.md`, y el proveedor real de cada análisis queda registrado en `evt.AnalisisIA.Proveedor`, así que la auditoría es honesta.

> **Si insiste en que debía ser OpenAI:** el código está completo y funciona con `api.openai.com` sin tocar nada; solo hace falta poner una clave con saldo. Lo que se evalúa —encapsulación, JSON Schema, validación, auditoría y control humano— está implementado igual.

---

## 6. Seguridad y calidad · 0.75 pts

### «¿Cómo almacenas las contraseñas?»

PBKDF2-SHA256 con **210 000 iteraciones** y un salt aleatorio de 16 bytes por usuario. El formato es `PBKDF2-SHA256$iteraciones$salt$hash`.

- **Por qué no SHA-256 a secas:** es demasiado rápido, y esa velocidad juega a favor del atacante. PBKDF2 hace que verificar una contraseña cueste milisegundos, pero un ataque por fuerza bruta se vuelve inviable.
- **Por qué salt por usuario:** sin él, dos usuarios con la misma contraseña tendrían el mismo hash y una tabla precalculada los rompería a la vez.
- Y la restricción `CK_Usuario_PasswordHash` impide **a nivel de motor** insertar una contraseña en texto plano.

### «El examen dice que el hash no debe llegar a la interfaz. ¿Cómo lo cumples?»

Con una autenticación **en dos fases** dentro del mismo procedimiento. En la primera, SQL Server me devuelve solo el algoritmo, las iteraciones y el **salt** —nunca el hash—. Con eso calculo localmente el hash de la contraseña escrita. En la segunda, envío ese hash candidato y **el motor lo compara internamente**. El hash almacenado no sale nunca de SQL Server.

### «¿Y si el usuario no existe?»

Devuelvo un **salt señuelo** determinista derivado del nombre. Así la aplicación hace exactamente el mismo trabajo criptográfico exista o no la cuenta, y el mensaje de error es idéntico en los tres casos: usuario inexistente, usuario inactivo y contraseña incorrecta. Eso impide **enumerar usuarios**, que es el primer paso de un ataque por fuerza bruta.

### «¿Dónde están las claves y las contraseñas?»

En ningún sitio del repositorio. Se leen de variables de entorno o de `appsettings.json`, que está en `.gitignore`. El `.gitignore` es el **primer commit del repositorio**, antes de que existiera cualquier archivo de configuración. Lo puedo demostrar:

```bash
git log --all --oneline -- appsettings.json .env secrets.json   → vacío
```

Y de la configuración SMTP solo se persiste **host y puerto**; de OpenAI, solo proveedor y modelo. La clave no toca la base de datos.

### «¿Y en los archivos de registro?»

El registrador **redacta obligatoriamente** antes de escribir: claves de API, cabeceras `Bearer`, pares `password=` y cadenas de conexión completas. No depende de que yo me acuerde de omitir el secreto al escribir una línea; el filtro se aplica siempre.

### «¿Por qué no mantienes una conexión abierta?»

Porque el examen lo prohíbe y porque sería incorrecto: una `SqlConnection` **no es segura** para usarse desde varias tareas asíncronas a la vez. Cada operación abre la suya y la cierra con `await using`. El coste es bajo porque `Microsoft.Data.SqlClient` mantiene un **pool**: al cerrar, la conexión física vuelve al pool.

---

## 7. GitHub, pruebas y documentación · 1.00 pt

### «¿Cómo sé que el proyecto compila desde cero?»

Lo verifiqué: cloné el repositorio en una carpeta nueva, hice `checkout` de la etiqueta `v1.0.0` y compilé. Resultado: **0 advertencias y 0 errores**. La captura está en `docs/evidencias/20-clon-limpio.png`.

### «¿Qué pasa si clono y no configuro nada?»

La aplicación **no revienta**: muestra un mensaje explicando exactamente qué variable definir y dónde. Y las pruebas se **omiten** con un mensaje explicativo en lugar de fallar.

### «¿Qué cubren las pruebas?»

28 pruebas de integración contra la base de datos real, un servidor SMTP real y el servicio de IA real. Cubren **CA-01 a CA-09**. Se ejecutan con un comando:

```bash
dotnet run --project tests/SmartEvent.Pruebas
```

Y son **repetibles**: limpian su propia franja de fechas antes de empezar. Eso lo descubrí porque la primera versión pasaba una vez y fallaba la segunda.

### «¿Por qué `dotnet run` y no `dotnet test`?»

Porque xUnit v3 se ejecuta sobre Microsoft.Testing.Platform, y el SDK 10 de .NET retiró el puente con VSTest. `dotnet run` funciona en cualquier SDK.

---

## 8. Preguntas difíciles

### «¿Qué parte del código te generó la IA?»

Prácticamente todo el código se escribió con asistencia de IA, y está documentado con detalle en `docs/USO_IA.md`. Pero **nada se entregó sin ejecutarse**. En ese documento describo diez defectos que produjo la asistencia, y **siete de ellos solo aparecieron al ejecutar**: dos al correr el script SQL, tres al usar la aplicación, uno al repetir las pruebas y otro al hacer la primera llamada real al servicio de IA.

El trabajo real no fue generar el código, sino ejecutarlo, leer las trazas, entender la causa y corregirla de raíz en lugar de tapar el síntoma.

### «Cuéntame un error que hayas tenido que depurar.»

*(Elige uno y cuéntalo con tus palabras.)*

**El de la grilla.** Al abrir «Nueva reserva» la grilla salía vacía aunque el subtotal sí se calculaba. La traza decía `rowIndex ('0') must be less than '0'`. Resultó ser **reentrada**: el método que recalculaba los totales **escribía** el subtotal de cada fila desde dentro del evento `ListChanged` de la lista enlazada. Esa escritura disparaba otro `ListChanged`, y la grilla intentaba refrescar la fila 0 cuando todavía no había terminado de crearla.

No lo arreglé silenciando la excepción. Lo arreglé quitando la reentrada: el subtotal pasó a tener setter privado, cada fila calcula el suyo cuando cambian sus valores, y el método de totales quedó de **solo lectura**.

### «Si el docente pide agregar una regla nueva, ¿dónde la pondrías?»

Depende. Si es una restricción simple sobre una columna, un `CHECK` en la tabla. Si necesita mirar otras tablas —como la capacidad o el stock—, dentro del procedimiento almacenado correspondiente. Y si además quiero avisar al usuario mientras escribe, añado la comprobación en `ValidadorReserva`, pero **sin quitar la de SQL**: la interfaz es para la experiencia de usuario, SQL es quien decide.

### «¿Qué mejorarías si tuvieras más tiempo?»

Tres cosas concretas:

1. **Reintento automático del correo** con espera creciente, en lugar de solo el reenvío manual.
2. **Pruebas de concurrencia**: lanzar dos guardados simultáneos del mismo salón y horario para verificar que el `UPDLOCK` hace su trabajo. Ahora está razonado pero no probado.
3. **Paginación en la auditoría**, que hoy tiene un tope de 300 filas.

### «¿Qué es lo que menos te convence de tu propia solución?»

*(Responde con honestidad; demuestra criterio.)* Que la capa de dominio replica en C# reglas que ya están en SQL. Es duplicación deliberada y el examen la pide, pero implica que si cambio una fórmula tengo que tocar dos sitios. Lo mitigué concentrando la aritmética en una sola clase, `CalculadoraTotales`, y comprobando con una prueba que la interfaz y SQL Server llegan **al mismo número**.

---

## Lo que debes tener abierto durante la defensa

| Ventana | Para qué |
|---|---|
| La aplicación, con sesión iniciada | Demostrar cualquier flujo en vivo |
| SSMS o una terminal con `sqlcmd` | Ejecutar `99_pruebas_CA.sql` si lo piden |
| `http://localhost:5080` con smtp4dev | Mostrar el correo HTML |
| El repositorio en GitHub | Mostrar el historial y la etiqueta |
| `docs/AUDITORIA_FINAL.md` | Localizar cualquier requisito en segundos |

## Los tres números que debes recordar

```
15 % de impuesto     ·  2 a 12 horas de duración  ·  20 % de descuento máximo
10 % sin ser ADMIN   ·  20 caracteres de motivo   ·  210 000 iteraciones PBKDF2
```

## La frase que resume tu solución

> «Las validaciones en C# son para la experiencia del usuario. **Las que mandan son las de SQL Server.** Por eso puedo demostrar cualquier regla ejecutando el procedimiento directamente, sin abrir la aplicación.»
