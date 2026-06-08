using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TestFramework.DebugUI.Shaders;

public class BlurShaderEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(BlurShaderEffect), 0);

    public static readonly DependencyProperty BlurAmountProperty =
        DependencyProperty.Register(
            "BlurAmount",
            typeof(double),
            typeof(BlurShaderEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty BlurSigmaProperty =
    DependencyProperty.Register(
        "BlurSigma",
        typeof(double),
        typeof(BlurShaderEffect),
        new UIPropertyMetadata(10.0, PixelShaderConstantCallback(1)));

    public BlurShaderEffect()
    {
        PixelShader ps = new PixelShader();
        ps.UriSource = new Uri("pack://application:,,,/TestFramework.DebugUI;component/Shaders/HLSL/BlurShader.ps", UriKind.Absolute);
        this.PixelShader = ps;

        UpdateShaderValue(InputProperty);
        UpdateShaderValue(BlurAmountProperty);
    }

    public Brush Input
    {
        get { return (Brush)GetValue(InputProperty); }
        set { SetValue(InputProperty, value); }
    }

    public double BlurAmount
    {
        get { return (double)GetValue(BlurAmountProperty); }
        set { SetValue(BlurAmountProperty, value); }
    }

    public double BlurSigma
    {
        get { return (double)GetValue(BlurSigmaProperty); }
        set { SetValue(BlurSigmaProperty, value); }
    }

}