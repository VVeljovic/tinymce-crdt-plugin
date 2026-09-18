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
  let pendingValueAttributes = {};

  const OFFLINE_QUEUE_KEY = `crdtsync_offline_queue_${docId}`;

  const ANCHOR_BEFORE = 0;
  const ANCHOR_AFTER = 1;

  // "boolean" marks are simple on/off (bold, italic...) and render as a fixed
  // tag from TAG_FOR_ATTRIBUTE. "value" marks (color, backgroundColor...) carry
  // an actual value (e.g. a hex color) and render as a style on a wrapping span.
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
  // Maps a toggle-style TinyMCE format name (as seen via the generic
  // "mceToggleFormat" command) to our attribute key.
  const FORMAT_COMMANDS = {
    bold: "bold",
    italic: "italic",
    underline: "underline",
    strikethrough: "strikethrough",
    subscript: "subscript",
    superscript: "superscript",
    code: "code",
  };
  // Maps a color-style TinyMCE format name to our attribute key.
  const VALUE_COMMANDS = {
    forecolor: "color",
    hilitecolor: "backgroundColor",
    backcolor: "backgroundColor",
    fontname: "fontFamily",
    fontsize: "fontSize",
  };
  async function flushOfflineQueue() {
    const queue = getOfflineQueue();
    if (queue === null ||  queue.length === 0) return;
    console.log("[crdtsync] offline queue:", queue);
    console.log("[crdtsync] offline queue JSON:", JSON.stringify(queue));
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

  const HUB_METHOD_FOR_TYPE = {
    Insert: "Insert",
    Delete: "Delete",
    Formatting: "ApplyFormatting",
  };

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

  // Resolves a CRDT anchor (element id + Before/After) back into a position
  // expressed as "number of visible characters before this point" - the same
  // unit that buildAnchor()/getFlatOffsets() used when the anchor was created.
  // This has to work even when the anchored element has since been deleted:
  // in that case the position collapses to wherever that tombstone sits among
  // the still-visible characters.
  function resolveAnchorToVisibleIndex(anchor) {
    if (!anchor || anchor.id === null || anchor.id === undefined) {
      return anchor && anchor.type === ANCHOR_AFTER ? Infinity : 0;
    }

    const idx = localElements.findIndex((e) => idsEqual(e.crdtId, anchor.id));
    if (idx === -1) {
      // The element this anchor pointed to isn't known at all - drop the formatting
      // rather than guess at a position.
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

  // Turns localFormattings into resolved [start, end) ranges over the visible
  // text, carrying the Lamport counter of each op so overlapping/conflicting
  // formattings on the same attribute can be resolved last-write-wins.
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

  // Whether a winning value counts as "on" for a given attribute key - boolean
  // marks are on when the value is the string "true"; value marks are on
  // whenever they carry any value other than "false"/empty.
  function isMarkOn(key, value) {
    const kind = MARK_KIND[key] || "boolean";
    return kind === "boolean" ? value === "true" : !!value && value !== "false";
  }

  // Returns a { key: value } object of attributes that are "on" at visible
  // index i, resolving conflicts between overlapping formattings on the same
  // attribute by highest Lamport counter wins.
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

  // Builds the open/close HTML for a given active-attributes state: value
  // marks (color, backgroundColor) are combined into one wrapping <span
  // style="...">, boolean marks each get their own nested tag from
  // TAG_FOR_ATTRIBUTE.
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

  function renderHtml() {
    const visible = getVisibleElements();
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

    visible.forEach((el, i) => {
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

  editor.on("init", () => {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting((error) => {
      console.log("[crdtsync] connection lost, reconnecting...", error);

      editor.notificationManager.open({
        text: "The server is currently unavailable. You can continue editing in offline mode. Your changes will be synchronized when the connection is restored.",
        type: "warning",
        timeout: 0,
      });
    });

    connection.onreconnected(async () => {
      console.log("REKONEKCIJA SE DESILA");

      editor.notificationManager.open({
        text: "Connection to the server has been restored.",
        type: "success",
        timeout: 3000,
      });
      await connection.invoke("JoinDocument", docId);

      await flushOfflineQueue();

    });

    connection.onclose((error) => {
      console.log("[crdtsync] connection closed", error);

      editor.notificationManager.open({
        text: "The server is currently unavailable. You can continue editing in offline mode. Your changes will be synchronized when the connection is restored.",
        type: "warning",
        timeout: 0,
      });
    });

    connection.on("ElementsChanged", (elements) => {
      console.log('primenio elementschanged', elements);
      myCounter = buildLamportCounter(elements.map((e) => e.crdtId));
      localElements = elements;
      isApplyingRemoteChange = true;
      editor.setContent(renderHtml());
      isApplyingRemoteChange = false;
    });

    connection.on("FormattingsChanged", (formattings) => {
      myCounter = buildLamportCounter(formattings.map((f) => f.formattingId));
      console.log("[crdtsync] formattings changed", formattings);
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

  editor.on("BeforeExecCommand", (e) => {
    const commandLower = e.command.toLowerCase();
    let attributeKey;
    let rawValue;

    if (commandLower === "mcetoggleformat") {
      // Modern toolbar buttons for on/off marks (strikethrough, subscript,
      // superscript, code, and also bold/italic/underline in some UI paths)
      // route through this generic command, with the actual format name
      // passed as e.value.
      attributeKey = FORMAT_COMMANDS[String(e.value).toLowerCase()];
    } else if (commandLower === "mceapplytextcolor") {
      // The built-in color-picker toolbar buttons (forecolor/backcolor) are
      // believed to route through this command, but the exact shape of
      // which arg carries the format ("forecolor"/"hilitecolor") vs the
      // color value isn't confirmed yet for this TinyMCE build - try the
      // known possibilities and fall back to "forecolor" if unclear. If
      // color doesn't apply, check the "[crdtsync] unhandled command" log
      // below for the real e.command/e.value/e.ui shape.
      const format =
        typeof e.ui === "string" ? e.ui : e.value && e.value.format;
      rawValue =
        typeof e.value === "string" ? e.value : e.value && e.value.value;
      attributeKey =
        VALUE_COMMANDS[String(format || "forecolor").toLowerCase()];
    } else if (VALUE_COMMANDS[commandLower]) {
      // Legacy-named color commands (ForeColor/HiliteColor/BackColor), if
      // TinyMCE fires those directly instead of mceApplyTextcolor.
      attributeKey = VALUE_COMMANDS[commandLower];
      rawValue = e.value;
    } else {
      attributeKey = FORMAT_COMMANDS[commandLower];
    }

    if (!attributeKey) {
      console.log("[crdtsync] unhandled command", e.command, e.value, e.ui);
      return;
    }

    let { start, end } = getFlatOffsets();

    if (start == end) {
      // Collapsed cursor - nothing to wrap yet. For value marks (font,
      // size, color) there's no native "match" API to query later like
      // formatter.match() for booleans, so remember the chosen value
      // ourselves and apply it to whatever gets typed next.
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
      // If the whole selection is already formatted with this attribute, this
      // toggle should remove it instead of re-applying it - otherwise clicking
      // e.g. Bold on bold text could never turn bold back off.
      const ranges = resolveFormattingRanges();
      const isActive = isAttributeActiveOverRange(
        ranges,
        attributeKey,
        start,
        end,
      );
      value = isActive ? "false" : "true";
    }

    const formatting = {
      formattingId: { nodeId: myNodeId, counter: myCounter++ },
      start: buildAnchor(start, true),
      end: buildAnchor(end, false),
      attributes: { [attributeKey]: value },
    };

    localFormattings.push(formatting);
    sendOrQueue({
      type: "Formatting",
      data: formatting,
    });
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
      sendOrQueue({
        type: "Delete",
        data: el.crdtId,
      });
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

      sendOrQueue({
        type: "Insert",
        data: newElement,
      });

      // Attributes inherited from the CRDT model (the character immediately to
      // the left) cover "typing inside already-formatted text". Attributes
      // TinyMCE itself currently has toggled (editor.formatter.match) also
      // cover "clicked Bold with the cursor collapsed, then started typing" -
      // native contentEditable/TinyMCE track that pending state, our CRDT
      // model doesn't unless we ask TinyMCE directly. TinyMCE's live state
      // wins for boolean marks since it reflects an explicit toggle either way.
      const inheritedAttributes =
        start + i > 0
          ? activeAttributesAt(resolveFormattingRanges(), start + i - 1)
          : {};
      const combinedAttributes = { ...inheritedAttributes, ...pendingValueAttributes };
      for (const key of Object.keys(FORMAT_COMMANDS)) {
        if (editor.formatter.match(key)) {
          combinedAttributes[key] = "true";
        } else {
          delete combinedAttributes[key];
        }
      }
      const inheritedKeys = Object.keys(combinedAttributes);
      if (inheritedKeys.length > 0) {
        const formatting = {
          formattingId: { nodeId: myNodeId, counter: myCounter++ },
          start: { id: newElement.crdtId, type: ANCHOR_BEFORE },
          end: { id: newElement.crdtId, type: ANCHOR_AFTER },
          attributes: Object.fromEntries(
            inheritedKeys.map((key) => [key, combinedAttributes[key]]),
          ),
        };

        localFormattings.push(formatting);
        sendOrQueue({
          type: "Formatting",
          data: formatting,
        });
      }
    }
  });

  return {
    getMetadata: () => ({ name: "CRDT Sync" }),
  };
});
