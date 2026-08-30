using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Ia;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Muestra el resultado del analisis de IA ya validado.
///
/// LIMITE DELIBERADO: este dialogo NO tiene ningun boton que confirme,
/// cancele ni modifique la reserva. Solo informa y permite copiar el borrador
/// de correo. El examen es explicito: "la IA solo recomienda; el usuario
/// conserva el control de todas las acciones".
///
/// El borrador de correo se muestra en una caja de texto de solo lectura con un
/// boton de copiar. NO existe ningun boton de enviar, porque el examen prohibe
/// expresamente que ese borrador se envie de forma automatica.
/// </summary>
internal sealed class FrmAnalisisIa : Form
{
    public FrmAnalisisIa(ResultadoAnalisisIa resultado, string proveedor, string modelo, int duracionMs)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        Text = "Analisis de riesgo con IA";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ClientSize = new Size(760, 620);
        MinimumSize = new Size(680, 520);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        var contenedor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20),
            BackColor = Color.White
        };

        contenedor.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // nivel de riesgo
        contenedor.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // resumen
        contenedor.RowStyles.Add(new RowStyle(SizeType.Percent, 26));// alertas
        contenedor.RowStyles.Add(new RowStyle(SizeType.Percent, 30));// recomendaciones
        contenedor.RowStyles.Add(new RowStyle(SizeType.Percent, 44));// correo sugerido
        contenedor.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // pie

        // ---------- Nivel de riesgo ----------
        var panelRiesgo = new Panel { Dock = DockStyle.Fill, Height = 62, Margin = new Padding(0, 0, 0, 12) };

        var etiquetaRiesgo = new Label
        {
            Text = $"  NIVEL DE RIESGO: {resultado.NivelRiesgo}  ",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AyudasUi.ColorNivelRiesgo(resultado.Nivel),
            AutoSize = false,
            Size = new Size(320, 46),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 6)
        };

        panelRiesgo.Controls.Add(etiquetaRiesgo);
        contenedor.Controls.Add(panelRiesgo, 0, 0);

        // ---------- Resumen ----------
        var panelResumen = new Panel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };

        panelResumen.Controls.Add(new Label
        {
            Text = "Resumen",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(0, 0)
        });

        panelResumen.Controls.Add(new Label
        {
            Text = resultado.Resumen,
            Location = new Point(0, 22),
            Size = new Size(700, 56),
            Font = new Font("Segoe UI", 9.5F)
        });

        panelResumen.Height = 84;
        contenedor.Controls.Add(panelResumen, 0, 1);

        // ---------- Alertas ----------
        contenedor.Controls.Add(
            ConstruirLista("Alertas", resultado.Alertas, AyudasUi.Paleta.Peligro,
                "El analisis no detecto alertas."),
            0, 2);

        // ---------- Recomendaciones ----------
        contenedor.Controls.Add(
            ConstruirLista("Recomendaciones", resultado.Recomendaciones, AyudasUi.Paleta.Exito,
                "Sin recomendaciones."),
            0, 3);

        // ---------- Correo sugerido ----------
        var panelCorreo = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };

        panelCorreo.Controls.Add(new Label
        {
            Text = "Borrador de correo sugerido",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(0, 0)
        });

        var avisoNoEnvio = new Label
        {
            Text = "Es solo una propuesta. El sistema NUNCA lo envia automaticamente.",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            ForeColor = AyudasUi.Paleta.TextoSuave,
            AutoSize = true,
            Location = new Point(0, 20)
        };
        panelCorreo.Controls.Add(avisoNoEnvio);

        var cajaCorreo = new TextBox
        {
            Text = resultado.CorreoSugerido,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 249, 250),
            Font = new Font("Segoe UI", 9F),
            Location = new Point(0, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Size = new Size(700, 140)
        };
        panelCorreo.Controls.Add(cajaCorreo);

        var btnCopiar = new Button
        {
            Text = "Copiar borrador",
            Size = new Size(150, 30),
            Location = new Point(560, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnCopiar.EstiloSecundario();
        btnCopiar.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(cajaCorreo.Text))
            {
                Clipboard.SetText(cajaCorreo.Text);
                AyudasUi.MostrarInformacion("El borrador se copio al portapapeles.");
            }
        };
        panelCorreo.Controls.Add(btnCopiar);

        contenedor.Controls.Add(panelCorreo, 0, 4);

        // ---------- Pie ----------
        var pie = new Panel { Dock = DockStyle.Fill, Height = 52 };

        pie.Controls.Add(new Label
        {
            Text = $"Proveedor: {proveedor}   |   Modelo: {modelo}   |   Tiempo: {duracionMs} ms"
                   + Environment.NewLine
                   + "Este analisis quedo guardado en la auditoria de la reserva.",
            ForeColor = AyudasUi.Paleta.TextoSuave,
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = false,
            Size = new Size(500, 40),
            Location = new Point(0, 10)
        });

        var btnCerrar = new Button
        {
            Text = "Cerrar",
            Size = new Size(120, 34),
            Location = new Point(590, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        btnCerrar.EstiloPrimario();
        pie.Controls.Add(btnCerrar);

        contenedor.Controls.Add(pie, 0, 5);

        Controls.Add(contenedor);
        AcceptButton = btnCerrar;
        CancelButton = btnCerrar;
    }

    /// <summary>Construye un bloque con titulo y una lista de textos.</summary>
    private static Panel ConstruirLista(
        string titulo, List<string> elementos, Color colorVinneta, string textoVacio)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };

        panel.Controls.Add(new Label
        {
            Text = $"{titulo} ({elementos.Count})",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(0, 0)
        });

        var lista = new ListBox
        {
            Location = new Point(0, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Size = new Size(700, 90),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F),
            IntegralHeight = false,
            SelectionMode = SelectionMode.One
        };

        if (elementos.Count == 0)
        {
            lista.Items.Add(textoVacio);
            lista.ForeColor = AyudasUi.Paleta.TextoSuave;
        }
        else
        {
            foreach (var elemento in elementos)
            {
                lista.Items.Add("•  " + elemento);
            }

            lista.ForeColor = colorVinneta;
        }

        panel.Controls.Add(lista);
        return panel;
    }
}
