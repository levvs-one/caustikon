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
