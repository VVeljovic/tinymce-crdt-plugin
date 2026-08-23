tinymce.PluginManager.add("crdtsync", function (editor) {
  editor.options.register("crdtsync_hub_url", {
    processor: "string",
    default: "default",
  });
  editor.options.register("crdtsync_doc_id", {
    processor: "string",
    default: "default",
  });

  const hubUrl = editor.options.get("crdtsync_hub_url");
  const docId = editor.options.get("crdtsync_doc_id");

  let nodeId = null;
  let connection = null;
  let isApplyingRemoteChange = false;
  let lastKnownText = "";

  function diffText(oldText, newText) {
    let start = 0;
    while (
      start < oldText.length &&
      start < newText.length &&
      oldText[start] == newText[start]
    ) {
      start++;
    }

    let oldEnd = oldText.length;
    let newEnd = newText.length;

    while (
      oldEnd > start &&
      newEnd > start &&
      oldText[oldEnd - 1] == newText[newEnd - 1]
    ) {
      oldEnd--;
      newEnd--;
    }

    return {
      start,
      deleted: oldText.slice(start, oldEnd),
      inserted: newText.slice(start, newEnd),
    };
  }

  editor.on("init", () => {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.on("ContentChanged", (text) => {
      if (text === lastKnownText) {
        return;
      }

      isApplyingRemoteChange = true;
      editor.setContent(text);
      lastKnownText = text;
      isApplyingRemoteChange = false;
      console.log("[crdtsync] content changed, new content = ", text);
    });

    connection
      .start()
      .then(() => connection.invoke("JoinDocument", docId))
      .then((assignedNodeId) => {
        nodeId = assignedNodeId;
        console.log("[crdtsync] was connected, nodeId = ", nodeId);
      })
      .catch((err) => console.error("[crdtsync] error during connection", err));
  });
editor.on("keydown", (e) => {
    if (e.key === "Enter") {
        e.preventDefault();
        editor.execCommand("InsertParagraph");
    }  if (e.key === "Tab") {
        e.preventDefault();
        editor.insertContent("&nbsp;&nbsp;&nbsp;&nbsp;");
    }
});

  editor.on("input", () => {
    const newText = editor.getContent({ format: "text" });
    if (isApplyingRemoteChange) {
      return;
    }
    const { start, deleted, inserted } = diffText(lastKnownText, newText);

    for (let i = 0; i < deleted.length; i++) {
      connection.invoke("Delete", docId, start);
    }

    for (let i = 0; i < inserted.length; i++) {
      connection.invoke("Insert", docId, inserted[i], start + i);
    }

    lastKnownText = newText;
  });

  return {
    getMetadata: () => ({ name: "CRDT Sync" }),
  };
});
