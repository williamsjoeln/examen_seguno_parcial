using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Dialogo para capturar un texto con longitud minima obligatoria.
///
/// Se usa en los dos puntos del examen que exigen justificacion escrita:
///   - motivo de cancelacion, minimo 20 caracteres (regla D23)
///   - justificacion de contingencia de IA, minimo 20 caracteres (regla D22)
///
/// El contador de caracteres es visible y el boton Aceptar permanece
/// deshabilitado hasta cumplir el minimo, de modo que el usuario entiende que
/// le falta antes de recibir un rechazo. La validacion real, de todos modos,
/// vuelve a hacerse en el servicio y en el procedimiento almacenado.
/// </summary>
internal sealed class FrmTextoRequerido : Form
{
    private readonly TextBox _txtTexto = new();
    private readonly Label _lblContador = new();
    private readonly Button _btnAceptar = new();
    private readonly int _longitudMinima;

    /// <summary>Texto escrito por el usuario, ya recortado.</summary>
    public string TextoCapturado => _txtTexto.Text.Trim();

    public FrmTextoRequerido(string titulo, string indicacion, int longitudMinima, int longitudMaxima = 500)
    {
        _longitudMinima = longitudMinima;

        Text = titulo;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 300);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = indicacion,
            Location = new Point(20, 18),
            Size = new Size(520, 48),
            Font = new Font("Segoe UI", 9.5F)
        });

        _txtTexto.Location = new Point(20, 74);
        _txtTexto.Size = new Size(520, 140);
        _txtTexto.Multiline = true;
        _txtTexto.ScrollBars = ScrollBars.Vertical;
        _txtTexto.MaxLength = longitudMaxima;
        _txtTexto.BorderStyle = BorderStyle.FixedSingle;
        _txtTexto.Font = new Font("Segoe UI", 10F);
        _txtTexto.TextChanged += (_, _) => ActualizarContador();
        Controls.Add(_txtTexto);

        _lblContador.Location = new Point(20, 220);
        _lblContador.Size = new Size(400, 20);
        _lblContador.Font = new Font("Segoe UI", 8.5F);
        Controls.Add(_lblContador);

        _btnAceptar.Text = "Aceptar";
        _btnAceptar.Location = new Point(300, 248);
        _btnAceptar.Size = new Size(120, 34);
        _btnAceptar.EstiloPrimario();
        _btnAceptar.Enabled = false;
        _btnAceptar.DialogResult = DialogResult.OK;
        Controls.Add(_btnAceptar);

        var btnCancelar = new Button
        {
            Text = "Cancelar",
            Location = new Point(430, 248),
            Size = new Size(110, 34),
            DialogResult = DialogResult.Cancel
        };
        btnCancelar.EstiloSecundario();
        Controls.Add(btnCancelar);

        AcceptButton = _btnAceptar;
        CancelButton = btnCancelar;

        ActualizarContador();
        Shown += (_, _) => _txtTexto.Focus();
    }

    private void ActualizarContador()
    {
        var longitud = _txtTexto.Text.Trim().Length;
        var cumple = longitud >= _longitudMinima;

        _btnAceptar.Enabled = cumple;

        _lblContador.Text = cumple
            ? $"{longitud} caracteres. Cumple el minimo requerido."
            : $"{longitud} de {_longitudMinima} caracteres minimos. Faltan {_longitudMinima - longitud}.";

        _lblContador.ForeColor = cumple ? AyudasUi.Paleta.Exito : AyudasUi.Paleta.Peligro;
    }
}
