tinymce.PluginManager.add('crdtsync', function (editor, url) {
    const hubUrl = editor.getParam('crdtsync_hub_url', 'http://localhost:5042/editorHub');
    const docId = editor.getParam('crdtsync_doc_id', 'default-doc');

    // --- SignalR setup ---
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    // --- CRDT client-side state (ogledalo CrdtDocument iz CrdtCore) ---
    let nodeId = null;
    let counter = 0;
    let elements = []; // { crdtId: {nodeId, counter}, value, predecessorId, isDeleted }

    function crdtIdEquals(a, b) {
        if (!a || !b) return a === b;
        return a.nodeId === b.nodeId && a.counter === b.counter;
    }

    function hasPriority(existing, incoming) {
        if (existing.crdtId.nodeId !== incoming.crdtId.nodeId) {
            return existing.crdtId.nodeId > incoming.crdtId.nodeId;
        }
        return existing.crdtId.counter > incoming.crdtId.counter;
    }

    function findElementIndexById(crdtId) {
        if (!crdtId) return -1;
        return elements.findIndex(el => crdtIdEquals(el.crdtId, crdtId));
    }

    function insertElementInOrder(newElement) {
        const insertAfterIndex = findElementIndexById(newElement.predecessorId);
        let candidateIndex = insertAfterIndex + 1;

        const skippedIds = [];
        if (newElement.predecessorId) skippedIds.push(newElement.predecessorId);

        while (candidateIndex < elements.length) {
            const candidate = elements[candidateIndex];
            const partOfChain = candidate.predecessorId
                && skippedIds.some(id => crdtIdEquals(id, candidate.predecessorId));

            if (!partOfChain) break;

            if (crdtIdEquals(candidate.predecessorId, newElement.predecessorId)
                && !hasPriority(candidate, newElement)) break;

            skippedIds.push(candidate.crdtId);
            candidateIndex++;
        }

        elements.splice(candidateIndex, 0, newElement);
    }

    function findPredecessorId(visibleIndex) {
        if (visibleIndex === 0) return null;

        let visibleCount = 0;
        for (const el of elements) {
            if (el.isDeleted) continue;
            visibleCount++;
            if (visibleIndex === visibleCount) return el.crdtId;
        }

        for (let i = elements.length - 1; i >= 0; i--) {
            if (!elements[i].isDeleted) return elements[i].crdtId;
        }
        return null;
    }

    function localInsert(value, visibleIndex) {
        const predecessorId = findPredecessorId(visibleIndex);
        const newElement = {
            crdtId: { nodeId: nodeId, counter: counter++ },
            value: value,
            predecessorId: predecessorId,
            isDeleted: false
        };
        insertElementInOrder(newElement);
        return newElement;
    }

    function findVisibleElementAt(visibleIndex) {
        let visibleCount = -1;
        for (const el of elements) {
            if (el.isDeleted) continue;
            visibleCount++;
            if (visibleCount === visibleIndex) return el;
        }
        return null;
    }

    function localDelete(visibleIndex) {
        const element = findVisibleElementAt(visibleIndex);
        if (!element) return null;
        element.isDeleted = true;
        return element;
    }

    function remoteInsert(element) {
        if (findElementIndexById(element.crdtId) !== -1) return;
        insertElementInOrder(element);
    }

    function remoteDelete(crdtId) {
        const element = elements.find(el => crdtIdEquals(el.crdtId, crdtId));
        if (element) element.isDeleted = true;
    }

    function getText() {
        return elements.filter(el => !el.isDeleted).map(el => el.value).join('');
    }

    function render() {
        editor.setContent(getText());
    }

    function getCaretIndex() {
        const rng = editor.selection.getRng();
        const preRange = editor.dom.createRng();
        preRange.setStart(editor.getBody(), 0);
        preRange.setEnd(rng.startContainer, rng.startOffset);
        return preRange.toString().length;
    }

    // --- SignalR events ---
    connection.on("FullSync", (serverElements) => {
        elements = serverElements;
        console.log('FullSync primljen, elemenata:', elements.length);
        render();
    });

    connection.on("ReceiveInsert", (element) => {
        remoteInsert(element);
        console.log('Primljen remote insert:', element);
        render();
    });

    connection.on("ReceiveDelete", (crdtId) => {
        remoteDelete(crdtId);
        console.log('Primljen remote delete:', crdtId);
        render();
    });

    async function start() {
        await connection.start();
        nodeId = await connection.invoke('JoinDocument', docId);
        console.log('Pridruzen dokumentu, dodeljen nodeId:', nodeId);
    }

    // --- Editor event handleri ---
    editor.on('init', () => {
        start().catch(err => console.error('Greska pri povezivanju:', err));
    });

    editor.on('keypress', (e) => {
        if (e.key.length !== 1) return; // Backspace/Enter/Shift itd. - Enter ide posebno kasnije

        const index = getCaretIndex();
        const element = localInsert(e.key, index);

        console.log('Lokalni insert:', element);
        connection.invoke('SendInsert', docId, element)
            .catch(err => console.error('Greska pri slanju:', err));
    });

    editor.on('keydown', (e) => {
        if (e.key !== 'Backspace' && e.key !== 'Delete') return;

        const index = getCaretIndex();
        // Backspace brise karakter PRE kursora, Delete brise karakter POSLE kursora
        const targetIndex = e.key === 'Backspace' ? index - 1 : index;

        if (targetIndex < 0) return;

        const deleted = localDelete(targetIndex);
        if (!deleted) return;

        console.log('Lokalni delete:', deleted.crdtId);
        connection.invoke('SendDelete', docId, deleted.crdtId)
            .catch(err => console.error('Greska pri slanju delete:', err));
    });

    return {
        getMetadata: () => ({ name: "CRDT Sync Plugin" })
    };
});
