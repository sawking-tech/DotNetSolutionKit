/* ==== THE DOCUMENTATION PAGE ===================================================================
   Renders the documents of the repository in place: the page fetches the same markdown files the
   repository holds, so a document on the site is never a second copy that drifts. What the documents
   are and where their sources live comes from site/config.js. */
const SITE_CFG = window.SITE;
const REPO = SITE_CFG.repo;
const SHELF = SITE_CFG.shelf;

// The language being read, and the file a document is carried in for it.
const langIndex = () => (document.documentElement.getAttribute("data-lang") === "ru" ? 1 : 0);
const fileOf = (entry) => entry.base + (langIndex() === 1 && entry.ru ? ".ru.md" : ".md");

const BY_ID = new Map(SHELF.flatMap((g) => g.docs.map(([id, base, title, ru]) => [id, { id, base, title, ru }])));
const DEFAULT_DOC = SITE_CFG.defaultDoc;
// Anchors that are already out in the world, from before the documents were renamed.
const ALIASES = SITE_CFG.aliases || {};

/* ==== STRINGS ====
   What the page says around a document. Both languages go into the node and the stylesheet shows
   one, so no wording is decided in JavaScript. */
const STR = {
  loading: '<span lang="en">Loading…</span><span lang="ru">Загружается…</span>',
  failed: '<span lang="en">This document did not load. Read it in the repository:</span>'
        + '<span lang="ru">Документ не загрузился. Его можно прочитать в репозитории:</span>',
  source: '<span lang="en">source on GitHub</span><span lang="ru">исходник на GitHub</span>',
};

/* ==== MENU ==== */
const menu = document.getElementById("menu");
const doc = document.getElementById("doc");

function renderMenu() {
  const i = langIndex();
  menu.innerHTML = SHELF.map((group) => `
  <h2>${group.group[i]}</h2>
  <ul>${group.docs.map(([id, , title]) => `<li><a href="#${id}" data-id="${id}">${title[i]}</a></li>`).join("")}</ul>
`).join("");
}
renderMenu();

function markCurrent(id) {
  for (const link of menu.querySelectorAll("a")) {
    if (link.dataset.id === id) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  }
}

/* ==== RENDERING ==== */
// Front matter is Hugo's, not the reader's: the title line survives, the rest is dropped.
function stripFrontMatter(text) {
  if (!text.startsWith("---")) return text;
  const end = text.indexOf("\n---", 3);
  if (end < 0) return text;
  const head = text.slice(3, end);
  const body = text.slice(end + 4).replace(/^\s*\n/, "");
  if (body.startsWith("# ")) return body;   // the body already opens with its own title
  const title = head.match(/^title:\s*"?(.+?)"?\s*$/m);
  return (title ? `# ${title[1]}\n\n` : "") + body;
}

// Links between documents stay inside the site when the target is a document the shelf holds.
const byPath = new Map([...BY_ID.values()].flatMap((d) => [[d.base + ".md", d], [d.base + ".ru.md", d]]));
function resolveLink(href, fromPath) {
  if (/^[a-z]+:|^#/.test(href)) return null;
  const base = fromPath.split("/").slice(0, -1);
  for (const part of href.split("#")[0].split("/")) {
    if (part === "..") base.pop();
    else if (part !== "." && part !== "") base.push(part);
  }
  const target = byPath.get(base.join("/"));
  const section = href.includes("#") ? href.split("#")[1] : "";
  if (!target) return REPO + base.join("/") + (section ? "#" + section : "");
  // a section of another document is addressed as #<document>:<section>; a slug has no colon
  return `#${target.id}${section ? ":" + section : ""}`;
}

/* ==== MATH ==== */
// Formulas are written as on GitHub: $...$ in a line, $$...$$ on their own. marked would read their
// underscores and backslashes as markup, so they are taken out before it and set by KaTeX after it;
// code keeps its dollars - a model's garbage output quoted in backticks is not a formula.
const CODE = /(```[\s\S]*?```|`[^`\n]*`)/;
const FORMULA = /\$\$([\s\S]+?)\$\$|\$([^\s$](?:[^$\n]*?[^\s$])?)\$/g;

function extractMath(text) {
  const formulas = [];
  const kept = text.split(CODE).map((part, i) => (i % 2 ? part : part.replace(FORMULA, (_, display, inline) => {
    formulas.push({ tex: display ?? inline, display: display !== undefined });
    return `@@MATH${formulas.length - 1}@@`;
  })));
  return { text: kept.join(""), formulas };
}

function renderMath(html, formulas) {
  return html.replace(/@@MATH(\d+)@@/g, (_, i) => {
    const { tex, display } = formulas[Number(i)];
    if (typeof katex === "undefined") return display ? `$$${tex}$$` : `$${tex}$`;  // the CDN is down: the source stays readable
    return katex.renderToString(tex, { displayMode: display, throwOnError: false });
  });
}

/* ==== DIAGRAMS ==== */
// Mermaid is drawn as GitHub draws it, so a diagram reads the same in the repository and here. The
// library is fetched only for a document that has one; if it does not come, the source stays.
const isDark = () => {
  const theme = document.documentElement.getAttribute("data-theme");
  return theme === "dark" || (theme !== "light" && matchMedia("(prefers-color-scheme: dark)").matches);
};
let mermaidLib = null;
async function renderDiagrams(root) {
  const blocks = [...root.querySelectorAll("pre > code.language-mermaid")];
  if (!blocks.length) return;
  const nodes = blocks.map((code) => {
    const pre = document.createElement("pre");
    pre.className = "mermaid";
    pre.dataset.source = code.textContent;
    pre.textContent = code.textContent;
    code.parentElement.replaceWith(pre);
    return pre;
  });
  try {
    mermaidLib ??= (await import("https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs")).default;
    mermaidLib.initialize({ startOnLoad: false, theme: isDark() ? "dark" : "default", fontFamily: "IBM Plex Sans, sans-serif" });
    await mermaidLib.run({ nodes });
  } catch { /* the CDN is down: the source stays readable */ }
}

/* ==== ANCHORS ==== */
// A heading gets the id GitHub gives it, so the same "#section" link works in the repository and here;
// on this page the hash names the document, so a link to a section scrolls instead of navigating, and the
// address becomes #<document>:<section>, which opens the document on that section again.
function slug(text) {
  return text.trim().toLowerCase().replace(/[^\p{L}\p{N}\s-]/gu, "").replace(/\s/g, "-");
}

function scrollToSection(section) {
  doc.querySelector(`[id="${CSS.escape(section)}"]`)?.scrollIntoView();
}

// Fonts and formulas that arrive after a document is drawn reflow it and carry the heading away, on a
// phone by screens. Until the reader scrolls or leaves, the sheet follows the heading; past HOLD_MS the
// document has settled, and holding longer would fight a reader who is only reading.
const HOLD_MS = 4000;
const LET_GO = ["wheel", "touchstart", "pointerdown", "keydown", "hashchange"];
function holdSection(section) {
  scrollToSection(section);
  const follow = new ResizeObserver(() => scrollToSection(section));
  const letGo = () => {
    follow.disconnect();
    for (const event of LET_GO) removeEventListener(event, letGo);
  };
  follow.observe(doc);
  for (const event of LET_GO) addEventListener(event, letGo, { passive: true });
  setTimeout(letGo, HOLD_MS);
}

function anchorSections(body, docId) {
  for (const heading of body.querySelectorAll("h1, h2, h3, h4, h5, h6")) heading.id = slug(heading.textContent);
  for (const link of body.querySelectorAll('a[href^="#"]')) {
    const section = decodeURIComponent(link.getAttribute("href").slice(1));
    link.addEventListener("click", (event) => {
      event.preventDefault();
      history.replaceState(null, "", `#${docId}:${encodeURIComponent(section)}`);
      scrollToSection(section);
    });
  }
}

async function show(address) {
  const [id, section = ""] = address.split(/:(.*)/s);
  const entry = BY_ID.get(ALIASES[id] ?? id) ?? BY_ID.get(DEFAULT_DOC);
  const path = fileOf(entry);
  markCurrent(entry.id);
  doc.innerHTML = `<p class="state">${STR.loading}</p>`;
  let text;
  try {
    // Revalidated every time: a document changes without the page changing, and a cached copy would
    // show the reader a page that is no longer in the repository.
    const response = await fetch(path, { cache: "no-cache" });
    if (!response.ok) throw new Error(String(response.status));
    text = await response.text();
  } catch {
    doc.innerHTML = `<p class="state">${STR.failed} <a href="${REPO}${path}">${path}</a></p>`;
    return;
  }
  const body = document.createElement("div");
  const { text: markdown, formulas } = extractMath(stripFrontMatter(text));
  body.innerHTML = renderMath(marked.parse(markdown), formulas);
  anchorSections(body, entry.id);
  for (const link of body.querySelectorAll("a[href]:not([href^='#'])")) {
    const resolved = resolveLink(link.getAttribute("href"), path);
    if (resolved) link.setAttribute("href", resolved);
  }
  for (const table of body.querySelectorAll("table")) {
    const scroll = document.createElement("div");
    scroll.className = "table-scroll";
    table.replaceWith(scroll);
    scroll.append(table);
  }
  // A document with diagrams is drawn off screen at the sheet's width first: put on the sheet with the
  // sources of its diagrams, it would jump when they turn into pictures of another height.
  if (body.querySelector("pre > code.language-mermaid")) {
    const shown = location.hash;
    body.className = "doc doc-stage";
    body.style.width = doc.clientWidth + "px";
    document.body.append(body);
    await renderDiagrams(body);
    body.remove();
    body.className = ""; body.style.width = "";
    if (location.hash !== shown) return;   // the reader moved on while it was drawn
  }
  doc.innerHTML = `<p class="meta"><span>${path}</span> <a href="${REPO}${path}">${STR.source}</a></p>`;
  doc.append(...body.childNodes);
  document.title = `${entry.title[langIndex()]} - ${SITE_CFG.name}`;
  doc.focus?.();
  if (section) holdSection(decodeURIComponent(section));
}

addEventListener("hashchange", () => show(location.hash.slice(1)));
addEventListener("langchange", () => { renderMenu(); show(location.hash.slice(1) || DEFAULT_DOC); });
show(location.hash.slice(1) || DEFAULT_DOC);
