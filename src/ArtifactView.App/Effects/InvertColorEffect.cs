using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ArtifactView.App.Effects;

// Inverts the RGB channels of the rendered image while preserving alpha.
// Uses a pre-compiled ps_2_0 pixel shader (Effects/InvertColor.ps).
internal sealed class InvertColorEffect : ShaderEffect
{
    private static readonly PixelShader s_shader = new();

    static InvertColorEffect()
    {
        s_shader.UriSource = new Uri(
            "pack://application:,,,/ArtifactView.App;component/Effects/InvertColor.ps");
    }

    public InvertColorEffect()
    {
        PixelShader = s_shader;
        UpdateShaderValue(InputProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("implicitInput", typeof(InvertColorEffect), 0);
}
