# Cómo tomar las capturas

## Dónde guardarlas

**En esta misma carpeta:** `docs\evidencias\`

Ruta completa en este equipo:

```
C:\Users\PREDATOR\Desktop\examen\docs\evidencias\
```

## Con qué herramienta

Pulse **Win + Shift + S** → seleccione la ventana → la captura queda en el
portapapeles → péguela en Paint (**Ctrl+V**) → **Archivo → Guardar como → PNG**.

O bien **Alt + Impr Pant** copia solo la ventana activa.

También sirve la aplicación **Recortes** de Windows, que permite guardar
directamente en PNG eligiendo la carpeta.

## Nombres exactos

Use exactamente estos nombres, en minúsculas y con guiones. El archivo
`EVIDENCIAS.md` los referencia uno por uno.

| Archivo | Qué debe mostrar |
|---|---|
| `01-login.png` | Pantalla de inicio de sesión |
| `02-login-bloqueo.png` | Tras 3 intentos fallidos: botón con la cuenta atrás |
| `03-principal-coordinador.png` | Menú como `coordinador`: **sin** Catálogos ni Auditoría |
| `04-principal-admin.png` | Menú como `admin`: **con** Catálogos y Auditoría |
| `05-catalogos.png` | Pestaña Clientes con la grilla y el panel de edición |
| `06-reserva-tres-detalles.png` | Reserva con 3 líneas y los totales calculados |
| `07-reserva-recuperada.png` | La misma reserva reabierta desde la consulta |
| `08-rollback.png` | Salida de `99_pruebas_CA.sql`, bloque CA-02 |
| `09-cruce-horario.png` | Mensaje de rechazo por cruce de horario |
| `10-editar-borrador.png` | Borrador editado conservando su código |
| `11-capacidad-stock-sql.png` | Salida de `99_pruebas_CA.sql`, bloque CA-05 |
| `12-confirmar.png` | Reserva en estado CONFIRMADA |
| `13-correo-html.png` | El correo abierto en `http://localhost:5080` |
| `14-auditoria-correo.png` | Pestaña Intentos de correo con la fila ENVIADO |
| `15-reenvio-ca07.png` | Dos filas: intento 1 ERROR e intento 2 ENVIADO |
| `16-analisis-ia.png` | Ventana del análisis con nivel de riesgo y recomendaciones |
| `17-auditoria-ia-json.png` | Pestaña Análisis de IA con el JSON indentado |
| `18-ia-sin-clave.png` | Mensaje al analizar sin clave configurada |
| `19-consulta-reservas.png` | Consulta con filtros y estados por color |
| `20-clon-limpio.png` | Terminal mostrando el clon y la compilación sin errores |

## Antes de guardar cada captura, compruebe

- [ ] **No aparece ninguna clave de API ni contraseña.**
- [ ] La pestaña *Configuración vigente* solo muestra `Clave configurada: SI`,
      nunca el valor de la clave.
- [ ] Los datos son ficticios: los clientes semilla usan dominios `.ejemplo.com`
      precisamente para esto.
- [ ] Si usó su correo personal como destinatario, considere reemplazarlo por
      uno ficticio antes de la captura.
- [ ] La ventana se ve completa, no cortada.

## Cuando termine

Avise para hacer el commit final y mover la etiqueta `v1.0.0`, de modo que la
etiqueta quede sobre el commit que ya incluye las evidencias.
