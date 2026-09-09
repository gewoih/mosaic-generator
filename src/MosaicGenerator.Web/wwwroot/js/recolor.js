// Ручная замена артикула на готовом картоне. Клик по коду артикула в таблице
// расхода — выпадающий список всех цветов палитры, отсортированный по ΔE76 от
// заменяемого (ближайшие сверху). Выбор перекрашивает картон/схему/легенду и
// пересчитывает таблицу одним запросом; замены копятся в карту, «Сбросить» —
// очищает её целиком. Геометрия и нумерация схемы не трогаются.
(function () {
    'use strict';

    var root = document.querySelector('[data-recolor="on"]');
    var paletteRaw = document.querySelector('[data-palette]');
    var body = document.querySelector('[data-consumption-body]');
    if (!root || !paletteRaw || !body) { return; }

    var articles;
    try { articles = (JSON.parse(paletteRaw.textContent) || {}).articles || []; }
    catch (e) { return; }
    if (!articles.length) { return; }

    var byArticle = {};
    articles.forEach(function (a) { byArticle[a.article] = a; });

    var fmt0 = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
    var fmt2 = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    var fmt3 = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 3, maximumFractionDigits: 3 });
    var fmt1 = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 });

    var swaps = {};
    var reset = root.querySelector('[data-recolor-reset]');
    var colorStep = document.querySelector('[data-color-step]');

    function deltaE(a, b) {
        var dl = a[0] - b[0], da = a[1] - b[1], db = a[2] - b[2];
        return Math.sqrt(dl * dl + da * da + db * db);
    }

    function swapString() {
        return Object.keys(swaps).map(function (from) { return from + ':' + swaps[from]; }).join(',');
    }

    // Adds the swaps= parameter (and any extras) to a bare controller URL.
    function url(base, extra) {
        var params = [];
        if (extra) { params.push(extra); }
        if (Object.keys(swaps).length) { params.push('swaps=' + encodeURIComponent(swapString())); }
        return params.length ? base + '?' + params.join('&') : base;
    }

    function pins() {
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

    function renderRows(lines) {
        var held = pins();
        body.textContent = '';
        lines.forEach(function (line) {
            var tr = document.createElement('tr');
            tr.dataset.article = line.article;

            tr.appendChild(cell(line.code, 'code'));

            var hold = document.createElement('td');
            hold.className = 'hold';
            var box = document.createElement('input');
            box.type = 'checkbox';
            box.setAttribute('form', 'regenerate');
            box.name = 'PinnedArticles';
            box.value = line.article;
            box.checked = !!held[line.article];
            box.setAttribute('aria-label', 'Не сворачивать ' + line.article);
            hold.appendChild(box);
            tr.appendChild(hold);

            var mono = document.createElement('td');
            mono.className = 'mono';
            var open = document.createElement('button');
            open.type = 'button';
            open.className = 'swap-open';
            open.setAttribute('data-swap-open', '');
            open.textContent = line.article;
            mono.appendChild(open);
            tr.appendChild(mono);

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
    }

    function setText(selector, text) {
        var el = document.querySelector(selector);
        if (el) { el.textContent = text; }
    }

    function setAttr(selector, attr, value) {
        var el = document.querySelector(selector);
        if (el) { el.setAttribute(attr, value); }
    }

    function refreshImages() {
        setAttr('[data-cartoon-img]', 'src', url(root.dataset.cartoonUrl));
        setAttr('[data-cartoon-download]', 'href', url(root.dataset.cartoonUrl, 'download=true'));
        setAttr('[data-cartoon-pdf]', 'href', url(root.dataset.cartoonPdfUrl));

        document.querySelectorAll('[data-panel="scheme"] img').forEach(function (img) {
            img.src = url(root.dataset.schemeUrl);
        });
        document.querySelectorAll('[data-panel="legend"] img').forEach(function (img) {
            img.src = url(root.dataset.legendUrl);
        });
        document.querySelectorAll('a[data-download]').forEach(function (a) {
            a.setAttribute('href', url(
                a.dataset.download === 'scheme' ? root.dataset.schemeUrl : root.dataset.legendUrl,
                'download=true'));
        });
    }

    function refreshTable() {
        fetch(url(root.dataset.consumptionUrl), { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.status === 204 ? null : r.json(); })
            .then(function (data) {
                if (!data) { return; }
                renderRows(data.lines);
                setText('[data-total-area]', fmt3.format(data.totalArea));
                setText('[data-total-mass]', fmt2.format(data.totalMass));
                setText('[data-total-cost]', fmt0.format(data.totalCost));
                setText('[data-color-count]', String(data.lines.length));
            })
            .catch(function () { /* оставляем таблицу как есть */ });
    }

    function apply() {
        var active = Object.keys(swaps).length > 0;
        if (reset) { reset.hidden = !active; }
        if (colorStep) { colorStep.hidden = active; }
        refreshImages();
        refreshTable();
    }

    function closeMenu() {
        var open = root.querySelector('.swap-menu');
        if (open) { open.remove(); }
    }

    function openMenu(trigger) {
        closeMenu();
        var row = trigger.closest('tr');
        if (!row) { return; }
        var from = row.dataset.article;
        var fromLab = (byArticle[from] || {}).lab;
        if (!fromLab) { return; }

        var ranked = articles
            .filter(function (a) { return a.article !== from; })
            .map(function (a) { return { a: a, d: deltaE(fromLab, a.lab) }; })
            .sort(function (x, y) { return x.d - y.d; });

        var menu = document.createElement('div');
        menu.className = 'swap-menu';

        var head = document.createElement('div');
        head.className = 'swap-menu__head';
        head.textContent = 'Заменить ' + from + ' на:';
        menu.appendChild(head);

        var list = document.createElement('div');
        list.className = 'swap-menu__list';
        ranked.forEach(function (item) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'swap-menu__item';
            var dot = document.createElement('span');
            dot.className = 'dot';
            dot.style.background = item.a.hex;
            b.appendChild(dot);
            b.appendChild(document.createTextNode(
                ' ' + item.a.article + ' · ΔE ' + fmt1.format(item.d) + ' · ' + item.a.name));
            b.addEventListener('click', function () {
                if (item.a.article === from) { delete swaps[from]; }
                else { swaps[from] = item.a.article; }
                closeMenu();
                apply();
            });
            list.appendChild(b);
        });
        menu.appendChild(list);

        var mono = trigger.closest('td');
        (mono || row).appendChild(menu);
    }

    body.addEventListener('click', function (event) {
        var trigger = event.target.closest('[data-swap-open]');
        if (trigger && body.contains(trigger)) {
            event.preventDefault();
            if (root.querySelector('.swap-menu') && trigger.closest('td').contains(root.querySelector('.swap-menu'))) {
                closeMenu();
            } else {
                openMenu(trigger);
            }
        }
    });

    document.addEventListener('click', function (event) {
        if (!event.target.closest('.swap-menu') && !event.target.closest('[data-swap-open]')) {
            closeMenu();
        }
    });

    if (reset) {
        reset.addEventListener('click', function () {
            swaps = {};
            closeMenu();
            apply();
        });
    }
})();
