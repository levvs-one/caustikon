// The glass renderer on the GPU: a WebGL2 fragment shader that traces the same scene as GlassRenderer.cs,
// with the same nine wavelengths, Fresnel split, six internal bounces and Beer–Lambert absorption.
// .NET sends the scene once per parameter change; orbiting and zooming happen here without a round trip.
window.caustikonGl = (() => {
    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

    // The solid and the Fresnel split, shared by the camera pass and the photon pass.
    const SHAPE_GLSL = `
uniform bool uSphere;
uniform vec3 uCentre;
uniform int uPlaneCount;
uniform vec4 uPlanes[64];

uniform float uIndex[9];
uniform vec3 uWeight[9];
uniform float uAlpha[9];
// The stochastic spectrum: 33 points from 400 to 700 nm, sampled at uBands wavelengths per pixel with a random offset
// inside each band, so discrete colour speckle on strongly dispersing edges averages into a continuous spectrum.
uniform float uIndexTable[33];
uniform vec3 uWeightTable[33];
uniform float uAlphaTable[33];
uniform float uLambdaJitter;
uniform int uBands;
uniform float uMmPerUnit;
uniform float uTile;
uniform int uBackdrop;
uniform vec3 uKeyPosition;
uniform vec3 uKeyDirection;
uniform float uKeyIntensity;
uniform float uAmbient;
uniform float uExtent;     // half the solid's extent, for the contact shadow on the table
uniform float uExposure;
uniform sampler2D uCaustic;   // photons from the lamp that reached the table, per channel
uniform float uCausticNorm;   // scales the map so open table reads as 1
uniform float uRegion;        // the map covers x and z in [-uRegion, uRegion]
uniform bool uCausticReady;

const vec3 RIM = normalize(vec3(2.4, 1.2, 2.0));

// A softbox: a bright rectangle of the sky seen in direction d, soft-edged, as a studio lamp reflects in glass.
float softbox(vec3 d, vec3 centre, float halfAngle) {
    float a = acos(clamp(dot(d, centre), -1.0, 1.0));
    return 1.0 - smoothstep(halfAngle * 0.7, halfAngle, a);
}

// ACES filmic curve (Narkowicz fit): keeps the lamps from clipping to flat white while the glass stays bright.
vec3 tonemap(vec3 c) {
    c *= uExposure;
    return clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
}

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
    for (int i = 0; i < 64; i++) {
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
    for (int i = 0; i < 64; i++) {
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

`;

    const FRAGMENT = `#version 300 es
precision highp float;
precision highp int;
out vec4 outColour;

uniform vec2 uResolution;
uniform int uSamples;
uniform vec3 uEye, uForward, uRight, uUp;
uniform float uTanHalf;

${SHAPE_GLSL}

uniform sampler2D uPanorama;   // the forest around the solid, by direction
uniform bool uPanoramaReady;

float hashF(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float noiseF(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hashF(i), hashF(i + vec2(1.0, 0.0)), f.x), mix(hashF(i + vec2(0.0, 1.0)), hashF(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbmF(vec2 p) { return 0.5 * noiseF(p) + 0.25 * noiseF(p * 2.03) + 0.125 * noiseF(p * 4.1) + 0.0625 * noiseF(p * 8.3); }

vec3 panorama(vec3 d) {
    vec2 uv = vec2(atan(d.z, d.x) / 6.2831853 + 0.5, asin(clamp(d.y, -1.0, 1.0)) / 3.14159265 + 0.5);
    return texture(uPanorama, uv).rgb;
}

vec3 ground(float x, float z) {
    if (uBackdrop == 5) {
        // The forest floor, as in the panorama: moss and needles in patches, in millimetres.
        float n = fbmF(vec2(x, z) * uMmPerUnit * 0.0006);
        return mix(vec3(0.16, 0.22, 0.08), vec3(0.32, 0.36, 0.14), n);
    }
    // Half a tile of offset puts a tile centre, not a tile edge, under the solid: an edge there is magnified into a seam.
    float u = x / uTile + 0.5, v = z / uTile + 0.5;
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
    return dark ? vec3(0.42, 0.43, 0.44) : vec3(0.86, 0.80, 0.68);
}

// The studio: a gradient dome, a key softbox where the lamp stands, a cooler fill opposite, a thin rim from behind.
vec3 sky(vec3 direction) {
    float t = clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 horizon = (uBackdrop == 4 ? vec3(0.05, 0.06, 0.08) : vec3(0.44, 0.46, 0.49)) * uAmbient;
    vec3 zenith = (uBackdrop == 4 ? vec3(0.02, 0.025, 0.035) : vec3(0.30, 0.33, 0.38)) * uAmbient;
    vec3 s = horizon * (1.0 - t) + zenith * t;
    s += vec3(1.0, 0.97, 0.92) * uKeyIntensity * (3.0 * softbox(direction, uKeyDirection, 0.45) + 6.0 * softbox(direction, uKeyDirection, 0.08));
    vec3 fill = normalize(vec3(-uKeyDirection.x, max(0.25, uKeyDirection.y * 0.6), -uKeyDirection.z));
    s += vec3(0.80, 0.86, 1.0) * uAmbient * 1.4 * softbox(direction, fill, 0.7);
    s += vec3(0.55, 0.65, 0.80) * 3.0 * softbox(direction, RIM, 0.12);
    return s;
}

// The ground at y = -1 with its pattern, otherwise the sky with the lamp. In the forest the surroundings come from
// the panorama, with the sun's disc on top and the near floor lit as the table is; the far floor is the panorama's.
vec3 environment(vec3 origin, vec3 direction) {
    if (uBackdrop == 5 && uPanoramaReady) {
        vec3 around = panorama(direction);
        if (direction.y >= -1e-4) {
            return around + vec3(1.0, 0.97, 0.92) * uKeyIntensity * (2.0 * softbox(direction, uKeyDirection, 0.06) + 8.0 * softbox(direction, uKeyDirection, 0.012));
        }
        float t = (-1.0 - origin.y) / direction.y;
        vec3 hit = origin + direction * t;
        float distance = length(hit.xz);
        float near = 1200.0 / uMmPerUnit;
        if (distance > 2.0 * near) return around;
        vec3 lamp = vec3(1.0);
        if (uCausticReady && abs(hit.x) < uRegion && abs(hit.z) < uRegion) {
            vec2 uv = hit.xz / uRegion * 0.5 + 0.5;
            vec2 h = vec2(0.5 / 512.0);
            lamp = (texture(uCaustic, uv + vec2(-h.x, -h.y)).rgb + texture(uCaustic, uv + vec2(h.x, -h.y)).rgb
                  + texture(uCaustic, uv + vec2(-h.x, h.y)).rgb + texture(uCaustic, uv + vec2(h.x, h.y)).rgb) * 0.25 * uCausticNorm;
        }
        float contact = 1.0 - 0.35 * (1.0 - smoothstep(0.1 * uExtent, 1.6 * uExtent, distance));
        vec3 lit = vec3(uAmbient * 0.6 * contact) + uKeyIntensity * 0.9 * max(0.0, uKeyDirection.y) * lamp;
        return mix(ground(hit.x, hit.z) * lit, around, smoothstep(near, 2.0 * near, distance));
    }
    if (direction.y < -1e-4) {
        float t = (-1.0 - origin.y) / direction.y;
        vec3 hit = origin + direction * t;
        float distance = length(hit.xz);
        vec3 tile = ground(hit.x, hit.z);
        // The lamp's share of the table light comes from the photon map: shadow where the solid takes the photons
        // away, caustic where it focuses them, open table at 1. The room's share is darkened a little under the solid.
        vec3 lamp = vec3(1.0);
        if (uCausticReady && abs(hit.x) < uRegion && abs(hit.z) < uRegion) {
            // Four bilinear taps half a texel apart: a small blur that takes the grain out of the map.
            vec2 uv = hit.xz / uRegion * 0.5 + 0.5;
            vec2 h = vec2(0.5 / 512.0);
            lamp = (texture(uCaustic, uv + vec2(-h.x, -h.y)).rgb + texture(uCaustic, uv + vec2(h.x, -h.y)).rgb
                  + texture(uCaustic, uv + vec2(-h.x, h.y)).rgb + texture(uCaustic, uv + vec2(h.x, h.y)).rgb) * 0.25 * uCausticNorm;
        }
        float contact = 1.0 - 0.35 * (1.0 - smoothstep(0.1 * uExtent, 1.6 * uExtent, distance));
        vec3 lit = vec3(uAmbient * 0.55 * contact) + uKeyIntensity * 0.6 * max(0.0, uKeyDirection.y) * lamp;
        float fade = exp(-distance * 0.18);
        return tile * lit * fade + sky(direction) * (1.0 - fade);
    }
    return sky(direction);
}

vec3 trace(vec3 origin, vec3 direction, float tEntry, vec3 normal, float n, float alpha) {
    vec3 p = origin + direction * tEntry;
    float cosIncident = clamp(-dot(direction, normal), 0.0, 1.0);
    float entry = fresnel(cosIncident, 1.0, n);
    vec3 result = environment(p, reflect(direction, normal)) * entry;

    vec3 inside = refract(direction, normal, 1.0 / n);
    if (dot(inside, inside) < 0.5) return result;

    float weight = 1.0 - entry;
    vec3 position = p;
    vec3 travel = inside;
    vec3 lastExit = vec3(0.0);
    vec3 lastPoint = p;
    for (int bounce = 0; bounce < 12; bounce++) {
        if (weight <= 2e-3) break;
        float chord; vec3 outward;
        leave(position, travel, chord, outward);
        vec3 q = position + travel * chord;
        weight *= exp(-alpha * max(0.0, chord * uMmPerUnit));
        float cosInside = clamp(dot(travel, outward), 0.0, 1.0);
        vec3 exitDir = refract(travel, -outward, n);
        if (dot(exitDir, exitDir) > 0.5) {
            float leaving = fresnel(cosInside, n, 1.0);
            result += environment(q, exitDir) * (weight * (1.0 - leaving));
            weight *= leaving;
            lastExit = exitDir;
            lastPoint = q;
        }
        travel = reflect(travel, outward);
        position = q + travel * 1e-4;
    }
    // Light still trapped after twelve bounces: in real glass it leaves later through some edge, so it is sent out along
    // the last exit found, or along its current direction when every face reflected it. Neither is black.
    if (weight > 2e-3) result += (dot(lastExit, lastExit) > 0.5 ? environment(lastPoint, lastExit) : environment(position, travel)) * weight;
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
    float scale = 9.0 / float(uBands);
    for (int i = 0; i < 9; i++) {
        if (i >= uBands) break;
        float t = (float(i) + uLambdaJitter) / float(uBands) * 32.0;
        int j = int(floor(t));
        int k = min(j + 1, 32);
        float f = t - float(j);
        float n = mix(uIndexTable[j], uIndexTable[k], f);
        float alpha = mix(uAlphaTable[j], uAlphaTable[k], f);
        vec3 w = mix(uWeightTable[j], uWeightTable[k], f) * scale;
        colour += trace(uEye, direction, tEntry, normal, n, alpha) * w;
    }
    return colour;
}

float compand(float c) {
    c = clamp(c, 0.0, 1.0);
    return c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

uniform vec2 uJitter;
uniform bool uLinear;

void main() {
    vec3 colour;
    if (uSamples >= 2) {
        vec2 o = gl_FragCoord.xy + uJitter * 0.5;
        colour = (shade(o + vec2(-0.25, -0.25)) + shade(o + vec2(0.25, 0.25))) * 0.5;
    } else {
        colour = shade(gl_FragCoord.xy + uJitter);
    }
    if (uLinear) { outColour = vec4(colour, 1.0); return; }
    colour = tonemap(colour);
    outColour = vec4(compand(colour.r), compand(colour.g), compand(colour.b), 1.0);
}`;

    // Photons from the lamp: a grid on a plane facing the light, one point per cell and colour channel. Each is traced
    // through the solid with the same code as the camera rays and lands on the table as a point; the sum is the light
    // the lamp puts on the table, shadow and caustic included. Photons that miss the solid land too and set the scale.
    const PHOTON_VS = `#version 300 es
precision highp float;
precision highp int;
${SHAPE_GLSL}
uniform vec3 uTravel;        // unit direction the light travels
uniform vec3 uIor3;          // n at 610, 550, 465 nm
uniform vec3 uAlpha3;        // absorption per millimetre at the same wavelengths (uMmPerUnit and uRegion come with the shape block)
uniform float uEmitter;      // half-size of the emitting square, scene units
uniform int uGrid;
uniform vec2 uJitter;
out vec3 vEnergy;

void park() { gl_Position = vec4(4.0, 4.0, 4.0, 1.0); gl_PointSize = 1.0; vEnergy = vec3(0.0); }

void main() {
    int channel = gl_VertexID % 3;
    int cell = gl_VertexID / 3;
    int ix = cell % uGrid, iy = cell / uGrid;
    vec3 d = uTravel;
    vec3 a = normalize(cross(d, abs(d.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 b = cross(d, a);
    vec2 u = ((vec2(float(ix), float(iy)) + 0.5 + uJitter) / float(uGrid) * 2.0 - 1.0) * uEmitter;
    // Start above the table's centre, not the solid's: the beam lands on y = -1, and from the origin it would land
    // shifted by the lamp's slant, leaving the corner of the map nearest the lamp unlit.
    vec3 origin = vec3(0.0, -1.0, 0.0) - d * 6.0 + a * u.x + b * u.y;
    vec3 dir = d;
    float n = channel == 0 ? uIor3.x : (channel == 1 ? uIor3.y : uIor3.z);
    float alpha = channel == 0 ? uAlpha3.x : (channel == 1 ? uAlpha3.y : uAlpha3.z);
    float weight = 1.0;
    float t; vec3 normal;
    if (enter(origin, dir, t, normal)) {
        vec3 p = origin + dir * t;
        float cosI = clamp(-dot(dir, normal), 0.0, 1.0);
        weight *= 1.0 - fresnel(cosI, 1.0, n);
        vec3 inside = refract(dir, normal, 1.0 / n);
        if (dot(inside, inside) < 0.5) { park(); return; }
        vec3 position = p, travel = inside, exitDir = vec3(0.0), q = p;
        bool left = false;
        for (int bounce = 0; bounce < 8; bounce++) {
            float chord; vec3 outward;
            leave(position, travel, chord, outward);
            q = position + travel * chord;
            weight *= exp(-alpha * max(0.0, chord * uMmPerUnit));
            float cosInside = clamp(dot(travel, outward), 0.0, 1.0);
            vec3 e = refract(travel, -outward, n);
            if (dot(e, e) > 0.5) {
                weight *= 1.0 - fresnel(cosInside, n, 1.0);
                exitDir = e;
                left = true;
                break;
            }
            travel = reflect(travel, outward);
            position = q + travel * 1e-4;
        }
        if (!left) { park(); return; }
        origin = q;
        dir = exitDir;
    }
    if (dir.y >= -1e-4) { park(); return; }
    float tg = (-1.0 - origin.y) / dir.y;
    vec3 hit = origin + dir * tg;
    if (abs(hit.x) > uRegion || abs(hit.z) > uRegion) { park(); return; }
    gl_Position = vec4(hit.x / uRegion, hit.z / uRegion, 0.0, 1.0);
    gl_PointSize = 2.0;
    vEnergy = weight * 0.25 * (channel == 0 ? vec3(1.0, 0.0, 0.0) : (channel == 1 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0)));
}`;

    const PHOTON_FS = `#version 300 es
precision highp float;
in vec3 vEnergy;
out vec4 outColour;
void main() { outColour = vec4(vEnergy, 1.0); }`;

    // Shows the running average of the accumulated samples, companded to sRGB.
    const RESOLVE = `#version 300 es
precision highp float;
out vec4 outColour;
uniform sampler2D uAccum;
uniform float uCount;
uniform float uExposure;
float compand(float c) {
    c = clamp(c, 0.0, 1.0);
    return c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}
void main() {
    vec3 c = texelFetch(uAccum, ivec2(gl_FragCoord.xy), 0).rgb / uCount * uExposure;
    c = clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
    outColour = vec4(compand(c.r), compand(c.g), compand(c.b), 1.0);
}`;

    const views = new Map();
    // Each still frame takes four jittered taps; sixteen frames make sixty-four samples per pixel.
    const STILL_SAMPLES = 24;
    const narrowScreen = () => Math.min(window.innerWidth, document.documentElement.clientWidth) < 700;

    // The forest: a panorama traced once from where the solid stands, spruces on the ground around it, and looked
    // up by direction wherever a ray leaves the scene. The trees are in millimetres and scaled to the solid's units,
    // so a bigger solid stands in the same wood, not a smaller one. Lit by the same sun and sky as the solid.
    const FOREST_FS = `#version 300 es
precision highp float;
out vec4 outColour;
uniform vec3 uKeyDirection;    // towards the sun
uniform float uKeyIntensity;
uniform float uAmbient;
uniform vec2 uSize;
uniform float uMmPerUnit;

float hash1(float n) { return fract(sin(n) * 43758.5453123); }
float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash2(i), hash2(i + vec2(1.0, 0.0)), f.x), mix(hash2(i + vec2(0.0, 1.0)), hash2(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) { return 0.5 * noise(p) + 0.25 * noise(p * 2.03) + 0.125 * noise(p * 4.1) + 0.0625 * noise(p * 8.3); }

// A clear sky without its sun: haze at the horizon, blue overhead.
vec3 daylight(vec3 d) {
    float t = clamp(d.y, 0.0, 1.0);
    return mix(vec3(0.78, 0.84, 0.92), vec3(0.24, 0.42, 0.82), pow(t, 0.6)) * uAmbient;
}

struct Hit { float t; vec3 n; };

bool cylinder(vec3 o, vec3 d, vec2 c, float r, float y0, float y1, inout Hit h) {
    vec2 oc = o.xz - c;
    float a = dot(d.xz, d.xz);
    if (a < 1e-9) return false;
    float b = dot(oc, d.xz), cc = dot(oc, oc) - r * r;
    float disc = b * b - a * cc;
    if (disc < 0.0) return false;
    float t = (-b - sqrt(disc)) / a;
    if (t <= 0.0 || t >= h.t) return false;
    float y = o.y + d.y * t;
    if (y < y0 || y > y1) return false;
    vec3 p = o + d * t;
    h.t = t;
    h.n = normalize(vec3(p.x - c.x, 0.0, p.z - c.y));
    return true;
}

// A cone of needles: apex at apexY over c, radius k per unit of height below the apex, down to baseY.
bool cone(vec3 o, vec3 d, vec2 c, float apexY, float k, float baseY, inout Hit h) {
    vec3 co = vec3(o.x - c.x, o.y - apexY, o.z - c.y);
    float k2 = k * k;
    float a = d.x * d.x + d.z * d.z - k2 * d.y * d.y;
    float b = co.x * d.x + co.z * d.z - k2 * co.y * d.y;
    float cc = co.x * co.x + co.z * co.z - k2 * co.y * co.y;
    if (abs(a) < 1e-9) return false;
    float disc = b * b - a * cc;
    if (disc < 0.0) return false;
    float s = sqrt(disc);
    float t1 = (-b - s) / a, t2 = (-b + s) / a;
    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
    for (int i = 0; i < 2; i++) {
        float t = i == 0 ? t1 : t2;
        if (t <= 0.0 || t >= h.t) continue;
        float y = o.y + d.y * t;
        if (y < baseY || y > apexY) continue;
        vec3 p = o + d * t;
        vec2 radial = normalize(vec2(p.x - c.x, p.z - c.y));
        h.t = t;
        h.n = normalize(vec3(radial.x, k, radial.y));
        return true;
    }
    return false;
}

void main() {
    vec2 uv = gl_FragCoord.xy / uSize;
    float phi = (uv.x - 0.5) * 6.2831853, theta = (uv.y - 0.5) * 3.14159265;
    vec3 d = vec3(cos(theta) * cos(phi), sin(theta), cos(theta) * sin(phi));
    vec3 o = vec3(0.0);
    float mm = 1.0 / uMmPerUnit;          // units per millimetre
    Hit h;
    h.t = 1e9;
    h.n = vec3(0.0, 1.0, 0.0);
    int what = 0;                         // 0 sky, 1 ground, 2 trunk, 3 needles
    if (d.y < 0.0) { h.t = -1.0 / d.y; what = 1; }
    vec3 albedo = vec3(0.0);
    // Seventy-two spruces on three rings, 2.4 to 33 metres out, placed by a hash so the same wood stands every time;
    // 6.6 to 14 metres tall, trunks 12 to 27 centimetres across.
    for (int i = 0; i < 72; i++) {
        float fi = float(i);
        float ring = floor(fi / 24.0);
        float ang = (mod(fi, 24.0) + hash1(fi * 3.1 + 1.0) * 0.9) / 24.0 * 6.2831853;
        float dist = (2400.0 + ring * 6000.0) * (1.0 + 0.6 * hash1(fi * 7.7 + 2.0)) * mm;
        vec2 c = vec2(cos(ang), sin(ang)) * dist;
        float height = (6600.0 + 7800.0 * hash1(fi * 5.3 + 3.0)) * mm;
        float trunkR = (120.0 + 150.0 * hash1(fi * 2.9 + 4.0)) * mm;
        float crownBase = -1.0 + height * 0.18, apex = -1.0 + height;
        float k = 0.22 + 0.08 * hash1(fi * 4.4 + 5.0);
        if (cylinder(o, d, c, trunkR, -1.0, crownBase + 150.0 * mm, h)) { what = 2; albedo = vec3(0.30, 0.22, 0.16); }
        if (cone(o, d, c, apex, k, crownBase, h)) { what = 3; albedo = vec3(0.10, 0.20, 0.08) * (0.8 + 0.4 * hash1(fi * 6.6 + 7.0)); }
    }
    vec3 sun = normalize(uKeyDirection);
    if (what == 0) { outColour = vec4(daylight(d), 1.0); return; }
    vec3 p = o + d * h.t;
    vec3 colour;
    if (what == 1) {
        // The forest floor: moss and needles in patches.
        float n = fbm(p.xz * 0.02 / mm * 0.03);
        vec3 moss = mix(vec3(0.16, 0.22, 0.08), vec3(0.32, 0.36, 0.14), n);
        colour = moss * (uAmbient * 0.6 + uKeyIntensity * 0.9 * max(0.0, sun.y));
    } else {
        float n = fbm((p.xz * 0.15 + p.y * 0.05) / mm * 0.03);
        vec3 a = albedo * (0.7 + 0.6 * n);
        float diffuse = max(0.0, dot(h.n, sun));
        float skyLight = 0.5 + 0.5 * h.n.y;
        colour = a * (uAmbient * 0.7 * skyLight + uKeyIntensity * 1.1 * diffuse);
    }
    // Aerial perspective: the far trees pale into the horizon's haze.
    float fog = 1.0 - exp(-h.t * uMmPerUnit / 27000.0);
    colour = mix(colour, daylight(vec3(d.x, max(d.y, 0.02), d.z)), fog);
    outColour = vec4(colour, 1.0);
}`;
    const PANORAMA_WIDTH = 1536, PANORAMA_HEIGHT = 768;

    const DEFAULT_PITCH = Math.atan2(0.7, 3.6);
    const DEFAULT_DISTANCE = 5.2;
    const MIN_DISTANCE = 1.2, MAX_DISTANCE = 14;
    const TAN_HALF = Math.tan(30 * Math.PI / 360);

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

    function link(gl, fragment, vertex) {
        const program = gl.createProgram();
        gl.attachShader(program, compile(gl, gl.VERTEX_SHADER, vertex || VERTEX));
        gl.attachShader(program, compile(gl, gl.FRAGMENT_SHADER, fragment));
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            throw new Error(gl.getProgramInfoLog(program));
        }
        return program;
    }

    function setup(canvas) {
        const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: true });
        if (!gl) {
            return null;
        }
        const program = link(gl, FRAGMENT);
        const u = name => gl.getUniformLocation(program, name);
        // Accumulating many jittered samples needs a float render target; without the extension the still image
        // falls back to four fixed samples per pixel in one pass.
        const floatTargets = !!gl.getExtension("EXT_color_buffer_float");
        const view = {
            canvas, gl, program,
            resolve: floatTargets ? link(gl, RESOLVE) : null,
            forest: link(gl, FOREST_FS),
            panorama: null, panoramaFbo: null, panoramaKey: "",
            photon: floatTargets ? link(gl, PHOTON_FS, PHOTON_VS) : null,
            floatTargets,
            caustic: null, causticFbo: null, causticFrames: 0,
            loc: {
                resolution: u("uResolution"), samples: u("uSamples"), jitter: u("uJitter"), linear: u("uLinear"),
                eye: u("uEye"), forward: u("uForward"), right: u("uRight"), up: u("uUp"), tanHalf: u("uTanHalf"),
                sphere: u("uSphere"), centre: u("uCentre"), planeCount: u("uPlaneCount"), planes: u("uPlanes"),
                index: u("uIndex"), weight: u("uWeight"), alpha: u("uAlpha"), mmPerUnit: u("uMmPerUnit"),
                tile: u("uTile"), backdrop: u("uBackdrop"), keyPosition: u("uKeyPosition"), keyDirection: u("uKeyDirection"),
                keyIntensity: u("uKeyIntensity"), ambient: u("uAmbient"), extent: u("uExtent"), exposure: u("uExposure"),
                caustic: u("uCaustic"), causticNorm: u("uCausticNorm"), region: u("uRegion"), causticReady: u("uCausticReady"),
                indexTable: u("uIndexTable"), weightTable: u("uWeightTable"), alphaTable: u("uAlphaTable"), lambdaJitter: u("uLambdaJitter"), bands: u("uBands"),
                panorama: u("uPanorama"), panoramaReady: u("uPanoramaReady"),
            },
            scene: null,
            accum: null, fbo: null, accumWidth: 0, accumHeight: 0, count: 0,
            // Camera: the default matches the CPU renderer's fixed eye at (0, 0.55, -3.6) looking at (0, -0.15, 0).
            yaw: 0, pitch: DEFAULT_PITCH, distance: DEFAULT_DISTANCE,
            target: [0, -0.15, 0],
            dragging: false, lastX: 0, lastY: 0, pointers: new Map(), pinch: 0,
            frame: 0, fallback: 0, lastMs: 0, mode: "still",
        };
        if (view.photon) {
            const p = name => gl.getUniformLocation(view.photon, name);
            view.photonLoc = {
                sphere: p("uSphere"), centre: p("uCentre"), planeCount: p("uPlaneCount"), planes: p("uPlanes"),
                travel: p("uTravel"), ior3: p("uIor3"), alpha3: p("uAlpha3"), mmPerUnit: p("uMmPerUnit"),
                region: p("uRegion"), emitter: p("uEmitter"), grid: p("uGrid"), jitter: p("uJitter"),
            };
        }
        if (view.resolve) {
            view.resolveLoc = { accum: gl.getUniformLocation(view.resolve, "uAccum"), count: gl.getUniformLocation(view.resolve, "uCount"), exposure: gl.getUniformLocation(view.resolve, "uExposure") };
        }
        {
            const f = name => gl.getUniformLocation(view.forest, name);
            view.forestLoc = { keyDirection: f("uKeyDirection"), keyIntensity: f("uKeyIntensity"), ambient: f("uAmbient"), size: f("uSize"), mmPerUnit: f("uMmPerUnit") };
        }
        attach(view);
        return view;
    }

    function camera(view) {
        const cp = Math.cos(view.pitch), sp = Math.sin(view.pitch), cy = Math.cos(view.yaw), sy = Math.sin(view.yaw);
        const t = view.target;
        const eye = [t[0] + view.distance * sy * cp, t[1] + view.distance * sp, t[2] - view.distance * cy * cp];
        const forward = normalize([t[0] - eye[0], t[1] - eye[1], t[2] - eye[2]]);
        const right = normalize(cross([0, 1, 0], forward));
        const up = cross(forward, right);
        return { eye, forward, right, up };
    }

    // The world-space direction through a point on the canvas, in CSS pixels from its top-left corner.
    function rayThrough(view, px, py) {
        const { forward, right, up } = camera(view);
        const w = view.canvas.clientWidth, h = view.canvas.clientHeight;
        const aspect = w / h;
        const sx = (px / w * 2 - 1) * TAN_HALF * aspect;
        const sy = (1 - py / h * 2) * TAN_HALF;
        return normalize([forward[0] + right[0] * sx + up[0] * sy, forward[1] + right[1] * sx + up[1] * sy, forward[2] + right[2] * sx + up[2] * sy]);
    }

    function attach(view) {
        const c = view.canvas;
        const distance = () => {
            const p = [...view.pointers.values()];
            return p.length < 2 ? 0 : Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
        };
        c.addEventListener("pointerdown", e => {
            if (e.button !== 0) return;
            view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            c.setPointerCapture(e.pointerId);
            if (view.pointers.size === 2) {
                view.dragging = false;
                view.pinch = distance();
                return;
            }
            view.dragging = true;
            view.lastX = e.clientX;
            view.lastY = e.clientY;
        });
        c.addEventListener("pointermove", e => {
            if (view.pointers.has(e.pointerId)) view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                // Two fingers: the distance between them dollies the camera.
                const d = distance();
                if (view.pinch > 0 && d > 0) {
                    view.distance = Math.min(MAX_DISTANCE, Math.max(MIN_DISTANCE, view.distance * view.pinch / d));
                    view.pinch = d;
                    request(view, "moving");
                }
                return;
            }
            if (!view.dragging) return;
            const dx = e.clientX - view.lastX, dy = e.clientY - view.lastY;
            view.lastX = e.clientX;
            view.lastY = e.clientY;
            view.yaw -= dx * 0.008;
            view.pitch = Math.min(1.45, Math.max(0.02, view.pitch + dy * 0.008));
            request(view, "moving");
        });
        const stop = e => {
            if (e && e.pointerId !== undefined) view.pointers.delete(e.pointerId);
            if (!view.dragging && view.pointers.size > 0) return;
            view.dragging = false;
            view.pinch = 0;
            request(view, "still");
        };
        c.addEventListener("pointerup", stop);
        c.addEventListener("pointercancel", stop);
        c.addEventListener("wheel", e => {
            if (!(e.ctrlKey || e.metaKey)) return;
            e.preventDefault();
            // Dolly toward the point under the cursor: the eye slides along that ray and the target follows,
            // so whatever the cursor is on stays under it while the view closes in.
            const factor = Math.exp(e.deltaY * 0.0012);
            const next = Math.min(MAX_DISTANCE, Math.max(MIN_DISTANCE, view.distance * factor));
            const f = next / view.distance;
            const rect = c.getBoundingClientRect();
            const dir = rayThrough(view, e.clientX - rect.left, e.clientY - rect.top);
            const { eye, forward } = camera(view);
            const move = view.distance * (1 - f);
            const eye2 = [eye[0] + dir[0] * move, eye[1] + dir[1] * move, eye[2] + dir[2] * move];
            view.target = [eye2[0] + forward[0] * next, eye2[1] + forward[1] * next, eye2[2] + forward[2] * next];
            view.distance = next;
            request(view, "moving");
            clearTimeout(view.settle);
            view.settle = setTimeout(() => request(view, "still"), 160);
        }, { passive: false });
        c.addEventListener("dblclick", () => {
            view.yaw = 0;
            view.pitch = DEFAULT_PITCH;
            view.distance = DEFAULT_DISTANCE;
            view.target = [0, -0.15, 0];
            request(view, "still");
        });
        if (typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => request(view, "still")).observe(c);
        }
    }

    function fit(view) {
        const c = view.canvas;
        // Phones draw at one device pixel per CSS pixel: four times fewer rays, and the picture is small anyway.
        const dpr = narrowScreen() ? 1 : Math.min(window.devicePixelRatio || 1, 1.5);
        const width = Math.max(1, Math.min(1400, Math.round(c.clientWidth * dpr)));
        const height = Math.max(1, Math.round(width * 0.625));
        if (c.width !== width || c.height !== height) {
            c.width = width;
            c.height = height;
        }
    }

    // One draw per animation frame; a timer stands in when the tab is hidden and frames do not come.
    // "moving": one quick sample. "still": start accumulating from scratch. "continue": add one more sample.
    function request(view, mode) {
        if (mode !== "continue") {
            view.mode = mode;
            view.count = 0;
        }
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

    function ensureAccum(view) {
        const { gl } = view;
        const w = view.canvas.width, h = view.canvas.height;
        if (view.accum && view.accumWidth === w && view.accumHeight === h) return;
        if (view.accum) {
            gl.deleteTexture(view.accum);
            gl.deleteFramebuffer(view.fbo);
        }
        view.accum = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, view.accum);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, w, h, 0, gl.RGBA, gl.FLOAT, null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        view.fbo = gl.createFramebuffer();
        gl.bindFramebuffer(gl.FRAMEBUFFER, view.fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, view.accum, 0);
        if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) {
            view.floatTargets = false;
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        view.accumWidth = w;
        view.accumHeight = h;
        view.count = 0;
    }

    const CAUSTIC_SIZE = 512;
    const REGION = 3.0;
    const CAUSTIC_FRAMES = 8;

    function ensureCaustic(view) {
        const { gl } = view;
        if (view.caustic) return;
        view.caustic = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, view.caustic);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, CAUSTIC_SIZE, CAUSTIC_SIZE, 0, gl.RGBA, gl.FLOAT, null);
        // Nearest, not linear: a 32-bit float texture is only filterable with OES_texture_float_linear, and without
        // it a linear-filtered one is incomplete and reads as zero, which put the whole map in shadow on such GPUs.
        // The shader takes four taps half a texel apart, which is the smoothing the map needs.
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        view.causticFbo = gl.createFramebuffer();
        gl.bindFramebuffer(gl.FRAMEBUFFER, view.causticFbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, view.caustic, 0);
        if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) {
            view.photon = null;
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        view.causticFrames = 0;
    }

    // One more frame of photons into the map. The grid is jittered each frame, so the map smooths as frames add up.
    function photonFrame(view) {
        const { gl, photonLoc: p, scene } = view;
        ensureCaustic(view);
        if (!view.photon) return;
        const grid = narrowScreen() ? 192 : 288;
        // The emitter is a square across the beam, so on the table it lies turned by the lamp's azimuth: its half-width
        // must reach the map's corners, √2 of the region, whatever the azimuth.
        const emitter = REGION * 1.5;
        gl.useProgram(view.photon);
        gl.bindFramebuffer(gl.FRAMEBUFFER, view.causticFbo);
        gl.viewport(0, 0, CAUSTIC_SIZE, CAUSTIC_SIZE);
        if (view.causticFrames === 0) {
            gl.disable(gl.BLEND);
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
        }
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE);
        gl.uniform1i(p.sphere, scene.sphere ? 1 : 0);
        gl.uniform3fv(p.centre, scene.centre);
        gl.uniform1i(p.planeCount, scene.planeCount);
        const planes = new Float32Array(64 * 4);
        planes.set(scene.planes);
        gl.uniform4fv(p.planes, planes);
        const key = normalize(scene.keyPosition);
        gl.uniform3f(p.travel, -key[0], -key[1], -key[2]);
        gl.uniform3f(p.ior3, scene.indices[6], scene.indices[4], scene.indices[2]);
        gl.uniform3f(p.alpha3, scene.alphas[6], scene.alphas[4], scene.alphas[2]);
        gl.uniform1f(p.mmPerUnit, scene.millimetersPerUnit);
        gl.uniform1f(p.region, REGION);
        gl.uniform1f(p.emitter, emitter);
        gl.uniform1i(p.grid, grid);
        gl.uniform2f(p.jitter, Math.random() - 0.5, Math.random() - 0.5);
        gl.drawArrays(gl.POINTS, 0, grid * grid * 3);
        gl.disable(gl.BLEND);
        view.causticFrames++;
        // Open table: photons per texel from the grid's density on the emitter, projected onto the table.
        const perUnitEmitter = (grid * grid) / (4 * emitter * emitter);
        const texel = (2 * REGION / CAUSTIC_SIZE) ** 2;
        const expected = perUnitEmitter * Math.abs(key[1]) * texel * view.causticFrames;
        view.causticNorm = 1 / expected;
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        gl.useProgram(view.program);
    }

    // The forest panorama, traced again only when the sun, the room or the solid's scale changes.
    function ensurePanorama(view) {
        const { gl, scene } = view;
        if (!scene || scene.backdrop !== 5) return;
        const key = normalize(scene.keyPosition);
        const stamp = key.map(v => v.toFixed(3)).join(",") + "|" + scene.keyIntensity + "|" + scene.ambient + "|" + scene.millimetersPerUnit;
        if (view.panorama && view.panoramaKey === stamp) return;
        if (!view.panorama) {
            view.panorama = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, view.panorama);
            if (view.floatTargets) gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA16F, PANORAMA_WIDTH, PANORAMA_HEIGHT, 0, gl.RGBA, gl.HALF_FLOAT, null);
            else gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, PANORAMA_WIDTH, PANORAMA_HEIGHT, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            view.panoramaFbo = gl.createFramebuffer();
            gl.bindFramebuffer(gl.FRAMEBUFFER, view.panoramaFbo);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, view.panorama, 0);
            if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) {
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                gl.deleteTexture(view.panorama);
                view.panorama = null;
                view.panoramaKey = "failed";
                return;
            }
        }
        const f = view.forestLoc;
        gl.useProgram(view.forest);
        gl.bindFramebuffer(gl.FRAMEBUFFER, view.panoramaFbo);
        gl.viewport(0, 0, PANORAMA_WIDTH, PANORAMA_HEIGHT);
        gl.disable(gl.BLEND);
        gl.uniform3fv(f.keyDirection, key);
        gl.uniform1f(f.keyIntensity, scene.keyIntensity);
        gl.uniform1f(f.ambient, scene.ambient);
        gl.uniform2f(f.size, PANORAMA_WIDTH, PANORAMA_HEIGHT);
        gl.uniform1f(f.mmPerUnit, scene.millimetersPerUnit);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        view.panoramaKey = stamp;
        gl.useProgram(view.program);
    }

    function bindPanorama(view) {
        const { gl, loc } = view;
        const ready = view.scene && view.scene.backdrop === 5 && view.panorama && view.panoramaKey !== "failed";
        gl.activeTexture(gl.TEXTURE2);
        gl.bindTexture(gl.TEXTURE_2D, ready ? view.panorama : null);
        gl.uniform1i(loc.panorama, 2);
        gl.uniform1i(loc.panoramaReady, ready ? 1 : 0);
        gl.activeTexture(gl.TEXTURE0);
    }

    function bindCaustic(view) {
        const { gl, loc } = view;
        gl.activeTexture(gl.TEXTURE1);
        gl.bindTexture(gl.TEXTURE_2D, view.caustic);
        gl.uniform1i(loc.caustic, 1);
        gl.uniform1f(loc.causticNorm, view.causticNorm || 0);
        gl.uniform1f(loc.region, REGION);
        gl.uniform1i(loc.causticReady, view.causticFrames > 0 ? 1 : 0);
    }

    function setCamera(view) {
        const { gl, loc } = view;
        const { eye, forward, right, up } = camera(view);
        gl.uniform2f(loc.resolution, view.canvas.width, view.canvas.height);
        gl.uniform3fv(loc.eye, eye);
        gl.uniform3fv(loc.forward, forward);
        gl.uniform3fv(loc.right, right);
        gl.uniform3fv(loc.up, up);
        gl.uniform1f(loc.tanHalf, TAN_HALF);
    }

    function draw(view) {
        if (!view.scene) return;
        const { gl, loc } = view;
        fit(view);
        const w = view.canvas.width, h = view.canvas.height;
        const started = performance.now();
        if (view.floatTargets && (view.causticFrames === 0 || (view.mode !== "moving" && view.causticFrames < CAUSTIC_FRAMES))) {
            photonFrame(view);
        }
        ensurePanorama(view);
        gl.useProgram(view.program);
        setCamera(view);
        if (view.floatTargets) bindCaustic(view);
        bindPanorama(view);

        if (view.mode === "moving" || !view.floatTargets) {
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.viewport(0, 0, w, h);
            gl.disable(gl.BLEND);
            gl.uniform1i(loc.samples, view.mode === "moving" ? 1 : 2);
            gl.uniform2f(loc.jitter, 0, 0);
            gl.uniform1i(loc.bands, view.mode === "moving" ? 3 : 9);
            gl.uniform1f(loc.lambdaJitter, 0.5);
            gl.uniform1i(loc.linear, 0);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
            view.lastMs = performance.now() - started;
            return;
        }

        ensureAccum(view);
        if (!view.floatTargets) {
            draw(view);
            return;
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, view.fbo);
        gl.viewport(0, 0, w, h);
        if (view.count === 0) {
            gl.disable(gl.BLEND);
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
        }
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE);
        gl.uniform1i(loc.samples, 2);
        gl.uniform2f(loc.jitter, Math.random() - 0.5, Math.random() - 0.5);
        gl.uniform1i(loc.bands, 9);
        gl.uniform1f(loc.lambdaJitter, Math.random());
        gl.uniform1i(loc.linear, 1);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        view.count++;

        gl.disable(gl.BLEND);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        gl.viewport(0, 0, w, h);
        gl.useProgram(view.resolve);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, view.accum);
        gl.uniform1i(view.resolveLoc.accum, 0);
        gl.uniform1f(view.resolveLoc.count, view.count);
        gl.uniform1f(view.resolveLoc.exposure, view.scene.exposure);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.useProgram(view.program);
        view.lastMs = performance.now() - started;

        if (view.count < STILL_SAMPLES) {
            request(view, "continue");
        }
    }

    function upload(view, scene) {
        const { gl, loc } = view;
        view.scene = scene;
        gl.useProgram(view.program);
        gl.uniform1i(loc.sphere, scene.sphere ? 1 : 0);
        gl.uniform3fv(loc.centre, scene.centre);
        gl.uniform1i(loc.planeCount, scene.planeCount);
        const planes = new Float32Array(64 * 4);
        planes.set(scene.planes);
        gl.uniform4fv(loc.planes, planes);
        view.causticFrames = 0;
        gl.uniform1fv(loc.index, scene.indices);
        gl.uniform3fv(loc.weight, scene.weights);
        gl.uniform1fv(loc.alpha, scene.alphas);
        gl.uniform1fv(loc.indexTable, scene.spectrumIndices);
        gl.uniform3fv(loc.weightTable, scene.spectrumWeights);
        gl.uniform1fv(loc.alphaTable, scene.spectrumAlphas);
        gl.uniform1f(loc.mmPerUnit, scene.millimetersPerUnit);
        gl.uniform1f(loc.tile, scene.tileUnits);
        gl.uniform1i(loc.backdrop, scene.backdrop);
        gl.uniform3fv(loc.keyPosition, scene.keyPosition);
        gl.uniform3fv(loc.keyDirection, normalize(scene.keyPosition));
        gl.uniform1f(loc.keyIntensity, scene.keyIntensity);
        gl.uniform1f(loc.ambient, scene.ambient);
        gl.uniform1f(loc.extent, scene.extent);
        gl.uniform1f(loc.exposure, scene.exposure);
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
            request(view, "still");
            return true;
        },
        // Steps the camera closer (factor below 1) or farther, as the + and − buttons do.
        dolly(id, factor) {
            const canvas = document.getElementById(id);
            const view = canvas && views.get(canvas);
            if (!view) return;
            view.distance = Math.min(MAX_DISTANCE, Math.max(MIN_DISTANCE, view.distance * factor));
            request(view, "still");
        },
        // Backing width and height, the last frame's time in ms, and how many samples per pixel the picture holds.
        stats(id) {
            const canvas = document.getElementById(id);
            const view = canvas && views.get(canvas);
            return view ? [view.canvas.width, view.canvas.height, view.lastMs, view.floatTargets ? view.count : 4] : [0, 0, 0, 0];
        },
    };
})();
