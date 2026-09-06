// Puts a rendered RGBA buffer onto a canvas. Blazor passes byte[] as a Uint8Array.
// When the buffer is smaller than the canvas (a preview pass) it is drawn scaled with smoothing.
window.caustikonCanvas = {
    put(id, width, height, bytes, canvasWidth, canvasHeight) {
        const canvas = document.getElementById(id);
        if (!canvas) {
            return;
        }
        const targetWidth = canvasWidth || width;
        const targetHeight = canvasHeight || height;
        if (canvas.width !== targetWidth || canvas.height !== targetHeight) {
            canvas.width = targetWidth;
            canvas.height = targetHeight;
        }
        const data = bytes instanceof Uint8Array ? bytes : Uint8Array.from(atob(bytes), c => c.charCodeAt(0));
        const image = new ImageData(new Uint8ClampedArray(data.buffer, data.byteOffset, width * height * 4), width, height);
        const context = canvas.getContext("2d");
        if (width === targetWidth && height === targetHeight) {
            context.putImageData(image, 0, 0);
            return;
        }
        const scratch = document.createElement("canvas");
        scratch.width = width;
        scratch.height = height;
        scratch.getContext("2d").putImageData(image, 0, 0);
        context.imageSmoothingEnabled = true;
        context.imageSmoothingQuality = "high";
        context.drawImage(scratch, 0, 0, targetWidth, targetHeight);
    }
};

window.caustikonClipboard = {
    async copy(text) {
        try {
            await navigator.clipboard.writeText(text);
        } catch {
            const area = document.createElement("textarea");
            area.value = text;
            document.body.appendChild(area);
            area.select();
            document.execCommand("copy");
            area.remove();
        }
    }
};

// Converts a client-space point to an SVG's user coordinates, for drawing on the optical bench.
window.caustikonSvg = {
    rect(id) {
        const svg = document.getElementById(id);
        if (!svg) {
            return [0, 0, 1, 1];
        }
        const r = svg.getBoundingClientRect();
        return [r.left, r.top, r.width, r.height];
    },
    // Blazor's wheel listener is passive, so the browser's own ctrl+wheel zoom has to be stopped here.
    guardWheel(id) {
        const box = document.getElementById(id);
        if (box && !box.dataset.wheelGuard) {
            box.dataset.wheelGuard = "1";
            box.addEventListener("wheel", e => { if (e.ctrlKey || e.metaKey) e.preventDefault(); }, { passive: false });
        }
    },
    toUser(id, clientX, clientY) {
        const svg = document.getElementById(id);
        if (!svg) {
            return [0, 0];
        }
        const point = svg.createSVGPoint();
        point.x = clientX;
        point.y = clientY;
        const user = point.matrixTransform(svg.getScreenCTM().inverse());
        return [user.x, user.y];
    }
};

// Remembers the language the visitor picked; English until they pick one.
window.caustikonLang = {
    get() {
        let code = null;
        try { code = localStorage.getItem("caustikon.lang"); } catch { }
        if (code !== "ru" && code !== "en") {
            code = "en";
        }
        document.documentElement.lang = code;
        return code;
    },
    set(code) {
        try { localStorage.setItem("caustikon.lang", code); } catch { }
        document.documentElement.lang = code;
    }
};

// Closes the header menus when a click lands outside them; the menus themselves stop propagation by being the target.
window.caustikonMenus = {
    watch() {
        if (document.body.dataset.menuWatch) return;
        document.body.dataset.menuWatch = "1";
        document.addEventListener("pointerdown", e => {
            if (e.target.closest && e.target.closest(".lang, .give")) return;
            DotNet.invokeMethodAsync("Caustikon.Site", "CloseMenus");
        });
    }
};
