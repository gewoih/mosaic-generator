// Shows what a panel size will actually yield before anything is uploaded: which tessera and
// joint the generator will pick, how many modules that is, and how much is left over as margin.
//
// The module range, the joint rule and the detail targets all arrive from the server as data.
// Only the arithmetic is repeated here, and it is the same arithmetic as MosaicLayout.FitCount.
(function () {
    'use strict';

    var dataNode = document.querySelector('[data-layout-data]');
    var form = document.querySelector('[data-layout-form]');
    if (!dataNode || !form) {
        return;
    }

    var data = JSON.parse(dataNode.textContent);
    var readout = form.querySelector('[data-readout]');
    var width = form.querySelector('[data-panel="width"]');
    var height = form.querySelector('[data-panel="height"]');

    // Matches the epsilon in MosaicLayout: an exact fit must not fall to the floor below.
    var FIT_EPSILON = 1e-9;

    function fitCount(panelMm, moduleMm, groutMm) {
        return Math.floor((panelMm + groutMm) / (moduleMm + groutMm) + FIT_EPSILON);
    }

    function target() {
        var checked = form.querySelector('[data-detail]:checked');
        return checked ? data.targets[checked.value] : 0;
    }

    // Closest to the requested count across the short side, ties going to the finer module:
    // falling short of the requested detail is the failure worth avoiding.
    function choose(panelWidthMm, panelHeightMm) {
        var shortSide = Math.min(panelWidthMm, panelHeightMm);
        var wanted = target();
        var best = null;

        data.modules.forEach(function (option) {
            var columns = fitCount(panelWidthMm, option.m, option.g);
            var rows = fitCount(panelHeightMm, option.m, option.g);
            if (columns < 1 || rows < 1 || columns * rows > data.maxModules) {
                return;
            }

            var across = fitCount(shortSide, option.m, option.g);
            if (best === null || Math.abs(across - wanted) < Math.abs(best.across - wanted)) {
                best = {
                    module: option.m,
                    grout: option.g,
                    columns: columns,
                    rows: rows,
                    across: across,
                    wanted: wanted
                };
            }
        });

        return best;
    }

    function ru(value, digits) {
        return value.toLocaleString('ru-RU', {
            minimumFractionDigits: digits || 0,
            maximumFractionDigits: digits || 0
        });
    }

    function update() {
        var panelWidthMm = parseFloat(width && width.value) * 10;
        var panelHeightMm = parseFloat(height && height.value) * 10;

        if (!isFinite(panelWidthMm) || !isFinite(panelHeightMm) || panelWidthMm <= 0 || panelHeightMm <= 0) {
            readout.hidden = true;
            return;
        }

        var choice = choose(panelWidthMm, panelHeightMm);
        if (!choice) {
            readout.hidden = false;
            readout.textContent = 'Для такого панно не находится рабочего модуля.';
            announceAspect(panelWidthMm / panelHeightMm);
            return;
        }

        var step = choice.module + choice.grout;
        var fieldWidth = choice.columns * step - choice.grout;
        var fieldHeight = choice.rows * step - choice.grout;

        var text =
            'Модуль ' + ru(choice.module, 1) + ' мм, шов ' + ru(choice.grout, 1) + ' мм. ' +
            'Сетка ' + choice.columns + ' × ' + choice.rows + ' = ' +
            ru(choice.columns * choice.rows) + ' модулей, ' +
            choice.across + ' по короткой стороне';

        if (choice.across < choice.wanted) {
            text += ' (запрошено ' + choice.wanted + ')';
        }

        text += '. Поля ' + ru((panelWidthMm - fieldWidth) / 2, 1) + ' и ' +
            ru((panelHeightMm - fieldHeight) / 2, 1) + ' мм.';

        readout.hidden = false;
        readout.textContent = text;

        // The crop follows the field, not the panel: the margin would otherwise stretch the
        // photograph by however much the grid failed to divide evenly.
        announceAspect(fieldWidth / fieldHeight);
    }

    function announceAspect(aspect) {
        if (isFinite(aspect) && aspect > 0) {
            document.dispatchEvent(new CustomEvent('mosaic:aspect', { detail: { aspect: aspect } }));
        }
    }

    form.addEventListener('input', update);
    form.addEventListener('change', update);
    update();
})();
