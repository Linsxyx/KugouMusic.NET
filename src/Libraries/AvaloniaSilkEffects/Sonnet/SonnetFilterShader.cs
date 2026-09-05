namespace AvaloniaSilkEffects.Sonnet;

// Folia d5b8b24d sonnetLensFilter/sonnetPrintFilters and Pixi NoiseFilter.
internal static class SonnetFilterShader
{
    internal const string Fragment = """
        #version 330 core
        in vec2 vUv;
        out vec4 finalColor;
        uniform sampler2D uTexture;
        uniform vec2 uResolution;
        uniform int uPass;
        uniform float uAmount;
        uniform float uDispersion;
        uniform float uSeed;
        vec4 sampleInside(vec2 uv) {
            vec2 halfTexel = 0.5 / uResolution;
            if (any(lessThan(uv, halfTexel)) || any(greaterThan(uv, 1.0-halfTexel))) return vec4(0.0);
            return texture(uTexture, uv);
        }
        vec4 splitColor(vec2 uv, vec2 offset, float amount) {
            vec4 center = sampleInside(uv);
            vec4 red = sampleInside(uv + offset);
            vec4 blue = sampleInside(uv - offset);
            vec3 core = center.rgb * (0.84 - clamp(amount, 0.0, 1.0) * 0.18);
            return vec4(max(core, vec3(red.r, center.g, blue.b)), max(center.a, max(red.a, blue.a)));
        }
        float dotScreen(vec2 p, float angle, float value) {
            float c = cos(angle), s = sin(angle);
            vec2 rotated = mat2(c,s,-s,c) * p;
            float dist = length(fract(rotated / 5.0) - 0.5) * 5.0;
            float radius = sqrt(clamp(value, 0.0, 1.0)) * 5.0 * 0.62;
            return 1.0 - smoothstep(radius - 1.2, radius + 1.2, dist);
        }
        void main() {
            vec4 color = texture(uTexture, vUv);
            float aspect = uResolution.x / uResolution.y;
            // Pixi's screen coordinates have their origin at the top left.
            vec2 screenUv = vec2(vUv.x, 1.0-vUv.y);
            vec2 frag = vec2(gl_FragCoord.x, uResolution.y-gl_FragCoord.y);
            if (uPass == 0) {
                vec2 centered = screenUv - 0.5;
                centered.x *= aspect;
                float r2 = dot(centered, centered);
                float curvature = uAmount * 0.32;
                vec2 lens = centered * (1.0-curvature*r2+curvature*0.16*r2*r2);
                lens.x /= aspect;
                lens += 0.5;
                float radius = sqrt(r2);
                vec2 direction = radius > 0.0001 ? centered/radius : vec2(0.0);
                vec2 offset = direction * uDispersion * 0.012 * smoothstep(0.12,0.9,radius);
                offset.x /= aspect;
                color = splitColor(vec2(lens.x,1.0-lens.y), vec2(offset.x,-offset.y), uDispersion);
            } else if (uPass == 1) {
                float noise = fract(sin(dot(frag*uSeed,vec2(12.9898,78.233)))*43758.5453);
                color.rgb += (noise-0.5)*uAmount*color.a;
            } else if (uPass == 2) {
                color.rgb = (color.rgb-0.5*color.a)*(1.0+uAmount)+0.5*color.a;
            } else if (uPass == 3) {
                color = splitColor(vUv,vec2(0.9063,-0.4226)*uAmount*3.0/uResolution,uAmount);
            } else if (uPass == 4) {
                vec3 straight = color.a > 0.0 ? color.rgb/color.a : color.rgb;
                vec3 screened = vec3(dotScreen(frag,radians(15.0),straight.r),
                    dotScreen(frag,radians(75.0),straight.g),dotScreen(frag,0.0,straight.b));
                color.rgb = mix(straight,screened,uAmount)*color.a;
            } else if (uPass == 5) {
                vec2 centered = screenUv-0.5;
                centered.x *= aspect;
                float amount = clamp(smoothstep(0.52,1.08,length(centered))*uAmount*0.6,0.0,1.0);
                color = mix(color,vec4(0.0,0.0,0.0,1.0),amount);
            }
            finalColor = color;
        }
        """;
}
