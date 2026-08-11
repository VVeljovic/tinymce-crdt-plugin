tinymce.PluginManager.add('crdtsync', function (editor, url) {
    const hubUrl = editor.getParam('crdtsync_hub_url', 'http://localhost:5042/editorHub');
    const docId = editor.getParam('crdtsync_doc_id', 'default-doc');

    // --- SignalR setup ---
    // Server-authoritative varijanta: klijent ne drzi nikakvo CRDT stanje (nema
    // elements niza, nema merge algoritma). Samo javlja "korisnik je otkucao X na
    // pozidiji N" / "obrisao pozidiju N", i renderuje ono sto server posalje nazad.
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    function getCaretIndex() {
        const rng = editor.selection.getRng();
        const preRange = editor.dom.createRng();
        preRange.setStart(editor.getBody(), 0);
        preRange.setEnd(rng.startContainer, rng.startOffset);
        return preRange.toString().length;
    }

    // editor.setContent() u potpunosti rekonstruise DOM, pa selection uvek pada na
    // pocetak - zato pamtimo gde kursor TREBA da bude (po broju karaktera od pocetka)
    // i vracamo ga rucno posle svakog ContentChanged.
    let pendingCaretIndex = 0;

    function setCaretIndex(targetIndex) {
        const body = editor.getBody();
        const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT);

        let remaining = targetIndex;
        let node = null;
        let offset = 0;
        let current;

        while ((current = walker.nextNode())) {
            const len = current.nodeValue.length;
            if (remaining <= len) {
                node = current;
                offset = remaining;
                break;
            }
            remaining -= len;
        }

        if (node) {
            editor.selection.setCursorLocation(node, offset);
        } else {
            // Prazan sadrzaj ili targetIndex van opsega - kursor na kraj.
            editor.selection.select(body, true);
            editor.selection.collapse(false);
        }
    }

    // --- SignalR events ---
    connection.on("ContentChanged", (text) => {
        console.log('Sadrzaj sa servera:', text);
        editor.setContent(text);
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
        if (e.key !== 'Backspace' && e.key !== 'Delete') return;

        const index = getCaretIndex();
        // Backspace brise karakter PRE kursora, Delete brise karakter POSLE kursora
        const targetIndex = e.key === 'Backspace' ? index - 1 : index;

        if (targetIndex < 0) return;

        // I u Backspace i u Delete slucaju, kursor posle brisanja ostaje na targetIndex
        // (mesto gde je obrisani karakter bio).
        pendingCaretIndex = targetIndex;

        console.log('Slanje delete-a serveru:', targetIndex);
        connection.invoke('Delete', docId, targetIndex)
            .catch(err => console.error('Greska pri slanju delete:', err));
    });

    return {
        getMetadata: () => ({ name: "CRDT Sync Plugin" })
    };
});
