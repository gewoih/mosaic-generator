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
})();
