// Shows what a panel size will actually yield before anything is uploaded: the tessera and joint
// for the chosen bite length, how many modules that is, and how much is left over as margin.
//
// The module range and the joint rule arrive from the server as data. Only the arithmetic is
// repeated here, and it is the same arithmetic as MosaicLayout.FitCount.
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

    function chosenModule() {
        var checked = form.querySelector('[data-detail]:checked');
        if (!checked) {
            return null;
        }

        var along = parseFloat(checked.value);
        var match = null;
        data.modules.forEach(function (option) {
            if (option.m === along) {
                match = option;
            }
        });

        return match;
    }

    // The panel's grid for exactly the bite length the mosaicist picked — no search, no
    // substitution. If it does not fit, that is reported rather than silently traded for another.
    function choose(panelWidthMm, panelHeightMm) {
        var option = chosenModule();
        if (!option) {
            return null;
        }

        var shortSide = Math.min(panelWidthMm, panelHeightMm);
        var columns = fitCount(panelWidthMm, option.m, option.g);
        var rows = fitCount(panelHeightMm, option.m, option.g);

        return {
            module: option.m,
            grout: option.g,
            columns: columns,
            rows: rows,
            across: fitCount(shortSide, option.m, option.g)
        };
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
            readout.hidden = true;
            return;
        }

        readout.hidden = false;

        if (choice.columns < 1 || choice.rows < 1) {
            readout.textContent =
                'Модуль ' + ru(choice.module, 1) + ' мм больше панно — ни один модуль не поместится.';
            announceAspect(panelWidthMm / panelHeightMm);
            return;
        }

        var total = choice.columns * choice.rows;
        if (total > data.maxModules) {
            readout.textContent =
                'При таком откусе получается ' + ru(total) + ' модулей, максимум ' +
                ru(data.maxModules) + '. Увеличьте откус или уменьшите панно.';
            announceAspect(panelWidthMm / panelHeightMm);
            return;
        }

        var step = choice.module + choice.grout;
        var fieldWidth = choice.columns * step - choice.grout;
        var fieldHeight = choice.rows * step - choice.grout;

        var text =
            'Модуль ' + ru(choice.module, 1) + ' мм, шов ' + ru(choice.grout, 1) + ' мм. ' +
            'Сетка ' + choice.columns + ' × ' + choice.rows + ' = ' +
            ru(total) + ' модулей, ' + choice.across + ' по короткой стороне' +
            '. Поля ' + ru((panelWidthMm - fieldWidth) / 2, 1) + ' и ' +
            ru((panelHeightMm - fieldHeight) / 2, 1) + ' мм.';

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
