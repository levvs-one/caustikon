// A glass panel over an interface, traced per pixel: the surface has a bevel and an optional dome, the view refracts into
// the glass with the chosen glass's index per colour channel, travels the thickness, and reads the backdrop where it lands.
// Fresnel reflectance of the same glass adds the room; Beer–Lambert absorption from the glass's own k table tints the path.
// Frost is a design choice layered on top. The fragment shader below is what the page hands out as GLSL to copy.
window.caustikonUi = (() => {
    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

    // Kept as a template so the page can bake the glass's constants in for the copy button.
    const FRAGMENT = `#version 300 es
precision highp float;
out vec4 outColour;

uniform vec2 uResolution;      // canvas in device pixels
uniform sampler2D uBackdrop;   // what sits behind the glass, sRGB
uniform vec3 uIor;             // refractive index at 610, 550 and 465 nm
uniform vec3 uAlpha;           // absorption per millimetre at the same wavelengths
uniform float uPxPerMm;        // how big a millimetre is on this canvas
uniform float uThickness;      // glass thickness, mm
uniform float uBevel;          // bevel width, px
uniform float uDome;           // 0 flat top, 1 a pillow that sags half the thickness at the edge
uniform float uFrost;          // blur radius, px
uniform float uRadius;         // corner radius, px
uniform vec2 uHalf;            // panel half size, px
uniform vec2 uLight;           // where the room light is, unit vector in the screen plane
uniform float uLightStrength;  // 1 is the default lamp

float sdRoundRect(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

// Unpolarized Fresnel power reflectance from n1 into n2; 1 past the critical angle.
float fresnel(float cosI, float n1, float n2) {
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

vec3 toLinear(vec3 c) { return pow(c, vec3(2.2)); }
vec3 toSrgb(vec3 c) { return pow(clamp(c, 0.0, 1.0), vec3(1.0 / 2.2)); }

// The backdrop at a point, blurred by the frost radius: a mip level for the bulk, eight taps on a ring for the shape.
vec3 backdrop(vec2 px, float frost) {
    vec2 uv = px / uResolution;
    if (frost < 0.5) return toLinear(texture(uBackdrop, uv).rgb);
    float lod = log2(1.0 + frost * 0.5);
    vec3 sum = vec3(0.0);
    for (int i = 0; i < 8; i++) {
        float a = float(i) * 0.785398;
        vec2 offset = vec2(cos(a), sin(a)) * frost * 0.7 / uResolution;
        sum += toLinear(textureLod(uBackdrop, uv + offset, lod).rgb);
    }
    return sum / 8.0;
}

// The room the glass reflects: a soft gradient plus one lamp where uLight points.
vec3 room(vec3 direction) {
    float up = clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 base = mix(vec3(0.62, 0.64, 0.68), vec3(0.96, 0.97, 1.0), up);
    vec3 lamp = normalize(vec3(uLight, 0.55));
    float key = max(0.0, dot(direction, lamp));
    return base * 0.55 + vec3(1.0, 0.98, 0.94) * uLightStrength * (2.6 * pow(key, 48.0) + 0.35 * pow(key, 6.0));
}

void main() {
    vec2 px = gl_FragCoord.xy;
    vec2 p = px - uResolution * 0.5;
    float d = sdRoundRect(p, uHalf, uRadius);
    vec3 plain = toLinear(texture(uBackdrop, px / uResolution).rgb);
    if (d > 0.0) {
        // A shadow the panel casts on the page, fading over about forty pixels.
        outColour = vec4(toSrgb(plain * (1.0 - 0.22 * exp(-d / 18.0))), 1.0);
        return;
    }

    // Surface height across the panel: a bevel that drops half the thickness over uBevel px, and a pillow dome.
    float inset = -d;
    vec2 g = normalize(vec2(sdRoundRect(p + vec2(1.0, 0.0), uHalf, uRadius) - sdRoundRect(p - vec2(1.0, 0.0), uHalf, uRadius),
                            sdRoundRect(p + vec2(0.0, 1.0), uHalf, uRadius) - sdRoundRect(p - vec2(0.0, 1.0), uHalf, uRadius)) + 1e-6);
    float thicknessPx = uThickness * uPxPerMm;
    float maxInset = min(uHalf.x, uHalf.y);
    float x = clamp(inset / maxInset, 0.0, 1.0);
    float bevelSlope = (uBevel > 0.5 && inset < uBevel) ? 0.5 * thicknessPx / uBevel : 0.0;
    float domeSag = uDome * 0.5 * thicknessPx;
    float domeSlope = 2.0 * domeSag * (1.0 - x) / maxInset;
    float slope = bevelSlope + domeSlope;
    // Height increases inward, so the normal leans outward.
    vec3 normal = normalize(vec3(g * slope, 1.0));
    float bevelDrop = (uBevel > 0.5 && inset < uBevel) ? 0.5 * thicknessPx * (1.0 - inset / uBevel) : 0.0;
    float depthPx = max(1.0, thicknessPx - bevelDrop - domeSag * (1.0 - x) * (1.0 - x));

    vec3 view = vec3(0.0, 0.0, -1.0);
    float cosI = normal.z;
    vec3 transmitted = vec3(0.0);
    vec3 reflectance = vec3(0.0);
    for (int c = 0; c < 3; c++) {
        float n = uIor[c];
        float r = fresnel(cosI, 1.0, n);
        reflectance[c] = r;
        vec3 inside = refract(view, normal, 1.0 / n);
        if (dot(inside, inside) < 0.5) continue;
        // Straight down to the flat back face, which sits on the interface; where the ray lands is what shows.
        vec2 shift = inside.xy / (-inside.z) * depthPx;
        float pathMm = depthPx / uPxPerMm / (-inside.z);
        float t = exp(-uAlpha[c] * pathMm);
        transmitted[c] = backdrop(px + shift, uFrost)[c] * t * (1.0 - r);
    }

    vec3 seen = room(reflect(view, normal));
    vec3 colour = transmitted + seen * reflectance;
    outColour = vec4(toSrgb(colour), 1.0);
}`;

    const views = new Map();

    function compile(gl, type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const log = gl.getShaderInfoLog(shader);
            gl.deleteShader(shader);
            throw new Error(log);
        }
        return shader;
    }

    function setup(canvas) {
        const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: true });
        if (!gl) return null;
        const program = gl.createProgram();
        gl.attachShader(program, compile(gl, gl.VERTEX_SHADER, VERTEX));
        gl.attachShader(program, compile(gl, gl.FRAGMENT_SHADER, FRAGMENT));
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
        gl.useProgram(program);
        const u = name => gl.getUniformLocation(program, name);
        const texture = gl.createTexture();
        const view = {
            canvas, gl, program, texture, spec: null, backdropKey: "",
            loc: {
                resolution: u("uResolution"), backdrop: u("uBackdrop"), ior: u("uIor"), alpha: u("uAlpha"), pxPerMm: u("uPxPerMm"),
                thickness: u("uThickness"), bevel: u("uBevel"), dome: u("uDome"), frost: u("uFrost"), radius: u("uRadius"),
                half: u("uHalf"), light: u("uLight"), lightStrength: u("uLightStrength"),
            },
            frame: 0, fallback: 0,
        };
        if (typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => request(view)).observe(canvas);
        }
        return view;
    }

    // What the glass sits on: an interface with text, colour, thin lines and a photo-like gradient, so refraction,
    // fringes, tint and frost each have something to act on. Drawn at device resolution.
    function drawBackdrop(view, w, h, spec) {
        const scratch = document.createElement("canvas");
        scratch.width = w;
        scratch.height = h;
        const ctx = scratch.getContext("2d");
        const s = w / 1000;
        const preset = spec.backdrop || "interface";
        const dark = preset === "dark";
        const sky = ctx.createLinearGradient(0, 0, 0, h);
        sky.addColorStop(0, dark ? "#1b2024" : "#f4f1ea");
        sky.addColorStop(1, dark ? "#0f1417" : "#dfe6ee");
        ctx.fillStyle = sky;
        ctx.fillRect(0, 0, w, h);
        if (preset === "photo") {
            // A photograph-like ground: large soft colour fields and a horizon, nothing to read.
            const bands = ["#d9a066", "#c96f3a", "#7a9cc6", "#4d6a8a", "#e8d8b8"];
            bands.forEach((colour, i) => {
                const grad = ctx.createRadialGradient(w * (0.15 + i * 0.18), h * (0.3 + (i % 2) * 0.4), 0, w * (0.15 + i * 0.18), h * (0.3 + (i % 2) * 0.4), w * 0.28);
                grad.addColorStop(0, colour + "e6");
                grad.addColorStop(1, colour + "00");
                ctx.fillStyle = grad;
                ctx.fillRect(0, 0, w, h);
            });
            ctx.fillStyle = "rgba(30, 40, 50, 0.55)";
            ctx.fillRect(0, h * 0.72, w, 2 * s);
            finish();
            return;
        }
        if (preset === "text") {
            ctx.fillStyle = dark ? "#e6e2d8" : "#1d262b";
            ctx.font = `${17 * s}px "IBM Plex Sans", "Segoe UI", sans-serif`;
            const words = (spec.lines || []).join("   ");
            for (let i = 0; i < 14; i++) {
                ctx.fillText(words.slice(i * 7) + "   " + words, w * 0.04, h * 0.08 + i * 32 * s);
            }
            finish();
            return;
        }
        const glow = ctx.createRadialGradient(w * 0.78, h * 0.25, 0, w * 0.78, h * 0.25, w * 0.45);
        const ink = dark ? "#e6e2d8" : "#1d262b";
        const inkSoft = dark ? "rgba(230, 226, 216, 0.7)" : "rgba(29, 38, 43, 0.7)";
        glow.addColorStop(0, "rgba(255, 176, 92, 0.85)");
        glow.addColorStop(0.5, "rgba(255, 120, 90, 0.35)");
        glow.addColorStop(1, "rgba(255, 120, 90, 0)");
        ctx.fillStyle = glow;
        ctx.fillRect(0, 0, w, h);
        const cool = ctx.createRadialGradient(w * 0.2, h * 0.85, 0, w * 0.2, h * 0.85, w * 0.4);
        cool.addColorStop(0, "rgba(72, 140, 220, 0.75)");
        cool.addColorStop(1, "rgba(72, 140, 220, 0)");
        ctx.fillStyle = cool;
        ctx.fillRect(0, 0, w, h);

        // Fine rules: refraction and frost show on them first.
        ctx.strokeStyle = dark ? "rgba(230, 226, 216, 0.22)" : "rgba(30, 40, 50, 0.28)";
        ctx.lineWidth = Math.max(1, 1 * s);
        for (let y = h * 0.12; y < h; y += 34 * s) {
            ctx.beginPath();
            ctx.moveTo(0, y);
            ctx.lineTo(w, y);
            ctx.stroke();
        }
        // Colour swatches: chromatic fringes need edges between saturated colours.
        const swatches = ["#e4572e", "#17bebb", "#ffc914", "#2e282a", "#76b041", "#7b5cff"];
        swatches.forEach((colour, i) => {
            ctx.fillStyle = colour;
            ctx.fillRect(w * 0.08 + i * 62 * s, h * 0.18, 44 * s, 44 * s);
        });
        // Text: the glasses and their indices, in the page's own font.
        ctx.fillStyle = ink;
        ctx.font = `600 ${30 * s}px "IBM Plex Sans", "Segoe UI", sans-serif`;
        ctx.fillText(spec.title, w * 0.08, h * 0.12);
        ctx.font = `${17 * s}px "IBM Plex Sans", "Segoe UI", sans-serif`;
        const lines = spec.lines || [];
        lines.forEach((line, i) => ctx.fillText(line, w * 0.08, h * 0.36 + i * 34 * s));
        ctx.font = `${13 * s}px "IBM Plex Sans", "Segoe UI", sans-serif`;
        ctx.fillStyle = inkSoft;
        for (let i = 0; i < 6; i++) {
            ctx.fillText("RefractiveIndex.INFO  CC0 1.0  " + ["n_d", "ν_d", "k(λ)", "dn/dT", "P_g,F", "τ_i"][i], w * 0.55, h * 0.4 + i * 34 * s);
        }
        finish();

        function finish() {
        const { gl } = view;
        gl.bindTexture(gl.TEXTURE_2D, view.texture);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, scratch);
        gl.generateMipmap(gl.TEXTURE_2D);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        view.backdropKey = backdropKey(w, h, spec);
        }
    }

    function backdropKey(w, h, spec) {
        return w + "x" + h + ":" + (spec.backdrop || "interface") + ":" + spec.title + ":" + (spec.lines || []).join("|");
    }

    function request(view) {
        if (view.frame) return;
        const run = () => {
            if (!view.frame) return;
            cancelAnimationFrame(view.frame);
            clearTimeout(view.fallback);
            view.frame = 0;
            draw(view);
        };
        view.frame = requestAnimationFrame(run);
        view.fallback = setTimeout(run, 250);
    }

    function draw(view) {
        const spec = view.spec;
        if (!spec) return;
        const { gl, loc, canvas } = view;
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const w = Math.max(1, Math.min(1800, Math.round(canvas.clientWidth * dpr)));
        const h = Math.max(1, Math.round(w * 0.5625));
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
        }
        if (view.backdropKey !== backdropKey(w, h, spec)) drawBackdrop(view, w, h, spec);
        const s = w / 1000;
        gl.viewport(0, 0, w, h);
        gl.useProgram(view.program);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, view.texture);
        gl.uniform1i(loc.backdrop, 0);
        gl.uniform2f(loc.resolution, w, h);
        gl.uniform3fv(loc.ior, spec.ior);
        gl.uniform3fv(loc.alpha, spec.alpha);
        gl.uniform1f(loc.pxPerMm, spec.pxPerMm * s);
        gl.uniform1f(loc.thickness, spec.thicknessMm);
        gl.uniform1f(loc.bevel, spec.bevelPx * s);
        gl.uniform1f(loc.dome, spec.dome);
        gl.uniform1f(loc.frost, spec.frostPx * s);
        gl.uniform1f(loc.radius, spec.radiusPx * s);
        let halfW = w * spec.widthFraction * 0.5, halfH = h * spec.heightFraction * 0.5, radius = spec.radiusPx * s;
        if (spec.shape === "pill") radius = Math.min(halfW, halfH);
        if (spec.shape === "circle") { halfW = halfH = Math.min(halfW, halfH); radius = halfW; }
        gl.uniform1f(loc.radius, radius);
        gl.uniform2f(loc.half, halfW, halfH);
        const a = spec.lightDegrees * Math.PI / 180;
        gl.uniform2f(loc.light, Math.cos(a), Math.sin(a));
        gl.uniform1f(loc.lightStrength, spec.lightStrength == null ? 1 : spec.lightStrength);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    return {
        // Returns false without WebGL2; the page then shows its CSS approximation alone.
        render(id, spec) {
            const canvas = document.getElementById(id);
            if (!canvas) return false;
            let view = views.get(canvas);
            if (!view) {
                try {
                    view = setup(canvas);
                } catch (error) {
                    console.error("caustikon: ui glass shader failed to build", error);
                    canvas.replaceWith(canvas.cloneNode(false));
                    return false;
                }
                if (!view) return false;
                views.set(canvas, view);
            }
            view.spec = spec;
            request(view);
            return true;
        },
        shaderSource() {
            return FRAGMENT;
        },
        // Saves the canvas as a PNG through a normal download.
        download(id, filename) {
            const canvas = document.getElementById(id);
            if (!canvas) return;
            canvas.toBlob(blob => {
                const url = URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = url;
                a.download = filename || "glass.png";
                document.body.appendChild(a);
                a.click();
                a.remove();
                setTimeout(() => URL.revokeObjectURL(url), 1000);
            }, "image/png");
        },
    };
})();
