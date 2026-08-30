# Uso de inteligencia artificial en el desarrollo

**Proyecto:** SmartEvent AI — Examen práctico EX-002-2-A-2026
**Estudiante:** Williams Joel Navarrete Merino

> El examen permite usar IA y exige documentar su uso: *«Usar IA no resta puntos; ocultar su uso o entregar código no comprendido sí afecta la evaluación.»*
> Este documento describe con exactitud qué se hizo con asistencia de IA, qué errores produjo, cómo se detectaron y qué decisiones se tomaron o se cambiaron.

---

## 1. Herramientas utilizadas

| Herramienta | Para qué |
|---|---|
| **Claude (Anthropic), modelo Opus** a través de Claude Code | Análisis del enunciado, diseño de la arquitectura, generación de la mayor parte del código, redacción de la documentación |
| **sqlcmd** | Ejecución y verificación de los scripts SQL |
| **xUnit v3** | Verificación automatizada de lo generado |
| **smtp4dev** | Servidor SMTP local para probar el correo de verdad |
| **Groq** (`openai/gpt-oss-120b`) | Proveedor del servicio de IA **en tiempo de ejecución** de la aplicación — ver sección 6 |

**Distinción importante:** hay dos usos de IA en este trabajo que no deben confundirse.

1. **IA como asistente de desarrollo** — me ayudó a escribir el código. Es lo que documenta este archivo.
2. **IA como funcionalidad del producto** — la integración con la Responses API que el examen exige implementar. Es parte del sistema entregado.

---

## 2. Qué se generó con asistencia de IA

Prácticamente todo el código se escribió con asistencia, siguiendo un ciclo de **generar → ejecutar → corregir**. La distribución fue:

| Parte | Grado de asistencia | Observación |
|---|---|---|
| Script de base de datos y procedimientos | Alto | Verificado ejecutándolo; se corrigieron 2 errores reales |
| Capa de dominio | Alto | Las fórmulas provienen del enunciado |
| Repositorios y acceso a datos | Alto | Verificado con pruebas de integración |
| Servicios de aplicación | Alto | El orden de las operaciones fue una decisión discutida |
| Correo con MailKit | Alto | Verificado con envíos reales |
| Integración con OpenAI | Alto | El JSON Schema se ajustó tras una prueba real |
| Formularios Windows Forms | Alto | Se corrigieron 4 defectos detectados **usando la aplicación** |
| Pruebas automatizadas | Alto | Diseñadas para cubrir CA-01 a CA-09 |
| Documentación | Alto | Revisada y ajustada |

**Nada se entregó sin ejecutarse.** Cada capa se compiló y se probó antes de continuar con la siguiente.

---

## 3. Prompts relevantes

Los prompts completos fueron largos. Estos son los que más determinaron el resultado:

**Prompt inicial (resumido).** Se entregó el enunciado del examen en formato Word con la instrucción de leerlo completo antes de escribir código, extraer todos los requisitos, no inventar reglas que el enunciado no especificara, trabajar por fases verificando cada una antes de avanzar, y explicar cada decisión técnica para poder defenderla oralmente.

**Prompts de decisión.** Cuando el enunciado no especificaba algo, se preguntó explícitamente en lugar de asumir. Los cuatro vacíos identificados se resolvieron así:

| Vacío del enunciado | Decisión tomada |
|---|---|
| Cómo se determina el descuento global de la cabecera | Columna `PorcentajeDescuentoGlobal` de 0 a 20 %, con la misma regla de autorización que el descuento de línea |
| Matriz de permisos ADMINISTRADOR / COORDINADOR | COORDINADOR opera reservas; ADMINISTRADOR además catálogos, auditoría y descuentos > 10 % |
| Intentos y duración del bloqueo temporal | 3 intentos → 3 minutos, configurable |
| Formato del código de reserva | `RSV-yyyyMMdd-NNNNNN` generado con una `SEQUENCE` dentro del procedimiento |

**Prompts de verificación.** Después de cada fase se pidió revisar el código buscando errores de compilación, referencias faltantes, problemas de `async`/`await`, fugas de recursos, SQL inseguro y reglas del examen omitidas.

---

## 4. Errores que produjo la IA y cómo se detectaron

Esta es la parte más importante del documento. **Todos estos errores se encontraron ejecutando, no leyendo.**

### 4.1 `DEFAULT ... FOR columna` dentro de `CREATE TABLE`

- **Qué pasó:** el script generado declaraba las restricciones `DEFAULT` con la sintaxis de `ALTER TABLE`, que no es válida dentro de `CREATE TABLE`.
- **Cómo se detectó:** al ejecutar el script con sqlcmd. `Msg 102: Incorrect syntax near 'for'`.
- **Corrección:** mover las 20 restricciones `DEFAULT` a la definición de cada columna.

### 4.2 `QUOTED_IDENTIFIER` y los métodos del tipo XML

- **Qué pasó:** `sp_Usuario_Autenticar` generaba el salt señuelo con un método del tipo `XML`, que exige `QUOTED_IDENTIFIER ON`. Esa opción **queda grabada al crear el procedimiento**, y sqlcmd la deja en `OFF` por defecto.
- **Cómo se detectó:** una prueba automatizada falló con `Msg 1934: SELECT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'`.
- **Corrección:** sustituir el método XML por una derivación hexadecimal válida como Base64, y fijar `SET ANSI_NULLS ON` y `SET QUOTED_IDENTIFIER ON` antes de crear los procedimientos.
- **Lo aprendido:** las opciones `SET` vigentes al **crear** un procedimiento quedan grabadas en él.

### 4.3 `System.Threading.Lock` no existe en .NET 8

- **Qué pasó:** el registrador usaba el tipo `Lock`, que es de .NET 9.
- **Cómo se detectó:** error de compilación.
- **Corrección:** usar `object`, con un comentario explicando por qué.

### 4.4 Reentrada en la grilla del detalle

- **Qué pasó:** al abrir *Nueva reserva* la grilla salía vacía y aparecía un error, aunque el subtotal sí se calculaba. La traza:

  ```
  ArgumentOutOfRangeException: rowIndex ('0') must be less than '0'
     at DataGridView.InvalidateCellPrivate
     at BindingList.Child_PropertyChanged
     at FilaDetalle.set_SubtotalLinea
     at RecalcularTotales
     at AgregarLinea
  ```

- **Causa:** `RecalcularTotales` **asignaba** el subtotal de cada fila desde dentro del evento `ListChanged` de la `BindingList`. Esa asignación disparaba `PropertyChanged`, que disparaba otro `ListChanged`, y la grilla intentaba refrescar la fila 0 cuando todavía no había terminado de crearla.
- **Corrección:** eliminar la reentrada de raíz en lugar de silenciar la excepción. `SubtotalLinea` pasó a tener setter privado, cada fila calcula su propio subtotal cuando cambian sus valores, `RecalcularTotales` quedó de solo lectura, y `AgregarLinea` deja la fila ya calculada antes de añadirla.
- **Lo aprendido:** no se deben modificar los elementos de una lista enlazada desde dentro del evento que notifica que la lista cambió.

### 4.5 Doble liberación de formularios por el contenedor de dependencias

- **Qué pasó:** al cerrar sesión con la ventana de auditoría abierta:

  ```
  ObjectDisposedException: The CancellationTokenSource has been disposed.
     at FrmAuditoriaIntegraciones.Dispose
     at ServiceProvider.Dispose
  ```

- **Causa:** los formularios se resolvían con `GetRequiredService`. El contenedor de Microsoft **rastrea** los servicios transitorios que implementan `IDisposable`, y un `Form` lo implementa. Consecuencias: guardaba una referencia a **cada ventana abierta** durante toda la sesión (fuga de memoria) y la liberaba **por segunda vez** al cerrar la aplicación.
- **Corrección:** crear los formularios con `ActivatorUtilities.CreateInstance` a través de una `FabricaFormularios`. Resuelve las dependencias igual, pero el contenedor no rastrea el objeto: su ciclo de vida queda en manos de Windows Forms. Se mantiene el registro únicamente para que `ValidateOnBuild` compruebe las dependencias al arrancar.
- **Lo aprendido:** el contenedor de inyección retiene los servicios transitorios que son `IDisposable`. Registrar formularios como transitorios es una fuga de memoria silenciosa.

### 4.6 `MinDate` fuera del rango admitido

- **Qué pasó:** la reserva se guardaba bien, pero al recargarla saltaba:

  ```
  ArgumentOutOfRangeException: DateTimePicker no admite fechas anteriores a 1/1/1753.
  Actual value was 1/1/0001.
  ```

- **Causa:** para poder editar reservas con fecha pasada se relajaba el mínimo del selector con `DateTime.MinValue`, que es el año 1.
- **Corrección:** usar `DateTimePicker.MinimumDateTime`.

### 4.7 La batería de pruebas no era repetible

- **Qué pasó:** las pruebas pasaban la primera vez y fallaban la segunda.
- **Causa:** las reservas creadas en una ejecución chocaban por cruce de horario con las de la siguiente. **La aplicación funcionaba bien; la prueba estaba mal escrita.**
- **Corrección:** limpiar la franja de fechas reservada antes de cada ejecución. Se comprobó ejecutando la batería dos veces seguidas.

### 4.8 El JSON Schema sin descripciones por campo

- **Qué pasó:** en la primera prueba real contra la Responses API, el campo `correoSugerido` devolvió solo una dirección de correo en lugar de un borrador.
- **Causa:** el esquema declaraba el campo como `string` pero no lo **describía**.
- **Corrección:** añadir `description` a cada propiedad del esquema y reforzar las instrucciones del sistema. Se resolvió **sin tocar una sola línea de código C#**.
- **Lo aprendido:** en salida estructurada, el esquema no solo valida: también guía al modelo.

### 4.9 Errores de calidad detectados por los analizadores

Cuestiones menores que el compilador señaló y se corrigieron: interpolación de cadenas sin cultura (produce números distintos según la configuración regional), `JsonSerializerOptions` creado en cada llamada, uso de `System.Web.HttpUtility` cuando `System.Net.WebUtility` del BCL bastaba, y un conflicto de versiones de paquetes (`NU1605`) que se resolvió centralizando las versiones en `Directory.Packages.props`.

### 4.10 Dos problemas de usabilidad detectados usando la aplicación

- **El buscador de cliente no avisaba cuando no encontraba a nadie.** Al escribir una identificación inexistente, la lista se quedaba vacía en silencio y el mensaje posterior *«Seleccione un cliente»* no se relacionaba con la causa. Se corrigió marcando el cuadro en rojo, mostrando un aviso explícito y seleccionando automáticamente cuando hay una sola coincidencia.
- **La cabecera se cortaba por la derecha** en ventanas no muy anchas. Se reorganizó en tres filas y el botón de verificar disponibilidad se movió a la barra inferior.

---

## 5. Decisiones que modifiqué respecto a la primera propuesta

| Propuesta inicial | Decisión final | Motivo |
|---|---|---|
| Formato de solución `.slnx` (por defecto del SDK 10) | `.sln` clásico | Visual Studio 2022 anterior a la 17.14 no abre `.slnx`. Si el docente no puede abrir la solución, la penalización es máximo 4/10 |
| MailKit 4.8.0 | MailKit **4.17.0** | La 4.8.0 tiene la vulnerabilidad `GHSA-9j88-vvj5-vhgr` |
| Versiones de paquetes en cada `.csproj` | `Directory.Packages.props` | Evita el conflicto `NU1605` y la duplicación |
| Formularios como servicios transitorios del contenedor | `FabricaFormularios` con `ActivatorUtilities` | Ver 4.5 |
| GitHub Models como proveedor de IA gratuito | **Groq** | GitHub Models fue **retirado el 30 de julio de 2026**; su API devuelve `410 Gone`. Se verificó antes de escribir el código |
| Redondeo por defecto de .NET | `MidpointRounding.AwayFromZero` | .NET redondea «al par» y SQL Server no. Sin esto, pantalla y base podían diferir en un centavo |
| Botón «Verificar disponibilidad» en la cabecera | En la barra de acciones | Quedaba fuera del área visible |

---

## 6. Sobre el proveedor del servicio de IA

El examen exige consumir la **Responses API** y leer la clave de `OPENAI_API_KEY`. Ambas cosas se cumplen literalmente.

**Situación real:** no dispuse de una clave de pago de OpenAI. Investigando alternativas descubrí que:

1. **GitHub Models fue retirado el 30 de julio de 2026** — su API devuelve `410 Gone`. Lo comprobé con una petición real antes de descartarlo.
2. **Groq implementa la Responses API de OpenAI** en `https://api.groq.com/openai/v1/responses`, con el mismo contrato: `text.format.type = json_schema` y `strict: true`. Sirve los modelos **`openai/gpt-oss-120b`** y `openai/gpt-oss-20b`, que son modelos abiertos **de OpenAI**. Tiene nivel gratuito sin tarjeta de crédito.

**Consecuencia de diseño:** como el protocolo es idéntico, **no hizo falta una segunda implementación**. La dirección base es configuración, no código:

```
OpenAI__BaseUrl = https://api.openai.com/v1        →  OpenAI
OpenAI__BaseUrl = https://api.groq.com/openai/v1   →  Groq
```

`ServicioAnalisisIaResponses` es exactamente el mismo en ambos casos. El valor por defecto en la configuración de ejemplo apunta a OpenAI. El proveedor que se usó realmente queda registrado en `evt.AnalisisIA.Proveedor` de cada análisis, de modo que la auditoría es honesta y trazable.

**Esto no es un atajo:** encapsular el proveedor tras una interfaz y hacer la dirección configurable es precisamente la buena práctica que el enunciado premia con *«Responses API encapsulada»*.

---

## 7. Qué entiendo y puedo explicar

Puedo explicar cualquier archivo entregado. En particular:

- Por qué la transacción vive en el procedimiento almacenado y no en C#, y qué hacen `XACT_ABORT`, `TRY/CATCH` y `XACT_STATE()` juntos.
- Por qué el detalle viaja como TVP y qué impide la `PRIMARY KEY` del tipo tabla.
- Por qué la fórmula de cruce usa comparaciones estrictas y qué implica para dos franjas adyacentes.
- Por qué el hash de la contraseña nunca sale de SQL Server y qué es un salt señuelo.
- Por qué el correo se envía fuera de la transacción y por qué reintentar no duplica el cambio de estado.
- Por qué se valida la respuesta de la IA aunque el esquema sea estricto.
- Por qué los formularios no se resuelven del contenedor de dependencias.
- Por qué `RecalcularTotales` no puede escribir en las filas de la grilla.

---

## 8. Conclusión honesta

La IA aceleró mucho la escritura del código, pero **no lo entregó funcionando**. De los diez defectos documentados en la sección 4, **siete solo aparecieron al ejecutar**: dos al correr el script SQL, tres al usar la aplicación, uno al repetir las pruebas y uno al hacer la primera llamada real al servicio de IA.

El trabajo real no fue generar el código, sino **ejecutarlo, leer las trazas, entender la causa y corregirla de raíz** en lugar de silenciar el síntoma. Los tres defectos de la interfaz, en concreto, se encontraron usando la aplicación como la usaría el docente en la defensa.
