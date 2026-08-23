tinymce.PluginManager.add('crdtsync', function (editor, url) {
    const hubUrl = editor.getParam('crdtsync_hub_url', 'http://localhost:5042/editorHub');
    const docId = editor.getParam('crdtsync_doc_id', 'default-doc');

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    function invoke(method, ...args) {
        connection.invoke(method, ...args).catch(err => console.error(`Greska (${method}):`, err));
    }

    // <br> i <span class="crdt-tab"> su "prazni" markeri (nemaju tekst), ali u
    // CRDT modelu predstavljaju 1 karakter (vidi textToHtml).
    function isSingleCharMarker(node) {
        return node.nodeName === 'BR'
            || (node.nodeName === 'SPAN' && node.classList.contains('crdt-tab'));
    }

    // Broji koliko CRDT karaktera je PRE trenutne caret pozicije.
    function getCaretIndex() {
        const rng = editor.selection.getRng();
        const targetNode = rng.startContainer;
        const targetOffset = rng.startOffset;
        let count = 0, found = false;

        function walk(node) {
            if (found) return;

            if (node === targetNode) {
                if (node.nodeType === Node.TEXT_NODE) {
                    count += targetOffset;
                } else {
                    for (let i = 0; i < targetOffset && i < node.childNodes.length; i++) walk(node.childNodes[i]);
                }
                found = true;
                return;
            }

            if (node.nodeType === Node.TEXT_NODE) { count += node.nodeValue.length; return; }
            if (isSingleCharMarker(node)) { count += 1; return; }

            for (const child of node.childNodes) {
                walk(child);
                if (found) return;
            }
        }

        walk(editor.getBody());
        return count;
    }

    let pendingCaretIndex = 0;

    // Inverz od getCaretIndex: za dati CRDT indeks vraca {node, offset} u DOM-u.
    function locateCharacterNode(targetIndex) {
        let remaining = targetIndex, resultNode = null, resultOffset = 0;

        function walk(node) {
            if (resultNode) return;

            if (node.nodeType === Node.TEXT_NODE) {
                if (remaining <= node.nodeValue.length) {
                    resultNode = node;
                    resultOffset = remaining;
                    return;
                }
                remaining -= node.nodeValue.length;
                return;
            }

            if (isSingleCharMarker(node)) {
                if (remaining <= 0) {
                    // Pozicija je TACNO ovde - pre ovog markera.
                    resultNode = node.parentNode;
                    resultOffset = Array.prototype.indexOf.call(node.parentNode.childNodes, node);
                    return;
                }
                remaining -= 1;
                return;
            }

            for (const child of Array.from(node.childNodes)) {
                walk(child);
                if (resultNode) return;
            }
        }

        walk(editor.getBody());
        return resultNode ? { node: resultNode, offset: resultOffset } : null;
    }

    function setCaretIndex(targetIndex) {
        const pos = locateCharacterNode(targetIndex);

        if (pos) {
            editor.selection.setCursorLocation(pos.node, pos.offset);
        } else {
            // Prazan sadrzaj ili targetIndex van opsega - kursor na kraj.
            editor.selection.select(editor.getBody(), true);
            editor.selection.collapse(false);
        }
    }

    // POC: boldira/unboldira JEDAN karakter direktno u DOM-u (bez punog
    // re-rendera). Ne hvata sve edge-case-ove (npr. kombinaciju sa italic-om).
    function applyBoldAt(index, isBold) {
        const pos = locateCharacterNode(index);
        if (!pos || pos.node.nodeType !== Node.TEXT_NODE) return;

        const { node, offset } = pos;
        if (offset >= node.nodeValue.length) return; // nema karaktera na toj poziciji

        const parent = node.parentNode;
        const alreadyWrapped = parent.nodeName === 'STRONG'
            && parent.childNodes.length === 1
            && node.nodeValue.length === 1;

        if (isBold === !!alreadyWrapped) return; // nema promene

        if (isBold) {
            const before = node.nodeValue.slice(0, offset);
            const char = node.nodeValue[offset];
            const after = node.nodeValue.slice(offset + 1);

            const frag = document.createDocumentFragment();
            if (before) frag.appendChild(document.createTextNode(before));
            const strong = document.createElement('strong');
            strong.appendChild(document.createTextNode(char));
            frag.appendChild(strong);
            if (after) frag.appendChild(document.createTextNode(after));

            parent.replaceChild(frag, node);
        } else {
            // alreadyWrapped je true - izvuci karakter iz <strong> nazad u plain text.
            parent.parentNode.replaceChild(document.createTextNode(node.nodeValue), parent);
        }
    }

    // Plain string -> HTML: & < > escape-ovani, razmak/tab/newline pretvoreni u
    // nekolabirajuce ekvivalente (HTML parser inace kolabira niz whitespace-a).
    function textToHtml(text) {
        return text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/ /g, '&nbsp;')
            .replace(/\t/g, '<span class="crdt-tab"></span>')
            .split('\n').join('<br>');
    }

    connection.on("ContentChanged", (text) => {
        console.log('Sadrzaj sa servera:', text);
        editor.setContent(textToHtml(text));
        setCaretIndex(pendingCaretIndex);
    });

    connection.on("ReceiveFormatChange", (visibleIndex, isBold, isItalic) => {
        console.log('Format promena sa servera:', visibleIndex, 'bold=', isBold, 'italic=', isItalic);
        applyBoldAt(visibleIndex, isBold);
        // isItalic se za sada ignorise - POC testira samo Bold.
    });

    async function start() {
        await connection.start();
        await connection.invoke('JoinDocument', docId);
    }

    editor.on('init', () => {
        start().catch(err => console.error('Greska pri povezivanju:', err));

        // TinyMCE VEC ima ugradjen Ctrl+B/Cmd+B precac za Bold (core
        // funkcionalnost). Registracija OVDE (u 'init', ne u setup-u) osigurava
        // da se izvrsi POSLE core-ovog default binding-a, pa nas addShortcut
        // pobedi umesto da bude prepisan.
        editor.addShortcut('meta+b', 'Bold (CRDT)', () => {
            const boldIndex = getCaretIndex() - 1;
            if (boldIndex < 0) return;

            const pos = locateCharacterNode(boldIndex);
            const alreadyBold = !!(pos && pos.node.nodeType === Node.TEXT_NODE
                && pos.node.parentNode.nodeName === 'STRONG');

            console.log('Slanje format-a serveru (Bold):', boldIndex, '->', !alreadyBold);
            invoke('SendFormatChange', docId, boldIndex, !alreadyBold, false);
        });
    });

    editor.on('keypress', (e) => {
        if (e.key.length !== 1) return; // Enter/Backspace/itd. - obradjeno u keydown

        const index = getCaretIndex();
        pendingCaretIndex = index + 1; // kursor ide IZA novoubacenog karaktera

        console.log('Slanje insert-a serveru:', e.key, index);
        invoke('Insert', docId, e.key, index);
    });

    const SPECIAL_INSERT_KEYS = { Enter: '\n', Tab: '\t' };

    editor.on('keydown', (e) => {
        if (SPECIAL_INSERT_KEYS[e.key] !== undefined) {
            e.preventDefault(); // inace TinyMCE/browser sam pravi <p>/<br> ili izbaci fokus

            const index = getCaretIndex();
            pendingCaretIndex = index + 1;

            console.log(`Slanje insert-a serveru (${e.key}):`, index);
            invoke('Insert', docId, SPECIAL_INSERT_KEYS[e.key], index);
            return;
        }

        if (e.key !== 'Backspace' && e.key !== 'Delete') return;

        const index = getCaretIndex();
        // Backspace brise karakter PRE kursora, Delete brise karakter POSLE kursora.
        // U oba slucaja kursor posle brisanja ostaje na targetIndex.
        const targetIndex = e.key === 'Backspace' ? index - 1 : index;
        if (targetIndex < 0) return;

        pendingCaretIndex = targetIndex;

        console.log('Slanje delete-a serveru:', targetIndex);
        invoke('Delete', docId, targetIndex);
    });

    return {
        getMetadata: () => ({ name: "CRDT Sync Plugin" })
    };
});
