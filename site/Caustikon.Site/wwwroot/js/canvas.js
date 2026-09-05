// Puts a rendered RGBA buffer onto a canvas. Blazor passes byte[] as a Uint8Array.
window.caustikonCanvas = {
    put(id, width, height, bytes) {
        const canvas = document.getElementById(id);
        if (!canvas) {
            return;
        }
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
        const data = bytes instanceof Uint8Array ? bytes : Uint8Array.from(atob(bytes), c => c.charCodeAt(0));
        const image = new ImageData(new Uint8ClampedArray(data.buffer, data.byteOffset, width * height * 4), width, height);
        canvas.getContext("2d").putImageData(image, 0, 0);
    }
};
