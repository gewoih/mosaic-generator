// The crop frame. A photograph almost never puts its subject in the middle and the panel's
// proportions rarely match the camera's, so which part survives has to be a decision rather than
// a default. The frame keeps the aspect of the mosaic field and slides along whichever axis has
// slack; its centre is posted as a fraction of the source.
(function () {
    'use strict';

    var cropper = document.querySelector('[data-cropper]');
    if (!cropper) {
        return;
    }

    var stage = cropper.querySelector('.cropper__stage');
    var image = cropper.querySelector('.cropper__image');
    var frame = cropper.querySelector('[data-crop-frame]');
    var anchorX = document.querySelector('[data-crop-anchor="x"]');
    var anchorY = document.querySelector('[data-crop-anchor="y"]');
    var photo = document.querySelector('[data-photo]');

    var aspect = 1;
    var anchor = {
        x: clamp(parseFloat(anchorX.value), 0.5),
        y: clamp(parseFloat(anchorY.value), 0.5)
    };

    function clamp(value, fallback) {
        if (!isFinite(value)) {
            return fallback;
        }
        return Math.min(1, Math.max(0, value));
    }

    // Largest rectangle of the requested aspect inside the displayed image, in display pixels.
    // Mirrors ImageCropper: one axis is filled and the other is what is left to slide along.
    function window_() {
        var box = image.getBoundingClientRect();
        if (!box.width || !box.height) {
            return null;
        }

        var w = box.width;
        var h = box.height;

        if (w / h > aspect) {
            w = h * aspect;
        } else {
            h = w / aspect;
        }

        return { imageWidth: box.width, imageHeight: box.height, width: w, height: h };
    }

    function draw() {
        var view = window_();
        if (!view) {
            return;
        }

        var travelX = view.imageWidth - view.width;
        var travelY = view.imageHeight - view.height;

        // The anchor names a point of the source; the window is centred on it and pushed back
        // inside the frame where that would overhang.
        var left = Math.min(travelX, Math.max(0, anchor.x * view.imageWidth - view.width / 2));
        var top = Math.min(travelY, Math.max(0, anchor.y * view.imageHeight - view.height / 2));

        frame.style.left = left + 'px';
        frame.style.top = top + 'px';
        frame.style.width = view.width + 'px';
        frame.style.height = view.height + 'px';
        frame.style.cursor = travelX > 1 || travelY > 1 ? 'move' : 'default';

        anchorX.value = ((left + view.width / 2) / view.imageWidth).toFixed(4);
        anchorY.value = ((top + view.height / 2) / view.imageHeight).toFixed(4);
    }

    function moveTo(clientX, clientY) {
        var box = image.getBoundingClientRect();
        if (!box.width || !box.height) {
            return;
        }

        anchor.x = clamp((clientX - box.left) / box.width, anchor.x);
        anchor.y = clamp((clientY - box.top) / box.height, anchor.y);
        draw();
    }

    var dragging = false;

    stage.addEventListener('pointerdown', function (event) {
        dragging = true;
        stage.setPointerCapture(event.pointerId);
        moveTo(event.clientX, event.clientY);
        event.preventDefault();
    });

    stage.addEventListener('pointermove', function (event) {
        if (dragging) {
            moveTo(event.clientX, event.clientY);
        }
    });

    ['pointerup', 'pointercancel'].forEach(function (name) {
        stage.addEventListener(name, function () {
            dragging = false;
        });
    });

    document.addEventListener('mosaic:aspect', function (event) {
        aspect = event.detail.aspect;
        draw();
    });

    window.addEventListener('resize', draw);
    image.addEventListener('load', draw);

    // A newly picked file is shown straight from the browser: the frame has to be settable before
    // anything is uploaded, not after the first generation comes back wrongly cropped.
    if (photo) {
        photo.addEventListener('change', function () {
            var file = photo.files && photo.files[0];
            if (!file) {
                return;
            }

            if (image.src.indexOf('blob:') === 0) {
                URL.revokeObjectURL(image.src);
            }

            anchor = { x: 0.5, y: 0.5 };
            image.src = URL.createObjectURL(file);
            cropper.hidden = false;
        });
    }

    if (image.complete) {
        draw();
    }
})();
