/* ==== WHAT'S NEW ==============================================================================
   Every version as a digest, from version.json - the one source the GitHub release and the package on
   nuget.org take the notes from too; the published page carries them inside, a page served straight from
   the repository asks for the file (loadReleaseNotes); a version's "ru" holds the Russian text, shown
   when the page is read in Russian. Each version is a paragraph: its headline led by its number, its points.

   The digest stands in a window of fixed height, so filling it moves nothing on the page and a short
   note leaves no hole. One loop holds the window and the field: the buttons step one version and the
   window puts it at its top, a version typed into the field - part by part between fixed dots, only
   versions that exist - moves the window to it, and any scroll puts the version at the top into the
   field. On a touch screen only the buttons move the window - a finger on it scrolls the page, so the
   page never gets stuck in the list (site.css).

   Four parts, each with one job: loading the notes, drawing the digest, the window and its buttons,
   the field. The entry at the bottom wires them. */

/* The notes of the released versions, newest first; null when they do not come. The published page has
   them inside, written with the list by scripts/build-site.py, so it asks for nothing; a page served
   straight from the repository asks for version.json. A version with a label, as 2.8.0-rc on dev, is not
   out yet, so the page does not show it (CONTRIBUTING.md). */
async function loadReleaseNotes() {
  const built = document.getElementById("release-notes");
  if (built) {
    try {
      return JSON.parse(built.textContent);
    } catch (e) {
      return null;
    }
  }
  try {
    const response = await fetch("version.json", { cache: "no-cache" });
    if (!response.ok) return null;
    const notes = (await response.json()).releaseNotes;
    return Object.fromEntries(Object.entries(notes).filter(([version]) => !version.includes("-")));
  } catch (e) {
    return null;
  }
}

/* Draws every version into the list in the language asked; a version without "ru" stays English. */
function drawDigest(list, releaseNotes, lang) {
  list.replaceChildren(...Object.keys(releaseNotes).map((version) => {
    const notes = releaseNotes[version];
    const { headline, highlights } = lang === "ru" && notes.ru ? notes.ru : notes;
    const item = document.createElement("div");
    item.className = "releases__item";
    item.dataset.version = version;
    const number = document.createElement("span");
    number.className = "releases__version";
    number.textContent = "v" + version;
    const title = document.createElement("p");
    title.className = "releases__headline";
    title.append(number, " " + headline);
    item.append(title);
    if (highlights.length) {
      const points = document.createElement("ul");
      for (const point of highlights) {
        const li = document.createElement("li");
        li.textContent = point;
        points.append(li);
      }
      item.append(points);
    }
    return item;
  }));
}

/* The window. A version is put to its top - under the top fade, at the window's scroll padding - by to();
   the buttons step one version newer or older that way. A scroll, however made, tells onMove the version
   now at the top, and marks the ends of the list, so the buttons say when there is nothing further and
   the fade at an edge shows only where text is behind it; so does a change of the window's size, when a
   phone turns. Returns to, place, top and edges, which brings the buttons and the marks up to date after
   the content changed. */
function scrollWindow(list, up, down, onMove) {
  const inset = () => parseFloat(getComputedStyle(list).scrollPaddingTop);
  const offset = (item) => item.getBoundingClientRect().top - list.getBoundingClientRect().top - inset();

  // the buttons glide a step, unless reduced motion is asked for; the field makes a smooth jump (site/jump.js), however
  // far the version is, faster than the page does: the window is small
  function to(item, glide) {
    const top = list.scrollTop + offset(item);
    const still = matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (glide) list.scrollTo({ top, behavior: still ? "instant" : "smooth" });
    else jumpTo(list, top, 200);
  }

  // at once, with no motion: a version kept at the top when the digest is drawn anew
  function place(item) {
    list.scrollTop += offset(item);
  }

  // the top version: the first one that reaches below the top fade
  function top() {
    const line = list.getBoundingClientRect().top + inset() + 1;
    return [...list.children].find((item) => item.getBoundingClientRect().bottom > line);
  }

  function edges() {
    up.disabled = list.scrollTop <= 0;
    down.disabled = list.scrollTop + list.clientHeight >= list.scrollHeight - 1;
    list.classList.toggle("releases--scrolled", !up.disabled);
    list.classList.toggle("releases--end", down.disabled);
    const item = top();
    if (item) onMove(item.dataset.version);
  }

  // newer: the top version itself when it is partly scrolled past, else the one before it
  up.addEventListener("click", () => {
    const item = top();
    if (!item) return;
    to(offset(item) < -2 ? item : item.previousElementSibling ?? item, true);
  });
  down.addEventListener("click", () => {
    const next = top()?.nextElementSibling;
    if (next) to(next, true);
  });
  list.addEventListener("scroll", edges, { passive: true });
  new ResizeObserver(edges).observe(list);
  return { to, place, top, edges };
}

/* The field: a mask of parts between fixed dots that takes only a version that exists. A part accepts a
   digit only while some version, with the parts before it, still starts so - a digit refused shakes the
   field and shows under it the versions that can go there; once a part's value can grow no further,
   the cursor goes to the next part by itself. A dot or a space goes on too, Backspace in an
   empty part goes back, Enter goes to the version and lets the field go, and a changed part clears the
   parts after it. The window moves to the newest
   version that starts with what is typed. While the reader types, the field is theirs; otherwise show()
   puts a version into it. Returns show. */
function versionField(parts, list, versions, to) {
  const split = versions.map((version) => version.split("."));
  const field = parts[0].parentElement;
  const hint = field.querySelector(".releases__hint");
  let hide;

  // what can go into part i after the parts before it, as a hint for a digit refused there: "v2.x, v1.x"
  // on the first part, "v2.8.x, v2.7.x" on the second, whole versions on the last
  function refuse(i) {
    const before = parts.slice(0, i).map((part) => part.value);
    const tail = i < parts.length - 1 ? ".x" : "";
    const fit = values(i).map((value) => "v" + [...before, value].join(".") + tail);
    hint.textContent = fit.slice(0, 5).join(", ") + (fit.length > 5 ? ", ..." : "");
    field.classList.remove("releases--refused");
    void field.offsetWidth; // restart the shake
    field.classList.add("releases--refused");
    clearTimeout(hide);
    hide = setTimeout(() => field.classList.remove("releases--refused"), 3000);
  }

  // the values a part can take after the parts before it, as typed
  function values(i) {
    const before = parts.slice(0, i).map((part) => part.value);
    return [...new Set(split.filter((own) => before.every((value, k) => own[k] === value)).map((own) => own[i]))];
  }

  function find() {
    const typed = parts.map((part) => part.value);
    const filled = typed.findIndex((value) => !value);
    const count = filled === -1 ? typed.length : filled;
    const items = [...list.children];
    const target = count
      ? items.find((item) => {
          const own = item.dataset.version.split(".");
          return typed.slice(0, count).every((value, k) => (k < count - 1 ? own[k] === value : own[k].startsWith(value)));
        })
      : null;
    for (const item of items) item.classList.toggle("releases--found", item === target);
    if (target) to(target);
    return target;
  }

  function show(version) {
    if (parts.includes(document.activeElement)) return;
    version.split(".").forEach((value, i) => { if (parts[i]) parts[i].value = value; });
    for (const item of list.children) item.classList.remove("releases--found");
  }

  parts.forEach((part, i) => {
    part.placeholder = versions[0].split(".")[i] ?? "0";
    part.disabled = false;
    let kept = "";
    // a part taken in hand is selected, so what is typed replaces the version the field showed
    part.addEventListener("focus", () => { kept = part.value; part.select(); });
    part.addEventListener("input", () => {
      const goOn = /[.,\s]/.test(part.value);
      const digits = part.value.replace(/\D/g, "");
      const allowed = values(i);
      if (digits && !allowed.some((value) => value.startsWith(digits))) {
        part.value = kept;
        refuse(i);
        return;
      }
      field.classList.remove("releases--refused");
      part.value = kept = digits;
      for (const later of parts.slice(i + 1)) later.value = "";
      find();
      const complete = allowed.includes(digits);
      const grows = allowed.some((value) => value !== digits && value.startsWith(digits));
      if (parts[i + 1] && complete && (goOn || !grows)) parts[i + 1].focus();
    });
    part.addEventListener("keydown", (event) => {
      if (event.key === "Backspace" && !part.value && parts[i - 1]) {
        event.preventDefault();
        parts[i - 1].focus();
      } else if (event.key === "Enter") {
        // the version as typed so far, written out whole, and the field is let go: a scroll puts versions
        // into it again. Written here, since a window already there does not scroll and would not write it
        event.preventDefault();
        const target = find();
        part.blur();
        if (target) show(target.dataset.version);
      }
    });
  });
  return { show };
}

(async () => {
  const box = document.querySelector(".releases");
  if (!box) return;
  const releaseNotes = await loadReleaseNotes();
  if (!releaseNotes) {
    box.classList.add("releases--failed");
    return;
  }

  const root = document.documentElement;
  const list = box.querySelector(".releases__list");
  // one loop: a scroll puts the top version into the field; the buttons and the field move the window
  let field;
  const view = scrollWindow(list, box.querySelector(".releases__up"), box.querySelector(".releases__down"), (version) => field.show(version));
  field = versionField([...box.querySelectorAll(".releases__find input")], list, Object.keys(releaseNotes), view.to);

  // the version at the top stays at the top through a change of language, though the texts differ in
  // height; the panel switches the language by the attribute
  function draw() {
    const kept = view.top()?.dataset.version;
    drawDigest(list, releaseNotes, root.getAttribute("data-lang"));
    const item = kept && list.querySelector(`[data-version="${CSS.escape(kept)}"]`);
    if (item) view.place(item);
    view.edges();
  }
  draw();
  new MutationObserver(draw).observe(root, { attributes: true, attributeFilter: ["data-lang"] });
})();
