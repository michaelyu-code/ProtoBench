/* All assets are bundled locally. Source text is always data, never HTML. */
(() => {
  "use strict";
  const host = window.chrome?.webview;
  const post = (type, data = {}) => host?.postMessage({ type, ...data });
  window.addEventListener("error", event => post("error", { message: event.message }));
  const cm = CodeMirror(document.getElementById("editor"), {
    value: "", lineNumbers: true, mode: null, viewportMargin: 12,
    lineWrapping: false, undoDepth: 32, spellcheck: false, autocorrect: false,
    autocapitalize: false, inputStyle: "textarea", dragDrop: false,
    extraKeys: { "Ctrl-F": () => post("find"), F7: () => post("navigate", {direction:1}),
      "Shift-F7": () => post("navigate", {direction:-1}),
      "Ctrl-Alt-Left": () => post("copy", {toRight:false}), "Ctrl-Alt-Right": () => post("copy", {toRight:true}) }
  });
  let applying = false, version = 0, revision = 0, patches = [], weak = [], strong = [], removed = false;
  let markers = [], gutterLines = [], selected = new Set(), widgets = [], widgetHeights = new Map();
  let painted = "", highlightRevision = 0, painting = false, viewportPending = false;
  let suppressScroll = false, userScroll = false, layoutPending = false, measuredHeights = [];
  let spacingKey = "[]";
  const naturalHeight = line => Math.max(1, cm.getLineHandle(line).height - (widgetHeights.get(line) || 0));
  const notifyLayout = () => {
    if (layoutPending) return;
    layoutPending = true;
    setTimeout(() => { layoutPending = false; post("layout"); }, 80);
  };
  function queueViewport(user = false) {
    userScroll ||= user;
    if (viewportPending) return;
    viewportPending = true;
    requestAnimationFrame(() => {
      viewportPending = false; paint();
      const info = cm.getScrollInfo(), view = cm.getViewport(), lines = [];
      for (let i = view.from; i < view.to; i++) {
        lines.push([i + 1, cm.heightAtLine(i, "local") - info.top]);
        const height = naturalHeight(i);
        if (measuredHeights[i] != null && Math.abs(measuredHeights[i] - height) > .5) notifyLayout();
      }
      post("viewport", { top: info.top, left: info.left, lineHeight: cm.defaultTextHeight(), lines, user: userScroll,
        lineCount: cm.lineCount(), renderedLines: document.querySelectorAll(".CodeMirror-code > div").length });
      userScroll = false;
    });
  }
  function selection() {
    const from = cm.getCursor("from"), to = cm.getCursor("to");
    post("selection", { start: cm.indexFromPos(from), end: cm.indexFromPos(to), line: cm.getCursor().line + 1 });
  }
  function rangesInView(ranges, start, end) {
    let low = 0, high = ranges.length;
    while (low < high) { const mid = (low + high) >> 1; if (ranges[mid][1] <= start) low = mid + 1; else high = mid; }
    const result = [];
    for (let i = low; i < ranges.length && ranges[i][0] < end; i++) result.push([Math.max(start, ranges[i][0]), Math.min(end, ranges[i][1])]);
    return result;
  }
  function paint() {
    if (painting) return;
    const view = cm.getViewport(), key = `${view.from}:${view.to}:${highlightRevision}`;
    if (painted === key) return;
    painted = key; painting = true;
    try {
      cm.operation(() => {
        markers.forEach(m => m.clear()); markers = [];
        gutterLines.forEach(line => cm.removeLineClass(line, "gutter", "selected-change")); gutterLines = [];
        const start = cm.indexFromPos({ line: view.from, ch: 0 });
        const end = view.to >= cm.lineCount() ? cm.indexFromPos({ line: cm.lastLine(), ch: cm.getLine(cm.lastLine()).length }) : cm.indexFromPos({ line: view.to, ch: 0 });
        const add = (ranges, css) => rangesInView(ranges, start, end).forEach(([a,b]) => {
          if (b > a) markers.push(cm.markText(cm.posFromIndex(a), cm.posFromIndex(b), { className: css }));
        });
        add(weak, removed ? "diff-removed" : "diff-added");
        add(strong, removed ? "diff-removed-strong" : "diff-added-strong");
        const selectionStart = Math.max(start, cm.indexFromPos(cm.getCursor("from")));
        const selectionEnd = Math.min(end, cm.indexFromPos(cm.getCursor("to")));
        if (selectionEnd > selectionStart) markers.push(cm.markText(cm.posFromIndex(selectionStart), cm.posFromIndex(selectionEnd), { className: "source-selection" }));
        for (let line = view.from; line < view.to; line++) if (selected.has(line + 1)) {
          cm.addLineClass(line, "gutter", "selected-change"); gutterLines.push(line);
        }
      });
    } finally { painting = false; }
  }
  cm.on("beforeChange", (_, change) => {
    if (!applying) patches.push({ from: cm.indexFromPos(change.from), to: cm.indexFromPos(change.to), text: change.text.join("\n") });
  });
  cm.on("changes", () => {
    if (applying) return;
    if (patches.length) post("change", { version, revision: ++revision, patches: patches.splice(0) });
    weak = []; strong = []; highlightRevision++; measuredHeights = [];
    selection(); queueViewport(); notifyLayout();
  });
  cm.on("cursorActivity", () => { selection(); highlightRevision++; queueViewport(); });
  cm.on("scroll", () => queueViewport(!suppressScroll && !applying));
  cm.on("viewportChange", () => queueViewport());
  cm.on("update", () => queueViewport());
  const whitespace = { token(stream) {
    if (stream.eatWhile(/[ \t]/)) return "visible-space";
    while (!stream.eol() && !/[ \t]/.test(stream.peek())) stream.next();
    return null;
  }};
  function spacing(spaces) {
    const key = JSON.stringify(spaces);
    if (key === spacingKey) return;
    spacingKey = key; suppressScroll = true;
    cm.operation(() => {
      widgets.forEach(w => w.clear()); widgets = []; widgetHeights.clear();
      for (const gap of spaces) {
        const line = Math.min(cm.lineCount() - 1, Math.max(0, gap.line - 1));
        const node = document.createElement("div"); node.style.height = `${gap.height}px`;
        // The pinned LineWidget implementation accepts its known height. Supplying it
        // avoids forcing browser layout merely to measure an offscreen spacer div.
        widgets.push(cm.addLineWidget(line, node, { above: gap.above, coverGutter: true, noHScroll: true, height: gap.height }));
        widgetHeights.set(line, (widgetHeights.get(line) || 0) + gap.height);
      }
    });
    requestAnimationFrame(() => { suppressScroll = false; queueViewport(); });
  }
  function receive(message) {
    switch (message.type) {
      case "text":
        applying = true; version = message.version; revision = 0; patches = [];
        try {
          if (cm.getValue() !== message.value) {
            cm.replaceRange(message.value, { line: 0, ch: 0 }, { line: cm.lastLine(), ch: cm.getLine(cm.lastLine()).length }, "external");
            cm.setCursor(0, 0);
          }
          weak = []; strong = []; highlightRevision++; spacing([]); measuredHeights = [];
        } finally { applying = false; }
        selection(); queueViewport(); notifyLayout(); break;
      case "highlights": weak = message.weak; strong = message.strong; removed = message.removed; highlightRevision++; queueViewport(); break;
      case "selected": selected = new Set(message.lines); highlightRevision++; queueViewport(); break;
      case "wrap": cm.setOption("lineWrapping", message.value); measuredHeights = []; notifyLayout(); break;
      case "theme": document.body.classList.toggle("light", !message.dark); cm.refresh(); notifyLayout(); break;
      case "label": cm.getInputField().setAttribute("aria-label", message.value); break;
      case "whitespace": cm.removeOverlay(whitespace); if (message.value) cm.addOverlay(whitespace); document.body.classList.toggle("show-whitespace", message.value); break;
      case "spacing": spacing(message.spaces); break;
      case "scroll": suppressScroll = true; cm.scrollTo(message.left, message.top); requestAnimationFrame(() => { suppressScroll = false; queueViewport(); }); break;
      case "reveal": cm.scrollIntoView({ line: Math.min(cm.lastLine(), Math.max(0, message.line - 1)), ch: 0 }, cm.defaultTextHeight() * 2); queueViewport(); break;
      case "replace": cm.replaceSelection(message.value, "around", "input"); cm.focus(); break;
      case "select": {
        const from = cm.posFromIndex(message.start), to = cm.posFromIndex(message.end);
        cm.setSelection(from, to, {scroll:false}); cm.scrollIntoView({from, to}, 32); selection(); queueViewport(); break;
      }
    }
  }
  host?.addEventListener("message", e => receive(e.data));
  window.editorBridge = {
    state: () => ({ version, revision, start: cm.indexFromPos(cm.getCursor("from")), end: cm.indexFromPos(cm.getCursor("to")), line: cm.getCursor().line + 1 }),
    snapshot: () => ({ text: cm.getValue(), version, revision }),
    measure: () => { measuredHeights = Array.from({ length: cm.lineCount() }, (_, i) => naturalHeight(i)); return measuredHeights; },
    diagnostics: () => ({ lineCount: cm.lineCount(), renderedLines: document.querySelectorAll(".CodeMirror-code > div").length, viewport: cm.getViewport() })
  };
  new ResizeObserver(() => { cm.refresh(); notifyLayout(); }).observe(document.getElementById("editor"));
  post("ready"); queueViewport();
})();
