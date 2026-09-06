// The glass renderer on the GPU: a WebGL2 fragment shader that traces the same scene as GlassRenderer.cs,
// with the same nine wavelengths, Fresnel split, six internal bounces and Beer–Lambert absorption.
// .NET sends the scene once per parameter change; orbiting and zooming happen here without a round trip.
window.caustikonGl = (() => {
    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

    const FRAGMENT = `#version 300 es
precision highp float;
precision highp int;
out vec4 outColour;

uniform vec2 uResolution;
uniform int uSamples;
uniform vec3 uEye, uForward, uRight, uUp;
uniform float uTanHalf;

uniform bool uSphere;
uniform vec3 uCentre;
uniform int uPlaneCount;
uniform vec4 uPlanes[32];

uniform float uIndex[9];
uniform vec3 uWeight[9];
uniform float uAlpha[9];
uniform float uMmPerUnit;
uniform float uTile;
uniform int uBackdrop;
uniform vec3 uKeyPosition;
uniform vec3 uKeyDirection;
uniform float uKeyIntensity;
uniform float uAmbient;

const vec3 RIM = normalize(vec3(2.4, 1.2, 2.0));

// First hit of a ray starting outside the solid; false on a miss.
bool enter(vec3 origin, vec3 direction, out float t, out vec3 normal) {
    if (uSphere) {
        float b = dot(origin, direction);
        float c = dot(origin, origin) - 1.0;
        float disc = b * b - c;
        if (disc < 0.0) { t = 0.0; normal = vec3(0.0); return false; }
        t = -b - sqrt(disc);
        normal = normalize(origin + direction * t);
        return t > 1e-4;
    }
    vec3 o = origin - uCentre;
    float tEntry = -1e30, tExit = 1e30;
    normal = vec3(0.0);
    for (int i = 0; i < 32; i++) {
        if (i >= uPlaneCount) break;
        vec3 n = uPlanes[i].xyz;
        float denominator = dot(n, direction);
        float distance = uPlanes[i].w - dot(n, o);
        if (abs(denominator) < 1e-7) {
            if (distance < 0.0) { t = 0.0; return false; }
            continue;
        }
        float tPlane = distance / denominator;
        if (denominator < 0.0) {
            if (tPlane > tEntry) { tEntry = tPlane; normal = n; }
        } else {
            tExit = min(tExit, tPlane);
        }
    }
    t = tEntry;
    return tEntry > 1e-4 && tEntry <= tExit;
}

// Where a ray starting inside leaves, and the outward normal there.
void leave(vec3 origin, vec3 direction, out float t, out vec3 normal) {
    if (uSphere) {
        t = -2.0 * dot(origin, direction);
        normal = normalize(origin + direction * t);
        return;
    }
    vec3 o = origin - uCentre;
    t = 1e30;
    normal = vec3(0.0, 1.0, 0.0);
    for (int i = 0; i < 32; i++) {
        if (i >= uPlaneCount) break;
        vec3 n = uPlanes[i].xyz;
        float denominator = dot(n, direction);
        if (denominator <= 1e-7) continue;
        float tPlane = (uPlanes[i].w - dot(n, o)) / denominator;
        if (tPlane < t) { t = tPlane; normal = n; }
    }
    if (t > 1e29 || t < 0.0) t = 0.0;
}

// Unpolarized Fresnel power reflectance going from n1 into n2; 1 past the critical angle.
float fresnel(float cosI, float n1, float n2) {
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

vec3 ground(float x, float z) {
    float u = x / uTile, v = z / uTile;
    if (uBackdrop == 1) {
        int band = int(mod(floor(v), 5.0));
        if (band == 0) return vec3(0.80, 0.12, 0.05);
        if (band == 1) return vec3(0.92, 0.62, 0.05);
        if (band == 2) return vec3(0.05, 0.55, 0.20);
        if (band == 3) return vec3(0.03, 0.25, 0.75);
        return vec3(0.86, 0.80, 0.68);
    }
    if (uBackdrop == 2) {
        float fu = abs(u * 4.0 - floor(u * 4.0 + 0.5)), fv = abs(v * 4.0 - floor(v * 4.0 + 0.5));
        float gu = abs(u - floor(u + 0.5)), gv = abs(v - floor(v + 0.5));
        bool heavy = gu < 0.012 || gv < 0.012;
        bool light = fu < 0.05 || fv < 0.05;
        return heavy ? vec3(0.10, 0.28, 0.45) : (light ? vec3(0.55, 0.68, 0.80) : vec3(0.93, 0.94, 0.92));
    }
    if (uBackdrop == 3) return vec3(0.88, 0.85, 0.78);
    if (uBackdrop == 4) return vec3(0.04, 0.045, 0.05);
    bool dark = mod(floor(u) + floor(v), 2.0) == 0.0;
    return dark ? vec3(0.08, 0.085, 0.09) : vec3(0.86, 0.80, 0.68);
}

vec3 sky(vec3 direction) {
    float t = clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 horizon = (uBackdrop == 4 ? vec3(0.05, 0.06, 0.08) : vec3(0.30, 0.33, 0.36)) * uAmbient;
    vec3 zenith = vec3(0.035, 0.045, 0.06) * uAmbient;
    vec3 s = horizon * (1.0 - t) + zenith * t;
    float key = max(0.0, dot(direction, uKeyDirection));
    s += vec3(1.0, 0.97, 0.92) * uKeyIntensity * (40.0 * pow(key, 220.0) + 0.6 * pow(key, 8.0));
    float rim = max(0.0, dot(direction, RIM));
    s += vec3(0.55, 0.65, 0.80) * (8.0 * pow(rim, 140.0));
    return s;
}

// The ground at y = -1 with its pattern, otherwise the sky with the lamp.
vec3 environment(vec3 origin, vec3 direction) {
    if (direction.y < -1e-4) {
        float t = (-1.0 - origin.y) / direction.y;
        vec3 hit = origin + direction * t;
        float distance = length(hit.xz);
        vec3 tile = ground(hit.x, hit.z);
        float lit = uAmbient + uKeyIntensity * 0.12 * max(0.0, normalize(uKeyPosition - hit).y);
        float fade = exp(-distance * 0.18);
        return tile * lit * fade + sky(direction) * (1.0 - fade);
    }
    return sky(direction);
}

vec3 trace(vec3 origin, vec3 direction, float tEntry, vec3 normal, int band) {
    float n = uIndex[band];
    vec3 p = origin + direction * tEntry;
    float cosIncident = clamp(-dot(direction, normal), 0.0, 1.0);
    float entry = fresnel(cosIncident, 1.0, n);
    vec3 result = environment(p, reflect(direction, normal)) * entry;

    vec3 inside = refract(direction, normal, 1.0 / n);
    if (dot(inside, inside) < 0.5) return result;

    float weight = 1.0 - entry;
    vec3 position = p;
    vec3 travel = inside;
    for (int bounce = 0; bounce < 6; bounce++) {
        if (weight <= 1e-3) break;
        float chord; vec3 outward;
        leave(position, travel, chord, outward);
        vec3 q = position + travel * chord;
        weight *= exp(-uAlpha[band] * max(0.0, chord * uMmPerUnit));
        float cosInside = clamp(dot(travel, outward), 0.0, 1.0);
        vec3 exitDir = refract(travel, -outward, n);
        if (dot(exitDir, exitDir) > 0.5) {
            float leaving = fresnel(cosInside, n, 1.0);
            result += environment(q, exitDir) * (weight * (1.0 - leaving));
            weight *= leaving;
        }
        travel = reflect(travel, outward);
        position = q + travel * 1e-4;
    }
    return result;
}

vec3 shade(vec2 pixel) {
    float aspect = uResolution.x / uResolution.y;
    float sx = (pixel.x / uResolution.x * 2.0 - 1.0) * uTanHalf * aspect;
    float sy = (pixel.y / uResolution.y * 2.0 - 1.0) * uTanHalf;
    vec3 direction = normalize(uForward + uRight * sx + uUp * sy);
    float tEntry; vec3 normal;
    if (!enter(uEye, direction, tEntry, normal)) return environment(uEye, direction);
    vec3 colour = vec3(0.0);
    for (int i = 0; i < 9; i++) colour += trace(uEye, direction, tEntry, normal, i) * uWeight[i];
    return colour;
}

float compand(float c) {
    c = clamp(c, 0.0, 1.0);
    return c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

void main() {
    vec3 colour;
    if (uSamples >= 4) {
        colour = (shade(gl_FragCoord.xy + vec2(-0.25, -0.25)) + shade(gl_FragCoord.xy + vec2(0.25, -0.25))
                + shade(gl_FragCoord.xy + vec2(-0.25, 0.25)) + shade(gl_FragCoord.xy + vec2(0.25, 0.25))) * 0.25;
    } else {
        colour = shade(gl_FragCoord.xy);
    }
    outColour = vec4(compand(colour.r), compand(colour.g), compand(colour.b), 1.0);
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
        if (!gl) {
            return null;
        }
        const program = gl.createProgram();
        gl.attachShader(program, compile(gl, gl.VERTEX_SHADER, VERTEX));
        gl.attachShader(program, compile(gl, gl.FRAGMENT_SHADER, FRAGMENT));
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            throw new Error(gl.getProgramInfoLog(program));
        }
        gl.useProgram(program);
        const u = name => gl.getUniformLocation(program, name);
        const view = {
            canvas, gl, program,
            loc: {
                resolution: u("uResolution"), samples: u("uSamples"),
                eye: u("uEye"), forward: u("uForward"), right: u("uRight"), up: u("uUp"), tanHalf: u("uTanHalf"),
                sphere: u("uSphere"), centre: u("uCentre"), planeCount: u("uPlaneCount"), planes: u("uPlanes"),
                index: u("uIndex"), weight: u("uWeight"), alpha: u("uAlpha"), mmPerUnit: u("uMmPerUnit"),
                tile: u("uTile"), backdrop: u("uBackdrop"), keyPosition: u("uKeyPosition"), keyDirection: u("uKeyDirection"),
                keyIntensity: u("uKeyIntensity"), ambient: u("uAmbient"),
            },
            scene: null,
            // Camera: the default matches the CPU renderer's fixed eye at (0, 0.55, -3.6) looking at (0, -0.15, 0).
            yaw: 0, pitch: Math.atan2(0.7, 3.6), distance: Math.hypot(0.7, 3.6),
            target: [0, -0.15, 0],
            dragging: false, lastX: 0, lastY: 0,
            frame: 0, fineTimer: 0, lastMs: 0,
        };
        attach(view);
        return view;
    }

    function attach(view) {
        const c = view.canvas;
        c.addEventListener("pointerdown", e => {
            if (e.button !== 0) return;
            view.dragging = true;
            view.lastX = e.clientX;
            view.lastY = e.clientY;
            c.setPointerCapture(e.pointerId);
        });
        c.addEventListener("pointermove", e => {
            if (!view.dragging) return;
            const dx = e.clientX - view.lastX, dy = e.clientY - view.lastY;
            view.lastX = e.clientX;
            view.lastY = e.clientY;
            view.yaw -= dx * 0.008;
            view.pitch = Math.min(1.45, Math.max(0.02, view.pitch + dy * 0.008));
            request(view, 1);
        });
        const stop = () => {
            if (!view.dragging) return;
            view.dragging = false;
            request(view, 4);
        };
        c.addEventListener("pointerup", stop);
        c.addEventListener("pointercancel", stop);
        c.addEventListener("wheel", e => {
            if (!(e.ctrlKey || e.metaKey)) return;
            e.preventDefault();
            view.distance = Math.min(9, Math.max(1.5, view.distance * Math.exp(e.deltaY * 0.0012)));
            request(view, 1);
            clearTimeout(view.fineTimer);
            view.fineTimer = setTimeout(() => request(view, 4), 160);
        }, { passive: false });
        c.addEventListener("dblclick", () => {
            view.yaw = 0;
            view.pitch = Math.atan2(0.7, 3.6);
            view.distance = Math.hypot(0.7, 3.6);
            request(view, 4);
        });
        if (typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => request(view, 4)).observe(c);
        }
    }

    function fit(view) {
        const c = view.canvas;
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const width = Math.max(1, Math.min(1600, Math.round(c.clientWidth * dpr)));
        const height = Math.max(1, Math.round(width * 0.625));
        if (c.width !== width || c.height !== height) {
            c.width = width;
            c.height = height;
        }
    }

    // One draw per animation frame; a timer stands in when the tab is hidden and frames do not come.
    function request(view, samples) {
        view.pendingSamples = Math.max(view.pendingSamples || 0, samples);
        if (view.frame) return;
        const run = () => {
            if (!view.frame) return;
            cancelAnimationFrame(view.frame);
            clearTimeout(view.fallback);
            view.frame = 0;
            const s = view.pendingSamples;
            view.pendingSamples = 0;
            draw(view, s);
        };
        view.frame = requestAnimationFrame(run);
        view.fallback = setTimeout(run, 250);
    }

    function draw(view, samples) {
        if (!view.scene) return;
        const { gl, loc } = view;
        fit(view);
        gl.viewport(0, 0, view.canvas.width, view.canvas.height);
        const cp = Math.cos(view.pitch), sp = Math.sin(view.pitch), cy = Math.cos(view.yaw), sy = Math.sin(view.yaw);
        const t = view.target;
        const eye = [t[0] + view.distance * sy * cp, t[1] + view.distance * sp, t[2] - view.distance * cy * cp];
        const forward = normalize([t[0] - eye[0], t[1] - eye[1], t[2] - eye[2]]);
        const right = normalize(cross([0, 1, 0], forward));
        const up = cross(forward, right);
        gl.uniform2f(loc.resolution, view.canvas.width, view.canvas.height);
        gl.uniform1i(loc.samples, samples);
        gl.uniform3fv(loc.eye, eye);
        gl.uniform3fv(loc.forward, forward);
        gl.uniform3fv(loc.right, right);
        gl.uniform3fv(loc.up, up);
        gl.uniform1f(loc.tanHalf, Math.tan(30 * Math.PI / 360));
        const started = performance.now();
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        view.lastMs = performance.now() - started;
    }

    function upload(view, scene) {
        const { gl, loc } = view;
        view.scene = scene;
        gl.uniform1i(loc.sphere, scene.sphere ? 1 : 0);
        gl.uniform3fv(loc.centre, scene.centre);
        gl.uniform1i(loc.planeCount, scene.planeCount);
        const planes = new Float32Array(32 * 4);
        planes.set(scene.planes);
        gl.uniform4fv(loc.planes, planes);
        gl.uniform1fv(loc.index, scene.indices);
        gl.uniform3fv(loc.weight, scene.weights);
        gl.uniform1fv(loc.alpha, scene.alphas);
        gl.uniform1f(loc.mmPerUnit, scene.millimetersPerUnit);
        gl.uniform1f(loc.tile, scene.tileUnits);
        gl.uniform1i(loc.backdrop, scene.backdrop);
        gl.uniform3fv(loc.keyPosition, scene.keyPosition);
        gl.uniform3fv(loc.keyDirection, normalize(scene.keyPosition));
        gl.uniform1f(loc.keyIntensity, scene.keyIntensity);
        gl.uniform1f(loc.ambient, scene.ambient);
    }

    function normalize(v) {
        const l = Math.hypot(v[0], v[1], v[2]) || 1;
        return [v[0] / l, v[1] / l, v[2] / l];
    }

    function cross(a, b) {
        return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    }

    return {
        // Returns false when WebGL2 is unavailable, so the caller can fall back to the CPU renderer.
        render(id, scene) {
            const canvas = document.getElementById(id);
            if (!canvas) return false;
            let view = views.get(canvas);
            if (!view) {
                try {
                    view = setup(canvas);
                } catch (error) {
                    console.error("caustikon: shader failed to build", error);
                    // The failed attempt claimed the canvas as WebGL; give the CPU path a fresh one with the same id.
                    canvas.replaceWith(canvas.cloneNode(false));
                    return false;
                }
                if (!view) return false;
                views.set(canvas, view);
            }
            upload(view, scene);
            request(view, 4);
            return true;
        },
        // What the canvas is currently drawing at: backing width, height, and the last frame's time in ms.
        stats(id) {
            const canvas = document.getElementById(id);
            const view = canvas && views.get(canvas);
            return view ? [view.canvas.width, view.canvas.height, view.lastMs] : [0, 0, 0];
        },
    };
})();
