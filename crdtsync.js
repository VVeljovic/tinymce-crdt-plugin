tinymce.PluginManager.add('crdtsync', function (editor, url) {
    const hubUrl = editor.getParam('crdtsync_hub_url', 'http://localhost:5042/editorHub');
    const docId = editor.getParam('crdtsync_doc_id', 'default-doc');

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    function isSingleCharMarker(node) {
        return node.nodeName === 'BR'
            || (node.nodeName === 'SPAN' && node.classList.contains('crdt-tab'));
    }

    function getCaretIndex() {
        const rng = editor.selection.getRng();
        const targetNode = rng.startContainer;
        const targetOffset = rng.startOffset;

        let count = 0;
        let found = false;

        function walk(node) {
            if (found) return;

            if (node === targetNode) {
                if (node.nodeType === Node.TEXT_NODE) {
                    count += targetOffset;
                } else {
                    for (let i = 0; i < targetOffset && i < node.childNodes.length; i++) {
                        walk(node.childNodes[i]);
                    }
                }
                found = true;
                return;
            }

            if (node.nodeType === Node.TEXT_NODE) {
                count += node.nodeValue.length;
                return;
            }

            if (isSingleCharMarker(node)) {
                count += 1;
                return;
            }

            for (const child of node.childNodes) {
                walk(child);
                if (found) return;
            }
        }

        walk(editor.getBody());
        return count;
    }

    let pendingCaretIndex = 0;

    function setCaretIndex(targetIndex) {
        const body = editor.getBody();

        let remaining = targetIndex;
        let resultNode = null;
        let resultOffset = 0;

        function walk(node) {
            if (resultNode) return;

            if (node.nodeType === Node.TEXT_NODE) {
                const len = node.nodeValue.length;
                if (remaining <= len) {
                    resultNode = node;
                    resultOffset = remaining;
                    return;
                }
                remaining -= len;
                return;
            }

            if (isSingleCharMarker(node)) {
                if (remaining <= 0) {
                    // Kursor treba da stoji TACNO ovde - pre ovog markera.
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

        walk(body);

        if (resultNode) {
            editor.selection.setCursorLocation(resultNode, resultOffset);
        } else {
            // Prazan sadrzaj ili targetIndex van opsega - kursor na kraj.
            editor.selection.select(body, true);
            editor.selection.collapse(false);
        }
    }

    function textToHtml(text) {
        const escaped = text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/ /g, '&nbsp;')
            .replace(/\t/g, '<span class="crdt-tab"></span>');

        return escaped.split('\n').join('<br>');
    }

    // --- SignalR events ---
    connection.on("ContentChanged", (text) => {
        console.log('Sadrzaj sa servera:', text);
        editor.setContent(textToHtml(text));
        setCaretIndex(pendingCaretIndex);
    });

    async function start() {
        await connection.start();
        await connection.invoke('JoinDocument', docId);
    }

    // --- Editor event handleri ---
    editor.on('init', () => {
        start().catch(err => console.error('Greska pri povezivanju:', err));
    });

    editor.on('keypress', (e) => {
        if (e.key.length !== 1) return; // Backspace/Enter/Shift itd. - Enter ide posebno kasnije

        const index = getCaretIndex();
        pendingCaretIndex = index + 1; // kursor ide IZA novoubacenog karaktera

        console.log('Slanje insert-a serveru:', e.key, index);
        connection.invoke('Insert', docId, e.key, index)
            .catch(err => console.error('Greska pri slanju:', err));
    });

    editor.on('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();

            const index = getCaretIndex();
            pendingCaretIndex = index + 1;

            console.log('Slanje insert-a serveru (Enter):', index);
            connection.invoke('Insert', docId, '\n', index)
                .catch(err => console.error('Greska pri slanju:', err));
            return;
        }

        if (e.key === 'Tab') {
            e.preventDefault();

            const index = getCaretIndex();
            pendingCaretIndex = index + 1;

            console.log('Slanje insert-a serveru (Tab):', index);
            connection.invoke('Insert', docId, '\t', index)
                .catch(err => console.error('Greska pri slanju:', err));
            return;
        }

        if (e.key !== 'Backspace' && e.key !== 'Delete') return;

        const index = getCaretIndex();
        const targetIndex = e.key === 'Backspace' ? index - 1 : index;

        if (targetIndex < 0) return;

        pendingCaretIndex = targetIndex;

        console.log('Slanje delete-a serveru:', targetIndex);
        connection.invoke('Delete', docId, targetIndex)
            .catch(err => console.error('Greska pri slanju delete:', err));
    });

    return {
        getMetadata: () => ({ name: "CRDT Sync Plugin" })
    };
});
