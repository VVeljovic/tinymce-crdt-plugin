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
  let localFormattings = [];

  const ANCHOR_BEFORE = 0;
  const ANCHOR_AFTER = 1;

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

  function findVisibleElementAt(visibleIndex) {
    let visibleCount = -1;
    for (const el of localElements) {
      if (el.isDeleted) continue;
      visibleCount++;
      if (visibleCount === visibleIndex) return el;
    }
    return null;
  }

  function idsEqual(a, b) {
    return a && b && a.nodeId === b.nodeId && a.counter === b.counter;
  }

  function renderText() {
    return localElements
      .filter((e) => !e.isDeleted)
      .map((e) => e.value)
      .join("");
  }

  function escapeHtml(str) {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function getVisibleElements() {
    return localElements.filter((e) => !e.isDeleted);
  }

  // Resolves a formatting anchor (which points at a crdtId, not a plain index)
  // into an index in the *current* visible-elements array.
  function resolveAnchorVisibleIndex(anchor, visibleLength) {
    if (!anchor || anchor.id == null) {
      return anchor && anchor.type === ANCHOR_AFTER ? visibleLength : 0;
    }

    const idx = localElements.findIndex((e) => idsEqual(e.crdtId, anchor.id));
    if (idx === -1) {
      // anchor element unknown locally (e.g. formatting arrived before the
      // insert it depends on) - fall back to the ends of the text
      return anchor.type === ANCHOR_AFTER ? visibleLength : 0;
    }

    let visibleCount = 0;
    for (let i = 0; i < idx; i++) {
      if (!localElements[i].isDeleted) visibleCount++;
    }

    if (!localElements[idx].isDeleted) {
      return anchor.type === ANCHOR_BEFORE ? visibleCount : visibleCount + 1;
    }

    // the anchored character was deleted - both Before/After collapse to
    // the position it used to occupy
    return visibleCount;
  }

  // For every visible character, computes the merged set of attributes
  // (bold/italic/underline/...) coming from all formattings covering it.
  function computeCharAttributes(visible) {
    const marks = visible.map(() => ({}));

    for (const f of localFormattings) {
      const startIdx = resolveAnchorVisibleIndex(f.start, visible.length);
      const endIdx = resolveAnchorVisibleIndex(f.end, visible.length);
      for (let i = startIdx; i < endIdx && i < marks.length; i++) {
        Object.assign(marks[i], f.attributes);
      }
    }

    return marks;
  }

  function sameAttrs(a, b) {
    const aKeys = Object.keys(a);
    const bKeys = Object.keys(b);
    if (aKeys.length !== bKeys.length) return false;
    return aKeys.every((k) => a[k] === b[k]);
  }

  function attrsToTags(attrs) {
    const tags = [];
    if (attrs.bold === "true") tags.push("strong");
    if (attrs.italic === "true") tags.push("em");
    if (attrs.underline === "true") tags.push("u");
    return tags;
  }

  function renderHtml() {
    const visible = getVisibleElements();
    const marks = computeCharAttributes(visible);

    let html = "";
    let paragraphOpen = false;
    let i = 0;

    while (i < visible.length) {
      if (visible[i].value === "\n") {
        html += paragraphOpen ? "</p>" : "<p><br></p>";
        paragraphOpen = false;
        i++;
        continue;
      }

      if (!paragraphOpen) {
        html += "<p>";
        paragraphOpen = true;
      }

      const currentAttrs = marks[i];
      let j = i;
      let runText = "";
      while (
        j < visible.length &&
        visible[j].value !== "\n" &&
        sameAttrs(marks[j], currentAttrs)
      ) {
        runText += visible[j].value;
        j++;
      }

      let chunk = escapeHtml(runText);
      for (const tag of attrsToTags(currentAttrs)) {
        chunk = `<${tag}>${chunk}</${tag}>`;
      }
      html += chunk;

      i = j;
    }

    if (paragraphOpen) html += "</p>";

    return html.length ? html : "<p><br></p>";
  }

  function buildLamportCounter(ids) {
    let maxIncoming = -1;

    for (const id of ids){
      if(id && typeof id.counter === "number" && id.counter> maxIncoming){
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

  editor.on("init", () => {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.on("ElementsChanged", (elements) => {
      myCounter = buildLamportCounter(elements.map((e) => e.crdtId));
      localElements = elements;
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    connection.on("FormattingsChanged", (formattings) => {
      myCounter = buildLamportCounter(formattings.map((f) => f.formattingId));
      console.log("formattings changed", formattings);
      localFormattings = formattings;
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    connection
      .start()
      .then(() => connection.invoke("JoinDocument", docId))
      .then(() => {
        console.log("[crdtsync] was connected, myNodeId = ", myNodeId);
      })
      .catch((err) => console.error("[crdtsync] error during connection", err));
  });

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
    const el = isStart
      ? findVisibleElementAt(visibleIndex)
      : findVisibleElementAt(visibleIndex - 1);

    return {
      id: el ? el.crdtId : null,
      type: isStart ? ANCHOR_BEFORE : ANCHOR_AFTER,
    };
  }
  const FORMAT_COMMANDS = {
    Bold: "bold",
    Italic: "italic",
    Underline: "underline",
  };
  editor.on("BeforeExecCommand", (e) => {
    let { start, end } = getFlatOffsets();

    const attributeKey = FORMAT_COMMANDS[e.command];
    if (!attributeKey) return;

    console.log(attributeKey);
    if (start == end) return;

    const formatting = {
      formattingId: { nodeId: myNodeId, counter: myCounter++ },
      start: buildAnchor(start, true),
      end: buildAnchor(end, false),
      attributes: { [attributeKey]: "true" },
    };

    localFormattings.push(formatting);
    connection.invoke("ApplyFormatting", formatting, docId);
    console.log("[crdtsync] sent formatting", formatting);
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

    const newText = editor.getContent({ format: "text" });
    const oldText = renderText();

    const { start, deleted, inserted } = diffText(oldText, newText);

    for (let i = 0; i < deleted.length; i++) {
      const el = findVisibleElementAt(start);
      if (!el) continue;
      el.isDeleted = true;
      connection.invoke("Delete", el.crdtId, docId);
    }

    for (let i = 0; i < inserted.length; i++) {
      const predecessorId = findPredecessorId(start + i);
      const successorElement = findVisibleElementAt(start + i);
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

      connection.invoke("Insert", newElement, docId);
    }
  });

  return {
    getMetadata: () => ({ name: "CRDT Sync" }),
  };
});
