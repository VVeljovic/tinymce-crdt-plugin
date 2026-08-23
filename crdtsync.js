tinymce.PluginManager.add('crdtsync', function(editor) {
editor.options.register('crdtsync_hub_url', {processor: 'string', default : 'default'})
editor.options.register('crdtsync_doc_id', {processor: 'string', default: 'default'})

const hubUrl = editor.options.get('crdtsync_hub_url');
const docId = editor.options.get('crdtsync_doc_id');

let nodeId = null;
let connection = null;
let isApplyingRemoteChange = false;

editor.on('init', () => {
    connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();


    connection.on('ContentChanged', (text) => {
        isApplyingRemoteChange = true;
        editor.setContent(text);
        isApplyingRemoteChange = false;
        console.log('[crdtsync] content changed, new content = ', text)
    })

    connection.start()
        .then(() => connection.invoke('JoinDocument', docId))
        .then((assignedNodeId) => {
            nodeId = assignedNodeId;
            console.log('[crdtsync] was connected, nodeId = ', nodeId)
        })
        .catch(err => console.error('[crdtsync] error during connection', err))

})

editor.on('input', () => {
    const newText = editor.getContent({format : 'text'});
    console.log('[crdtsync] input event, new content = ', newText);
})


return {
    getMetadata: () => ({name: 'CRDT Sync'})
}
});