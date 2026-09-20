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

function createPeritext({ editor, sendOrQueue, getLocalElements, getMyNodeId, nextCounter }) {
  const ANCHOR_BEFORE = 0;
  const ANCHOR_AFTER = 1;

  const TAG_FOR_ATTRIBUTE = {
    bold: "strong",
    italic: "em",
    underline: "u",
    strikethrough: "s",
    subscript: "sub",
    superscript: "sup",
    code: "code",
  };
  const STYLE_PROPERTY_FOR_ATTRIBUTE = {
    color: "color",
    backgroundColor: "background-color",
    fontFamily: "font-family",
    fontSize: "font-size",
  };
  const ATTRIBUTE_ORDER = [
    "bold",
    "italic",
    "underline",
    "strikethrough",
    "subscript",
    "superscript",
    "code",
    "color",
    "backgroundColor",
    "fontFamily",
    "fontSize",
  ];
  const MARK_KIND = {
    bold: "boolean",
    italic: "boolean",
    underline: "boolean",
    strikethrough: "boolean",
    subscript: "boolean",
    superscript: "boolean",
    code: "boolean",
    color: "value",
    backgroundColor: "value",
    fontFamily: "value",
    fontSize: "value",
  };
  const FORMAT_COMMANDS = {
    bold: "bold",
    italic: "italic",
    underline: "underline",
    strikethrough: "strikethrough",
    subscript: "subscript",
    superscript: "superscript",
    code: "code",
  };
  const VALUE_COMMANDS = {
    forecolor: "color",
    hilitecolor: "backgroundColor",
    backcolor: "backgroundColor",
    fontname: "fontFamily",
    fontsize: "fontSize",
  };

  let localFormattings = [];
  let pendingValueAttributes = {};

  function escapeHtml(str) {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function resolveAnchorToVisibleIndex(anchor) {
    const localElements = getLocalElements();

    if (!anchor || anchor.id === null || anchor.id === undefined) {
      return anchor && anchor.type === ANCHOR_AFTER ? Infinity : 0;
    }

    const idx = localElements.findIndex((e) => idsEqual(e.crdtId, anchor.id));
    if (idx === -1) {
      return null;
    }

    let visibleBefore = 0;
    for (let i = 0; i < idx; i++) {
      if (!localElements[i].isDeleted) visibleBefore++;
    }

    const el = localElements[idx];
    if (anchor.type === ANCHOR_BEFORE) return visibleBefore;
    return visibleBefore + (el.isDeleted ? 0 : 1);
  }

  function resolveFormattingRanges() {
    const ranges = [];
    for (const f of localFormattings) {
      const start = resolveAnchorToVisibleIndex(f.start);
      const end = resolveAnchorToVisibleIndex(f.end);
      if (start === null || end === null || start >= end) continue;

      ranges.push({
        start,
        end,
        attributes: f.attributes,
        counter: f.formattingId ? f.formattingId.counter : -1,
      });
    }
    return ranges;
  }

  function isMarkOn(key, value) {
    const kind = MARK_KIND[key] || "boolean";
    return kind === "boolean" ? value === "true" : !!value && value !== "false";
  }

  function activeAttributesAt(ranges, i) {
    const winners = {};

    for (const r of ranges) {
      if (i < r.start || i >= r.end) continue;
      for (const key of Object.keys(r.attributes)) {
        const current = winners[key];
        if (!current || r.counter > current.counter) {
          winners[key] = { counter: r.counter, value: r.attributes[key] };
        }
      }
    }

    const active = {};
    for (const key of ATTRIBUTE_ORDER) {
      const winner = winners[key];
      if (winner && isMarkOn(key, winner.value)) {
        active[key] = winner.value;
      }
    }
    return active;
  }

  function isAttributeActiveOverRange(ranges, attributeKey, start, end) {
    if (start >= end) return false;
    for (let i = start; i < end; i++) {
      if (!(attributeKey in activeAttributesAt(ranges, i))) return false;
    }
    return true;
  }

  function serializeActive(active) {
    return ATTRIBUTE_ORDER.filter((key) => key in active)
      .map((key) => `${key}:${active[key]}`)
      .join("|");
  }

  function buildTagsFor(active) {
    const openTags = [];
    const closeTags = [];

    const styleParts = [];
    for (const key of ATTRIBUTE_ORDER) {
      if (MARK_KIND[key] === "value" && key in active) {
        const prop = STYLE_PROPERTY_FOR_ATTRIBUTE[key];
        if (prop) styleParts.push(`${prop}: ${active[key]}`);
      }
    }
    if (styleParts.length) {
      openTags.push(`<span style="${styleParts.join("; ")}">`);
      closeTags.unshift("</span>");
    }

    for (const key of ATTRIBUTE_ORDER) {
      if (MARK_KIND[key] !== "value" && key in active) {
        openTags.push(`<${TAG_FOR_ATTRIBUTE[key]}>`);
        closeTags.unshift(`</${TAG_FOR_ATTRIBUTE[key]}>`);
      }
    }

    return { openHtml: openTags.join(""), closeHtml: closeTags.join("") };
  }

  function getFlatOffsets() {
    const rng = editor.selection.getRng();
    const bodyRng = editor.dom.createRng();
    bodyRng.selectNodeContents(editor.getBody());

    const startRng = bodyRng.cloneRange();
    startRng.setEnd(rng.startContainer, rng.startOffset);

    const endRng = bodyRng.cloneRange();
    endRng.setEnd(rng.endContainer, rng.endOffset);

    return { start: startRng.toString().length, end: endRng.toString().length };
  }

  function buildAnchor(visibleIndex, isStart) {
    const localElements = getLocalElements();
    const el = isStart
      ? findVisibleElementAt(localElements, visibleIndex)
      : findVisibleElementAt(localElements, visibleIndex - 1);

    return {
      id: el ? el.crdtId : null,
      type: isStart ? ANCHOR_BEFORE : ANCHOR_AFTER,
    };
  }

  function setFormattings(formattings) {
    localFormattings = formattings;
  }

  function renderHtml(visibleElements) {
    const ranges = resolveFormattingRanges();

    let html = "";
    let paragraphOpen = false;
    let openActive = {};
    let openCloseHtml = "";

    function closeOpenAttrs() {
      html += openCloseHtml;
      openActive = {};
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
        openActive = {};
        openCloseHtml = "";
      }

      const active = activeAttributesAt(ranges, i);
      if (serializeActive(active) !== serializeActive(openActive)) {
        closeOpenAttrs();
        const { openHtml, closeHtml } = buildTagsFor(active);
        html += openHtml;
        openActive = active;
        openCloseHtml = closeHtml;
      }

      html += escapeHtml(el.value);
    });

    if (paragraphOpen) {
      closeOpenAttrs();
      html += "</p>";
    }

    return html.length ? html : "<p><br></p>";
  }

  function attributesForNewChar(newVisibleIndex) {
    const inheritedAttributes =
      newVisibleIndex > 0
        ? activeAttributesAt(resolveFormattingRanges(), newVisibleIndex - 1)
        : {};

    const combinedAttributes = { ...inheritedAttributes, ...pendingValueAttributes };

    for (const key of Object.keys(FORMAT_COMMANDS)) {
      if (editor.formatter.match(key)) {
        combinedAttributes[key] = "true";
      } else {
        delete combinedAttributes[key];
      }
    }

    return combinedAttributes;
  }

  function buildFormattingFor(crdtId, attributes) {
    const keys = Object.keys(attributes);
    if (keys.length === 0) return null;

    return {
      formattingId: { nodeId: getMyNodeId(), counter: nextCounter() },
      start: { id: crdtId, type: ANCHOR_BEFORE },
      end: { id: crdtId, type: ANCHOR_AFTER },
      attributes: Object.fromEntries(keys.map((key) => [key, attributes[key]])),
    };
  }

  function recordAndSend(formatting) {
    localFormattings.push(formatting);
    sendOrQueue({
      type: "Formatting",
      data: formatting,
    });
  }

  function handleBeforeExecCommand(e) {
    const commandLower = e.command.toLowerCase();
    let attributeKey;
    let rawValue;

    if (commandLower === "mcetoggleformat") {
      attributeKey = FORMAT_COMMANDS[String(e.value).toLowerCase()];
    } else if (commandLower === "mceapplytextcolor") {
      const format =
        typeof e.ui === "string" ? e.ui : e.value && e.value.format;
      rawValue =
        typeof e.value === "string" ? e.value : e.value && e.value.value;
      attributeKey =
        VALUE_COMMANDS[String(format || "forecolor").toLowerCase()];
    } else if (VALUE_COMMANDS[commandLower]) {
      attributeKey = VALUE_COMMANDS[commandLower];
      rawValue = e.value;
    } else {
      attributeKey = FORMAT_COMMANDS[commandLower];
    }

    if (!attributeKey) {
      return;
    }

    let { start, end } = getFlatOffsets();

    if (start == end) {
      if (MARK_KIND[attributeKey] === "value" && rawValue) {
        pendingValueAttributes[attributeKey] = rawValue;
      }
      return;
    }

    let value;
    if (MARK_KIND[attributeKey] === "value") {
      value = rawValue;
      if (!value) return;
    } else {
      const ranges = resolveFormattingRanges();
      const isActive = isAttributeActiveOverRange(ranges, attributeKey, start, end);
      value = isActive ? "false" : "true";
    }

    const formatting = {
      formattingId: { nodeId: getMyNodeId(), counter: nextCounter() },
      start: buildAnchor(start, true),
      end: buildAnchor(end, false),
      attributes: { [attributeKey]: value },
    };

    recordAndSend(formatting);
  }

  return {
    setFormattings,
    renderHtml,
    attributesForNewChar,
    buildFormattingFor,
    recordAndSend,
    handleBeforeExecCommand,
  };
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

  const peritext = createPeritext({
    editor,
    sendOrQueue,
    getLocalElements: () => localElements,
    getMyNodeId: () => myNodeId,
    nextCounter: () => myCounter++,
  });

  function renderHtml() {
    return peritext.renderHtml(getVisibleElements());
  }

  editor.on("init", () => {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
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

    connection.on("ElementsChanged", (elements) => {
      myCounter = buildLamportCounter(elements.map((e) => e.crdtId));
      localElements = elements;
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    connection.on("FormattingsChanged", (formattings) => {
      myCounter = buildLamportCounter(formattings.map((f) => f.formattingId));
      peritext.setFormattings(formattings);
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    connection
      .start()
      .then(() => connection.invoke("JoinDocument", docId))
      .catch((err) => console.error("[crdtsync] error during connection", err));
  });

  editor.on("BeforeExecCommand", (e) => peritext.handleBeforeExecCommand(e));

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

    const newText = editor.getContent({ format: "text" });
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

      sendOrQueue({
        type: "Insert",
        data: newElement,
      });

      const attributes = peritext.attributesForNewChar(start + i);
      const formatting = peritext.buildFormattingFor(newElement.crdtId, attributes);
      if (formatting) {
        peritext.recordAndSend(formatting);
      }
    }
  });

  return {
    getMetadata: () => ({ name: "CRDT Sync" }),
  };
});
