// Мелочи интерфейса без сервера: вкладки выходных листов, память состояния
// свёрнутых блоков и перенос «один раз заданных» параметров на следующее фото.
(function () {
    'use strict';

    var store = {
        get: function (key) {
            try { return window.localStorage.getItem(key); } catch (e) { return null; }
        },
        set: function (key, value) {
            try { window.localStorage.setItem(key, value); } catch (e) { /* приватный режим */ }
        }
    };

    // ---- вкладки картон / легенда / схема ----

    var tabs = document.querySelector('[data-tabs]');
    if (tabs) {
        var buttons = Array.prototype.slice.call(tabs.querySelectorAll('.tabs__tab'));
        var panels = Array.prototype.slice.call(tabs.querySelectorAll('.tabpanel'));

        function activate(name) {
            var known = false;
            buttons.forEach(function (b) {
                var on = b.dataset.tab === name;
                b.setAttribute('aria-selected', on ? 'true' : 'false');
                known = known || on;
            });
            if (!known) { return; }
            panels.forEach(function (p) { p.hidden = p.dataset.panel !== name; });
            store.set('mosaic.tab', name);
        }

        buttons.forEach(function (b) {
            b.addEventListener('click', function () { activate(b.dataset.tab); });
        });

        var saved = store.get('mosaic.tab');
        if (saved) { activate(saved); }
    }

    // ---- память свёрнутых блоков ----

    Array.prototype.forEach.call(document.querySelectorAll('[data-remember-open]'), function (node) {
        var key = 'mosaic.open.' + node.dataset.rememberOpen;
        var saved = store.get(key);
        if (saved === '1') { node.open = true; }
        else if (saved === '0') { node.open = false; }
        node.addEventListener('toggle', function () {
            store.set(key, node.open ? '1' : '0');
        });
    });

    // ---- перенос параметров на следующее фото ----
    //
    // Любая форма сохраняет свои [data-remember] поля при отправке; форма с
    // [data-remember-form] (страница загрузки) ещё и восстанавливает их при открытии,
    // чтобы палитра / цена / запас / откус / цвета не сбрасывались на дефолт с каждым
    // новым снимком.

    function fieldKey(name) { return 'mosaic.field.' + name; }

    Array.prototype.forEach.call(document.querySelectorAll('form'), function (form) {
        var remembered = Array.prototype.slice.call(form.querySelectorAll('[data-remember]'));
        if (!remembered.length) { return; }

        if (form.hasAttribute('data-remember-form')) {
            remembered.forEach(function (el) {
                var saved = store.get(fieldKey(el.name));
                if (saved === null) { return; }
                if (el.type === 'radio') {
                    if (el.value === saved) { el.checked = true; }
                } else {
                    el.value = saved;
                }
            });
            // читалка размера пересчитывается от восстановленного откуса
            form.dispatchEvent(new Event('change', { bubbles: true }));
        }

        form.addEventListener('submit', function () {
            remembered.forEach(function (el) {
                if (el.type === 'radio') {
                    if (el.checked) { store.set(fieldKey(el.name), el.value); }
                } else {
                    store.set(fieldKey(el.name), el.value);
                }
            });
        });
    });

    // ---- лесенка цветов: стрелки ◄► над картоном ----
    //
    // В пределах напечённого диапазона (±5 от авто-числа) картон и таблица расхода
    // подменяются мгновенно из встроенного JSON. За его пределами — кнопка, которая
    // ставит точное число в скрытое поле и просит полный пересчёт (нужны схема с
    // номерами и легенда под новое число).

    (function () {
        var step = document.querySelector('[data-color-step]');
        var raw = document.querySelector('[data-color-ladder]');
        if (!step || !raw) { return; }

        var data;
        try { data = JSON.parse(raw.textContent); } catch (e) { step.hidden = true; return; }
        if (!data || !data.rungs || !Object.keys(data.rungs).length) { step.hidden = true; return; }

        var fmt0 = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
        var fmt2 = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        var fmt3 = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 3, maximumFractionDigits: 3 });
        var fmt1 = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 });

        var img = document.querySelector('[data-cartoon-img]');
        var download = document.querySelector('[data-cartoon-download]');
        var pdf = document.querySelector('[data-cartoon-pdf]');
        var label = step.querySelector('[data-step-label]');
        var hint = step.querySelector('[data-step-hint]');
        var body = document.querySelector('[data-consumption-body]');
        var force = document.querySelector('[data-force-colors]');
        var regenerate = document.getElementById('regenerate');
        var cartoonUrl = step.dataset.cartoonUrl;
        var downloadUrl = step.dataset.downloadUrl;
        var pdfUrl = step.dataset.pdfUrl;

        var current = data.chosen;

        function pinnedArticles() {
            var set = {};
            Array.prototype.forEach.call(
                document.querySelectorAll('[name="PinnedArticles"]:checked'),
                function (box) { set[box.value] = true; });
            return set;
        }

        function cell(text, cls) {
            var td = document.createElement('td');
            if (cls) { td.className = cls; }
            td.textContent = text;
            return td;
        }

        function renderTable(rung) {
            var pins = pinnedArticles();
            body.textContent = '';
            rung.lines.forEach(function (line) {
                var tr = document.createElement('tr');

                tr.appendChild(cell(line.code, 'code'));

                var hold = document.createElement('td');
                hold.className = 'hold';
                var box = document.createElement('input');
                box.type = 'checkbox';
                box.setAttribute('form', 'regenerate');
                box.name = 'PinnedArticles';
                box.value = line.hold;
                box.checked = !!pins[line.hold];
                box.setAttribute('aria-label', 'Не сворачивать ' + line.hold);
                hold.appendChild(box);
                tr.appendChild(hold);

                tr.appendChild(cell(line.hold, 'mono'));

                var name = document.createElement('td');
                var dot = document.createElement('span');
                dot.className = 'dot';
                dot.style.background = line.hex;
                name.appendChild(dot);
                name.appendChild(document.createTextNode(' ' + line.name));
                tr.appendChild(name);

                tr.appendChild(cell(fmt0.format(line.count), 'numeric'));
                tr.appendChild(cell(fmt3.format(line.area), 'numeric'));
                tr.appendChild(cell(fmt2.format(line.mass), 'numeric'));
                tr.appendChild(cell(fmt0.format(line.cost), 'numeric'));

                body.appendChild(tr);
            });

            setText('[data-total-area]', fmt3.format(rung.area));
            setText('[data-total-mass]', fmt2.format(rung.mass));
            setText('[data-total-cost]', fmt0.format(rung.cost));
            setText('[data-color-count]', String(rung.lines.length));

            var note = document.querySelector('[data-reduction-note]');
            if (note && data.total) {
                var share = rung.reassigned / data.total * 100;
                setText('[data-reassigned]', fmt0.format(rung.reassigned));
                setText('[data-reassigned-share]', fmt1.format(share));
            }
        }

        function setText(selector, text) {
            var el = document.querySelector(selector);
            if (el) { el.textContent = text; }
        }

        function apply(n) {
            var rung = data.rungs[String(n)];
            current = n;
            label.textContent = n + ' цв.';
            hint.textContent = '';

            if (rung) {
                var q = '?c=' + n;
                if (img) { img.src = cartoonUrl + q; }
                if (download) { download.href = downloadUrl + '&c=' + n; }
                if (pdf) { pdf.href = pdfUrl + '?c=' + n; }
                renderTable(rung);
                hint.textContent = n === data.auto ? 'авто'
                    : (n > data.auto ? 'больше авто (' + data.auto + ')' : 'меньше авто (' + data.auto + ')');
            } else {
                var button = document.createElement('button');
                button.type = 'button';
                button.className = 'button button--small';
                button.textContent = 'пересчитать при ' + n;
                button.addEventListener('click', function () {
                    if (force) { force.value = String(n); }
                    if (regenerate) { regenerate.submit(); }
                });
                hint.appendChild(button);
            }
        }

        Array.prototype.forEach.call(step.querySelectorAll('[data-step]'), function (arrow) {
            arrow.addEventListener('click', function () {
                var next = current + parseInt(arrow.dataset.step, 10);
                if (next < data.arrowMin || next > data.arrowMax) { return; }
                apply(next);
            });
        });
    })();
})();
