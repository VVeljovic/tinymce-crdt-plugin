function idsEqual(a, b) {
  return a && b && a.nodeId === b.nodeId && a.counter === b.counter;
}

function findVisibleElementAt(elements, visibleIndex) {
  let visibleCount = -1;
  for (const el of elements) {
    if (el.isDeleted) continue;
    visibleCount++;
    if (visibleCount === visibleIndex) return el;
  }
  return null;
}

function idKey(id) {
  return `${id.nodeId}:${id.counter}`;
}

function escapeHtml(str) {
  return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

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

  function getOrCreateNodeId() {
    let id = localStorage.getItem("crdtsync_node_id");
    if (!id) {
      id = Math.floor(Math.random() * 1000000);
      localStorage.setItem("crdtsync_node_id", id);
    }

    return parseInt(id, 10);
  }

  let connection = null;
  let isApplyingRemoteChange = false;

  const myNodeId = getOrCreateNodeId();
  let myCounter = 0;
  let localElements = [];
  let resolvedAttributes = new Map();
  let pendingAttributes = {};

  const OFFLINE_QUEUE_KEY = `crdtsync_offline_queue_${docId}`;

  const HUB_METHOD_FOR_TYPE = {
    Insert: "Insert",
    Delete: "Delete",
    Formatting: "ApplyFormatting",
  };

  async function flushOfflineQueue() {
    const queue = getOfflineQueue();
    if (queue.length === 0) return;

    await connection.invoke("ApplyOfflineOperations", queue, docId);

    saveOfflineQueue([]);
  }

  function getOfflineQueue() {
    const stored = localStorage.getItem(OFFLINE_QUEUE_KEY);

    if (!stored) {
      return [];
    }
    return JSON.parse(stored);
  }

  function saveOfflineQueue(queue) {
    localStorage.setItem(OFFLINE_QUEUE_KEY, JSON.stringify(queue));
  }

  function queueOfflineOperation(op) {
    const queue = getOfflineQueue();
    queue.push(op);
    saveOfflineQueue(queue);
  }

  async function sendOrQueue(operation) {
    if (connection.state === signalR.HubConnectionState.Connected) {
      try {
        await connection.invoke(
          HUB_METHOD_FOR_TYPE[operation.type],
          operation.data,
          docId,
        );
      } catch (error) {
        console.error("[crdtsync] failed to send operation", error);

        queueOfflineOperation(operation);
      }

      return;
    }

    queueOfflineOperation(operation);
  }

  function findPredecessorId(visibleIndex) {
    if (visibleIndex === 0) {
      return null;
    }

    let visibleCount = 0;
    for (const el of localElements) {
      if (el.isDeleted) {
        continue;
      }
      visibleCount++;
      if (visibleCount === visibleIndex) {
        return el.crdtId;
      }
    }

    const last = [...localElements].reverse().find((e) => !e.isDeleted);
    return last ? last.crdtId : null;
  }

  function renderText() {
    return localElements
      .filter((e) => !e.isDeleted)
      .map((e) => e.value)
      .join("");
  }

  function getVisibleElements() {
    return localElements.filter((e) => !e.isDeleted);
  }

  function buildLamportCounter(ids) {
    let maxIncoming = -1;

    for (const id of ids) {
      if (id && typeof id.counter === "number" && id.counter > maxIncoming) {
        maxIncoming = id.counter;
      }
    }
    return Math.max(myCounter, maxIncoming + 1);
  }

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

  function renderHtml() {
    return renderFormattedHtml(getVisibleElements());
  }

  const ANCHOR_TYPE = {
    Before: 0,
    After: 1,
  };
  // PERITEXT

  function getVisibleOffset(container, offset) {
    const body = editor.getBody();
    let total = 0;

    for (const paragraph of body.children) {
      if (paragraph.contains(container) || paragraph === container) {
        const rng = document.createRange();
        rng.selectNodeContents(paragraph);
        rng.setEnd(container, offset);
        total += rng.toString().length;
        return total;
      }

      total += paragraph.textContent.length + 1;
    }

    return total;
  }

  function getSelectionIndexes() {
    const selection = editor.selection.getRng();

    return {
      start: getVisibleOffset(selection.startContainer, selection.startOffset),
      end: getVisibleOffset(selection.endContainer, selection.endOffset),
    };
  }

  function getAnchors(startElement, endElement) {
    let startAnchor;
    let endAnchor;

    if (startElement === null) {
      startAnchor = { id: null, type: ANCHOR_TYPE.After };
    } else {
      startAnchor = { id: startElement.crdtId, type: ANCHOR_TYPE.Before };
    }

    if (endElement === null) {
      endAnchor = { id: null, type: ANCHOR_TYPE.Before };
    } else {
      endAnchor = { id: endElement.crdtId, type: ANCHOR_TYPE.After };
    }
    return { startAnchor, endAnchor };
  }

  function getFormattingAttribute(e) {
    const commandLower = e.command.toLowerCase();

    if (commandLower === "mcetoggleformat") {
      const key = FORMAT_COMMANDS[String(e.value).toLowerCase()];
      return key
        ? { key, value: editor.formatter.match(key) ? "true" : "false" }
        : null;
    }

    if (commandLower in FORMAT_COMMANDS) {
      const key = FORMAT_COMMANDS[commandLower];

      return { key, value: editor.formatter.match(key) ? "true" : "false" };
    }

    if (commandLower === "mceapplytextcolor") {
      const key = VALUE_COMMANDS[e.ui.toLowerCase()];
      return key ? { key, value: e.value } : null;
    }

    if (commandLower in VALUE_COMMANDS) {
      return { key: VALUE_COMMANDS[commandLower], value: e.value };
    }
  }

  const FORMAT_COMMANDS = {
    bold: "bold",
    italic: "italic",
    underline: "underline",
    strikethrough: "strikethrough",
    subscript: "subscript",
    superscript: "superscript",
  };

  const VALUE_COMMANDS = {
    forecolor: "color",
    hilitecolor: "backgroundColor",
    fontname: "fontFamily",
    fontsize: "fontSize",
  };

  const EXCLUSIVE_ATTRIBUTES = {
    subscript: "superscript",
    superscript: "subscript",
  };

  const TAG_FOR_ATTRIBUTE = {
    bold: "strong",
    italic: "em",
    underline: "u",
    strikethrough: "s",
    subscript: "sub",
    superscript: "sup",
  };

  const STYLE_PROPERTY_FOR_ATTRIBUTE = {
    color: "color",
    backgroundColor: "background-color",
    fontFamily: "font-family",
    fontSize: "font-size",
  };

  //  RENDERING

  // formatting conflicts are resolved on the server (CrdtDocument.ResolveFormatting)
  function setResolvedFormatting(resolved) {
    resolvedAttributes = new Map(
      (resolved ?? []).map((r) => [idKey(r.elementId), r.attributes]),
    );
  }

  function attributesOf(element) {
    return (element && resolvedAttributes.get(idKey(element.crdtId))) || {};
  }

  function buildTagsFor(active) {
    const openTags = [];
    const closeTags = [];

    const styleParts = [];
    for (const key of Object.keys(active)) {
      if (key in STYLE_PROPERTY_FOR_ATTRIBUTE) {
        styleParts.push(`${STYLE_PROPERTY_FOR_ATTRIBUTE[key]}: ${active[key]}`);
      }
    }
    if (styleParts.length) {
      openTags.push(`<span style="${styleParts.join("; ")}">`);
      closeTags.unshift("</span>");
    }

    for (const key of Object.keys(active)) {
      if (key in TAG_FOR_ATTRIBUTE && active[key] === "true") {
        openTags.push(`<${TAG_FOR_ATTRIBUTE[key]}>`);
        closeTags.unshift(`</${TAG_FOR_ATTRIBUTE[key]}>`);
      }
    }

    return { openTags: openTags.join(""), closeTags: closeTags.join("") };
  }

  function renderFormattedHtml(visibleElements) {
    let html = "";
    let paragraphOpen = false;
    let openActiveKey = null;
    let openCloseHtml = "";

    function closeOpenAttrs() {
      html += openCloseHtml;
      openActiveKey = null;
      openCloseHtml = "";
    }

    visibleElements.forEach((el, i) => {
      if (el.value === "\n") {
        closeOpenAttrs();
        html += paragraphOpen ? "</p>" : "<p><br></p>";
        paragraphOpen = false;
        return;
      }

      if (!paragraphOpen) {
        html += "<p>";
        paragraphOpen = true;
        openActiveKey = null;
        openCloseHtml = "";
      }

      const active = attributesOf(el);
      const activeKey = JSON.stringify(active);

      if (activeKey !== openActiveKey) {
        closeOpenAttrs();
        const { openTags, closeTags } = buildTagsFor(active);
        html += openTags;
        openActiveKey = activeKey;
        openCloseHtml = closeTags;
      }

      html += escapeHtml(el.value);
    });

    if (paragraphOpen) {
      closeOpenAttrs();
      html += "</p>";
    }

    return html.length ? html : "<p><br></p>";
  }

  editor.on("init", () => {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect({

        nextRetryDelayInMilliseconds: (retryContext) =>
          Math.min(30000, (retryContext.previousRetryCount + 1) * 2000),
      })
      .build();

    connection.onreconnecting((error) => {
      editor.notificationManager.open({
        text: "The server is currently unavailable. You can continue editing in offline mode. Your changes will be synchronized when the connection is restored.",
        type: "warning",
        timeout: 0,
      });
    });

    connection.onreconnected(async () => {
      editor.notificationManager.open({
        text: "Connection to the server has been restored.",
        type: "success",
        timeout: 3000,
      });

      await connection.invoke("JoinDocument", docId);
      await flushOfflineQueue();
    });

    connection.onclose((error) => {
      editor.notificationManager.open({
        text: "The server is currently unavailable. You can continue editing in offline mode. Your changes will be synchronized when the connection is restored.",
        type: "warning",
        timeout: 0,
      });
    });

    connection.on("ElementsChanged", (elements, resolved) => {
      myCounter = buildLamportCounter(elements.map((e) => e.crdtId));
      localElements = elements;
      setResolvedFormatting(resolved);
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    connection.on("FormattingsChanged", (formattings, resolved) => {
      myCounter = buildLamportCounter(formattings.map((f) => f.formattingId));
      setResolvedFormatting(resolved);
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    // own operations are not re-rendered, only the resolved formatting is refreshed
    connection.on("FormattingResolved", (resolved) => {
      setResolvedFormatting(resolved);
    });

    connection
      .start()
      .then(() => connection.invoke("JoinDocument", docId))
      .catch((err) => console.error("[crdtsync] error during connection", err));
  });

  editor.on("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      editor.execCommand("InsertParagraph");
    }
    if (e.key === "Tab") {
      e.preventDefault();
      editor.insertContent("&nbsp;&nbsp;&nbsp;&nbsp;");
    }
  });

  editor.on("input", () => {
    if (isApplyingRemoteChange) {
      return;
    }

    const newText = editor
      .getContent({ format: "text" })
      .replace(/\n+/g, (run) => "\n".repeat(run.length / 2));
    const oldText = renderText();

    const { start, deleted, inserted } = diffText(oldText, newText);

    for (let i = 0; i < deleted.length; i++) {
      const el = findVisibleElementAt(localElements, start);
      if (!el) continue;
      el.isDeleted = true;
      sendOrQueue({
        type: "Delete",
        data: el.crdtId,
      });
    }

    const inheritedAttributes = {
      ...attributesOf(findVisibleElementAt(localElements, start - 1)),
      ...pendingAttributes,
    };

    let firstNewElement = null;
    let lastNewElement = null;

    for (let i = 0; i < inserted.length; i++) {
      const predecessorId = findPredecessorId(start + i);
      const successorElement = findVisibleElementAt(localElements, start + i);
      const successorId = successorElement ? successorElement.crdtId : null;
      const newElement = {
        crdtId: { nodeId: myNodeId, counter: myCounter++ },
        value: inserted[i],
        predecessorId,
        successorId,
        isDeleted: false,
      };

      const insertAt = predecessorId
        ? localElements.findIndex((e) => idsEqual(e.crdtId, predecessorId)) + 1
        : 0;
      localElements.splice(insertAt, 0, newElement);

      if (!firstNewElement) firstNewElement = newElement;
      lastNewElement = newElement;

      sendOrQueue({
        type: "Insert",
        data: newElement,
      });
    }

    if (Object.keys(inheritedAttributes).length > 0) {
      const formatting = {
        formattingId: { nodeId: myNodeId, counter: myCounter++ },
        start: { id: firstNewElement.crdtId, type: ANCHOR_TYPE.Before },
        end: { id: lastNewElement.crdtId, type: ANCHOR_TYPE.After },
        attributes: inheritedAttributes,
      };

      sendOrQueue({ type: "Formatting", data: formatting });
    }

    if (inserted.length > 0) {
      pendingAttributes = {};
    }
  });

  editor.on("ExecCommand", (e) => {
    const indexes = getSelectionIndexes();

    const startElement = findVisibleElementAt(localElements, indexes.start);
    const endElement = findVisibleElementAt(localElements, indexes.end - 1);

    const { startAnchor, endAnchor } = getAnchors(startElement, endElement);

    const formattingAttribute = getFormattingAttribute(e);

    if (!formattingAttribute) return;

    const attributes = { [formattingAttribute.key]: formattingAttribute.value };

    const exclusivePartner = EXCLUSIVE_ATTRIBUTES[formattingAttribute.key];
    if (exclusivePartner && formattingAttribute.value === "true") {
      attributes[exclusivePartner] = "false";
      editor.formatter.remove(exclusivePartner);
    }

    if (indexes.start === indexes.end) {
      Object.assign(pendingAttributes, attributes);
      return;
    }

    const formatting = {
      formattingId: { nodeId: myNodeId, counter: myCounter++ },
      start: startAnchor,
      end: endAnchor,
      attributes,
    };
    sendOrQueue({ type: "Formatting", data: formatting });
  });

  return {
    getMetadata: () => ({ name: "CRDT Sync" }),
  };
});
