sampler2D input : register(s0);
float blurRadius : register(c0); // Controls how far samples spread
float blurSigma : register(c1); // Controls Gaussian falloff sharpness

float gaussian(float distance, float sigma)
{
    const float pi = 3.14159265359;
    return exp(-(distance * distance) / (2.0 * sigma * sigma)) / (sqrt(2.0 * pi) * sigma);
}

float4 main(float2 uv : TEXCOORD0) : COLOR
{
    float4 color = float4(0, 0, 0, 0);
    float totalWeight = 0.0;
    
    float stepDistance = blurRadius * 0.00015; // Configurable step size
    int sampleRange = 5; // -5 to +5 = 11x11 grid (121 samples)
    
    for (int x = -sampleRange; x <= sampleRange; x++)
    {
        for (int y = -sampleRange; y <= sampleRange; y++)
        {
            float2 offset = float2(x, y) * stepDistance;
            float distance = length(offset);
            float weight = gaussian(distance, blurSigma);
            
            color += tex2D(input, uv + offset) * weight;
            totalWeight += weight;
        }
    }
    
    return color / totalWeight;
}
