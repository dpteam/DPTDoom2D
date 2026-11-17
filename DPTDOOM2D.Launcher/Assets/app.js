// __BRAND_NAME__ launcher page.
//
// Host bridge, written for MSHTML (IE8 document mode and later):
//
//   js  -> host : store the payload, then navigate to "dptdoom2d://<verb>".
//                 The host cancels that navigation and reads the payload back
//                 with dptdoom2dTakePayload().
//   host -> js  : dptReceive(verb, payload)
//
// The payload is newline-separated fields; the order is shared with
// LauncherSettings.Encode / Decode.
//
// No framework and no modern syntax on purpose: this file has to parse in the
// engine that ships with Windows XP.

(function () {
    'use strict';

    var state = {
        mode: 'single',
        game: '',
        map: '1',
        mapPack: '',
        scale: '2',
        stretch: 'near',
        fullscreen: '0',
        port: '5041',
        password: '',
        master: 'play.dartpower.xyz:25145',
        relaySession: '',
        address: '127.0.0.1',
        layout: '',
        name: 'Player'
    };

    var packs = [];
    var levels = 19;
    var mods = [];
    var enabledMods = [];
    var servers = [];
    var serverFilter = '';
    var protos = [];

    function $(id) { return document.getElementById(id); }

    // ---------------------------------------------------------------- bridge

    function post(verb, payload) {
        window.__dptOutbox = payload === undefined || payload === null ? '' : String(payload);
        try {
            window.location.href = 'dptdoom2d://' + verb;
        } catch (e) {
            // Navigating to an unknown scheme throws in some engines; the host
            // still receives the Navigating event.
        }
    }

    window.dptdoom2dTakePayload = function () {
        var payload = window.__dptOutbox || '';
        window.__dptOutbox = '';
        return payload;
    };

    // ---------------------------------------------------------------- state

    function encodeState() {
        return [
            state.mode, state.game, state.map, state.mapPack, state.scale,
            state.stretch, state.fullscreen, state.port, state.password,
            state.master, state.relaySession, state.address, state.name,
            enabledMods.join('|'), state.layout
        ].join('\n');
    }

    function decodeState(payload) {
        if (!payload) { return; }

        var parts = String(payload).split('\n');
        if (parts.length > 0 && parts[0]) { state.mode = parts[0]; }
        if (parts.length > 1) { state.game = parts[1]; }
        if (parts.length > 2 && parts[2]) { state.map = parts[2]; }
        if (parts.length > 3) { state.mapPack = parts[3]; }
        if (parts.length > 4 && parts[4]) { state.scale = parts[4]; }
        if (parts.length > 5 && parts[5]) { state.stretch = parts[5]; }
        if (parts.length > 6) { state.fullscreen = parts[6]; }
        if (parts.length > 7 && parts[7]) { state.port = parts[7]; }
        if (parts.length > 8) { state.password = parts[8]; }
        if (parts.length > 9) { state.master = parts[9]; }
        if (parts.length > 10) { state.relaySession = parts[10]; }
        if (parts.length > 11) { state.address = parts[11]; }
        if (parts.length > 12) { state.name = parts[12]; }
        if (parts.length > 13 && parts[13]) { enabledMods = parts[13].split('|'); }
        if (parts.length > 14) { state.layout = parts[14]; }
    }

    function applyState() {
        $('executable').value = state.game;
        $('playerName').value = state.name;
        $('port').value = state.port;
        $('password').value = state.password;
        $('masterInline').value = state.master;
        $('masterEndpoint').value = state.master;
        $('relaySession').value = state.relaySession;
        $('relaySessionInline').value = state.relaySession;
        $('address').value = state.address;
        $('scale').value = state.scale;
        $('stretch').value = state.stretch;
        $('fullscreen').checked = state.fullscreen === '1';
        $('layout').value = state.layout;

        setMode(state.mode);
        renderMapPack();
        renderLevels();
    }

    function readState() {
        state.game = $('executable').value;
        state.name = $('playerName').value;
        state.port = $('port').value;
        state.password = $('password').value;
        state.master = $('masterInline').value;
        state.relaySession = $('relaySessionInline').value;
        state.address = $('address').value;
        state.scale = $('scale').value;
        state.stretch = $('stretch').value;
        state.fullscreen = $('fullscreen').checked ? '1' : '0';
        state.layout = $('layout').value;
        state.map = $('map').value;
        state.mapPack = $('mapPack').value;
    }

    function changed() {
        readState();
        post('preview', encodeState());
    }

    // ---------------------------------------------------------------- modes

    function setMode(mode) {
        state.mode = mode;

        var all = document.getElementsByTagName('button');
        var i;

        for (i = 0; i < all.length; ++i) {
            if (all[i].getAttribute('data-mode')) {
                all[i].className = all[i].getAttribute('data-mode') === mode ? 'selected' : '';
            }
        }

        var network = $('network');
        if (mode === 'single') {
            network.className = 'hidden';
        } else {
            network.className = '';
        }

        // When joining by relay the host address is not used, so the field is
        // dimmed rather than hidden: it stays obvious that the address exists.
        $('addressRow').style.opacity = (mode === 'join') ? '0.45' : '1';

        changed();
    }

    // ---------------------------------------------------------------- levels

    function renderMapPack() {
        var select = $('mapPack');
        select.options.length = 0;

        var builtIn = document.createElement('option');
        builtIn.value = '';
        builtIn.text = '(встроенная кампания)';
        select.add(builtIn, null);

        for (var i = 0; i < packs.length; ++i) {
            var option = document.createElement('option');
            option.value = packs[i];
            option.text = packs[i];
            select.add(option, null);
        }

        select.value = state.mapPack || '';
    }

    function renderLevels() {
        var select = $('map');
        var current = state.map || '1';

        select.options.length = 0;

        for (var i = 1; i <= levels; ++i) {
            var option = document.createElement('option');
            var n = i < 10 ? '0' + i : '' + i;
            option.value = '' + i;
            option.text = 'MAP' + n;
            select.add(option, null);
        }

        // Keep the selection if the new pack still has that level.
        select.value = (parseInt(current, 10) <= levels) ? current : '1';
        state.map = select.value;
    }

    // ---------------------------------------------------------------- mods

    function renderMods() {
        var list = $('modList');

        if (mods.length === 0) {
            list.innerHTML = '<div class="mods-empty">Папка Data/Mods пуста. Положи туда WAD или PK3.</div>';
            return;
        }

        var html = '';

        for (var i = 0; i < mods.length; ++i) {
            var checked = enabledMods.indexOf(mods[i].name) >= 0 ? ' checked' : '';

            html += '<label class="mod">' +
                '<input type="checkbox" data-mod="' + escapeHtml(mods[i].name) + '"' + checked + '>' +
                escapeHtml(mods[i].name) +
                '<span class="size">' + escapeHtml(mods[i].size) + '</span>' +
                '</label>';
        }

        list.innerHTML = html;

        var boxes = list.getElementsByTagName('input');

        for (var j = 0; j < boxes.length; ++j) {
            boxes[j].onclick = function () {
                var name = this.getAttribute('data-mod');
                var at = enabledMods.indexOf(name);

                if (this.checked) {
                    if (at < 0) { enabledMods.push(name); }
                } else if (at >= 0) {
                    enabledMods.splice(at, 1);
                }

                changed();
            };
        }
    }

    // ---------------------------------------------------------------- servers

    // ---------------------------------------------------------------- protos

    function parseProtos(payload) {
        protos = [];

        if (!payload) { return; }

        var rows = String(payload).split('\n');

        for (var i = 0; i < rows.length; ++i) {
            if (!rows[i]) { continue; }

            var f = rows[i].split('\t');

            protos.push({
                id: f[0] || '',
                title: f[1] || '?',
                source: f[2] || '',
                description: f[3] || '',
                visual: f[4] !== '0',
                usage: f[5] || ''
            });
        }
    }

    function renderServers() {
        var list = $('serverList');
        var filter = serverFilter.toLowerCase();
        var shown = 0;
        var html = '';

        for (var i = 0; i < servers.length; ++i) {
            var s = servers[i];
            var haystack = (s.name + ' ' + s.map).toLowerCase();

            if (filter && haystack.indexOf(filter) < 0) { continue; }

            ++shown;

            html += '<div class="server" onclick="dptdoom2dJoinServer(' + i + ')">' +
                '<div class="name">' + escapeHtml(s.name) + '</div>' +
                '<div class="meta">' + escapeHtml(s.map) + ' · игроков ' + s.players +
                ' · ' + (s.relay ? 'через релей' : escapeHtml(s.address)) + '</div>' +
                '</div>';
        }

        if (shown === 0) {
            list.innerHTML = servers.length === 0
                ? 'Список пуст. Хост ещё не зарегистрировался?'
                : 'Ничего не найдено по фильтру.';
        } else {
            list.innerHTML = html;
        }
    }

    window.dptdoom2dJoinServer = function (index) {
        var s = servers[index];
        if (!s) { return; }

        state.mode = 'join';
        state.map = s.mapNumber ? '' + s.mapNumber : state.map;

        if (s.relay) {
            state.relaySession = s.id;
        } else {
            state.address = s.address;
            state.port = '' + s.port;
            state.relaySession = '';
        }

        applyState();
        changed();
        setMode('join');
        showStatus('Выбран сервер: ' + s.name + '. Нажми «Запустить игру».', false);
    };

    function escapeHtml(text) {
        return String(text === undefined || text === null ? '' : text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function parseServers(payload) {        servers = [];

        if (!payload) { return; }

        var rows = String(payload).split('\n');

        for (var i = 0; i < rows.length; ++i) {
            if (!rows[i]) { continue; }

            var f = rows[i].split('\t');

            servers.push({
                name: f[0] || '?',
                map: f[1] || 'MAP01',
                mapNumber: f[2] || '',
                players: f[3] || '2',
                address: f[4] || '',
                port: f[5] || '5041',
                relay: f[6] === '1',
                id: f[7] || ''
            });
        }
    }

    // ---------------------------------------------------------------- ui

    function showStatus(text, error) {
        var status = $('status');
        status.innerHTML = escapeHtml(text);
        status.className = 'status' + (error ? ' error' : '');
    }

    function setTab(which) {
        $('playPane').className = which === 'play' ? '' : 'hidden';
        $('serverBrowser').className = which === 'servers' ? '' : 'hidden';
        $('protoBrowser').className = which === 'protos' ? '' : 'hidden';

        $('navPlay').className = which === 'play' ? 'active' : '';
        $('navServers').className = which === 'servers' ? 'active' : '';
        $('navProtos').className = which === 'protos' ? 'active' : '';

        // The footer's launch button belongs to the game tabs, not the showcase.
        $('launch').className = which === 'protos' ? 'launch primary hidden' : 'launch primary';
    }

    // ---------------------------------------------------------------- protos

    function renderProtos() {
        var list = $('protoList');

        if (protos.length === 0) {
            list.innerHTML = 'Список пуст: билд собран без прототипов?';
            return;
        }

        var html = '';

        for (var i = 0; i < protos.length; ++i) {
            var p = protos[i];

            html += '<div class="proto">' +
                '<div class="head"><span class="name">' + escapeHtml(p.title) + '</span>' +
                '<span class="src">' + escapeHtml(p.source) + '</span></div>' +
                '<div class="text">' + escapeHtml(p.description) + '</div>' +
                (p.visual
                    ? '<button onclick="dptdoom2dRunProto(\'' + p.id + '\')" type="button">Запустить</button>'
                    : '<span class="hint">Консольный инструмент: <code>dptdoom2d.exe ' +
                      escapeHtml(p.usage) + '</code></span>') +
                '</div>';
        }

        list.innerHTML = html;
    }

    window.dptdoom2dRunProto = function (id) {
        post('proto', id);
        showStatus('Запускаю прототип ' + id + ' в отдельном окне…', false);
    };

    function applyTheme(theme) {
        document.documentElement.className = theme === 'light' ? 'light' : '';
        $('themeToggle').innerHTML = theme === 'light' ? 'Тёмная тема' : 'Светлая тема';
    }

    // ---------------------------------------------------------------- wiring

    function wire() {
        $('navPlay').onclick = function () { setTab('play'); };
        $('navServers').onclick = function () { setTab('servers'); };
        $('navProtos').onclick = function () { setTab('protos'); };

        $('themeToggle').onclick = function () {
            var next = document.documentElement.className === 'light' ? 'dark' : 'light';
            applyTheme(next);
            post('themesave', next);
        };

        var modes = document.getElementsByTagName('button');
        for (var i = 0; i < modes.length; ++i) {
            (function (button) {
                var mode = button.getAttribute('data-mode');
                if (!mode) { return; }
                button.onclick = function () { setMode(mode); };
            })(modes[i]);
        }

        $('browseExe').onclick = function () { post('browse', 'exe'); };

        $('map').onchange = changed;
        $('mapPack').onchange = function () {
            state.mapPack = $('mapPack').value;
            post('pack', state.mapPack);
        };

        var inputs = ['executable', 'playerName', 'port', 'password', 'masterInline',
                      'relaySessionInline', 'address', 'scale', 'stretch', 'layout'];

        for (var j = 0; j < inputs.length; ++j) {
            (function (id) {
                var element = $(id);
                if (!element) { return; }
                element.onchange = changed;
                element.onkeyup = changed;
            })(inputs[j]);
        }

        $('fullscreen').onclick = changed;

        $('masterSave').onclick = function () {
            state.master = $('masterEndpoint').value;
            $('masterInline').value = state.master;
            post('mastersave', state.master);
        };

        $('refreshServers').onclick = function () {
            post('masterrefresh', $('masterEndpoint').value);
            showStatus('Запрашиваю список у мастера…', false);
        };

        $('serverFilter').onkeyup = function () {
            serverFilter = $('serverFilter').value;
            renderServers();
        };

        $('joinById').onclick = function () {
            var id = $('relaySession').value;

            if (!id || id.length !== 32) {
                showStatus('Нужен 32-символьный id сессии от хоста.', true);
                return;
            }

            state.relaySession = id;
            state.master = $('masterEndpoint').value;
            state.mode = 'join';
            applyState();
            changed();
            showStatus('Готово: нажми «Запустить игру», чтобы подключиться.', false);
        };

        $('launch').onclick = function () {
            readState();
            post('launch', encodeState());
        };

        $('openLog').onclick = function () { post('openlog', ''); };
    }

    // ---------------------------------------------------------------- host -> page

    window.dptReceive = function (verb, payload) {
        payload = payload === undefined || payload === null ? '' : String(payload);

        if (verb === 'state') {
            decodeState(payload);
            applyState();
            renderMods();
            return;
        }

        if (verb === 'theme') {
            applyTheme(payload);
            return;
        }

        if (verb === 'status') {
            var at = payload.lastIndexOf('\n');
            var text = at >= 0 ? payload.substring(0, at) : payload;
            var error = at >= 0 && payload.substring(at + 1) === '1';
            showStatus(text, error);
            return;
        }

        if (verb === 'running') {
            var running = payload === '1';
            $('launch').disabled = running;
            $('launch').innerHTML = running ? 'Игра запущена &nbsp;&#8599;' : 'Запустить игру &nbsp;&#8599;';
            return;
        }

        if (verb === 'hasLog') {
            $('openLog').disabled = payload !== '1';
            return;
        }

        if (verb === 'storage') {
            $('storage').innerHTML = escapeHtml(payload);
            return;
        }

        if (verb === 'game') {
            var fields = payload.split('\n');
            $('gameName').innerHTML = escapeHtml(fields[0] || 'dptdoom2d.exe');
            $('dataHint').innerHTML = escapeHtml(fields[1] || '—');

            packs = (fields[2] || '') ? fields[2].split('\t') : [];
            levels = parseInt(fields[3] || '19', 10);
            if (!levels || levels < 1) { levels = 19; }

            mods = [];
            if (fields[4]) {
                var rows = fields[4].split('\t');
                for (var i = 0; i < rows.length; ++i) {
                    if (!rows[i]) { continue; }
                    var pair = rows[i].split('=');
                    mods.push({ name: pair[0], size: pair[1] || '' });
                }
            }

            renderMapPack();
            renderLevels();
            renderMods();
            return;
        }

        if (verb === 'servers') {
            parseServers(payload);
            renderServers();
            showStatus(servers.length === 0 ? 'Мастер не вернул ни одной сессии.' : 'Сессий: ' + servers.length, false);
            return;
        }

        if (verb === 'protos') {
            parseProtos(payload);
            renderProtos();
            return;
        }

        if (verb === 'fatal') {
            $('fatal').className = '';
            $('fatal').innerHTML = escapeHtml(payload);
            showStatus('Ошибка лаунчера', true);
            return;
        }
    };

    // ---------------------------------------------------------------- start

    function start() {
        wire();

        // Deep link: #servers opens the server browser. Useful on its own, and it
        // gives the screenshot tool a way to reach that tab.
        var hash = String(window.location.hash || '').replace('#', '');
        setTab(hash === 'servers' ? 'servers' : (hash === 'protos' ? 'protos' : 'play'));

        post('ready', '');
    }

    if (document.readyState === 'complete') {
        start();
    } else {
        window.onload = start;
    }
})();
