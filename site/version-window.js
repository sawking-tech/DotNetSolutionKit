/* ==== THE VERSION WINDOW ======================================================================
   The window of "What's new" (site/releases.js) and of "By release" on the samples page
   (site/samples.js): a list of versions in a box of fixed height, moved by the buttons, by the
   field and by a scroll. This module holds the window's mechanics; a page draws its items and
   wires the parts:

   - scrollWindow(list, up, down, onMove): the list scrolls one version to its top - under the top
     fade, at the window's scroll padding - by to(); the buttons step one version newer or older
     that way. A scroll, however made, tells onMove the version now at the top, and marks the ends
     of the list, so the buttons say when there is nothing further and the fade at an edge shows
     only where text is behind it; so does a change of the window's size. Returns to, place, top
     and edges, which brings the buttons and the marks up to date after the content changed.
   - versionField(parts, list, versions, to): a mask of parts between fixed dots that takes only a
     version that exists. Returns show.

   The smooth far jump of to() without glide is jumpTo (site/jump.js): a short way eases, a long way
   passes under a blur. A page without jump.js gets an instant move. */

window.VersionWindow = (() => {

/* The window. A version is put to its top - under the top fade, at the window's scroll padding - by to();
   the buttons glide a step, unless reduced motion is asked for; the field makes a smooth jump,
   however far the version is, faster than the page does: the window is small. */
function scrollWindow(list, up, down, onMove) {
  const inset = () => parseFloat(getComputedStyle(list).scrollPaddingTop);
  const offset = (item) => item.getBoundingClientRect().top - list.getBoundingClientRect().top - inset();

  function to(item, glide) {
    const top = list.scrollTop + offset(item);
    const still = matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (glide) list.scrollTo({ top, behavior: still ? "instant" : "smooth" });
    else jumpTo(list, top, 200);
  }

  // at once, with no motion: a version kept at the top when the list is drawn anew
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

/* The field: a mask of parts between fixed dots that takes only a key that exists. A part accepts a
   character only while some key, with the parts before it, still starts so - a character refused
   shakes the field and shows under it the keys that can go there; once a part's value can grow no
   further, the cursor goes to the next part by itself. A dot or a space goes on too, Backspace in an
   empty part goes back, Enter goes to the key and lets the field go, and a changed part clears
   the parts after it. The window moves to the newest key that starts with what is typed. While the
   reader types, the field is theirs; otherwise show() puts a key into it and clear() empties it.

   One mechanics, two instances (site/samples.js): a version field and a commit field. The defaults
   are the version field; options carry the little that a commit of master needs - its alphabet, how
   a key splits into parts (a commit is one part, no dots), and how a refused character's hint
   spells a key. Nothing else differs. */
function versionField(parts, list, versions, to, options = {}) {
  const strip = options.strip ?? /\D/g;             // a part keeps what this does not take out
  const cut = options.cut ?? ((key) => key.split("."));
  const spell = options.spell ?? ((key, open) => "v" + key.join(".") + (open ? ".x" : ""));
  const split = versions.map(cut);
  const field = parts[0].parentElement;
  const hint = field.querySelector(".releases__hint");
  let hide;

  // what can go into part i after the parts before it, as a hint for a character refused there:
  // "v2.x, v1.x" on the first part of a version, "v2.8.x, v2.7.x" on the second, whole keys on the last
  function refuse(i) {
    const before = parts.slice(0, i).map((part) => part.value);
    const fit = values(i).map((value) => spell([...before, value], i < parts.length - 1));
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
          const own = cut(item.dataset.version);
          return typed.slice(0, count).every((value, k) => (k < count - 1 ? own[k] === value : own[k].startsWith(value)));
        })
      : null;
    for (const item of items) item.classList.toggle("releases--found", item === target);
    if (target) to(target);
    return target;
  }

  function show(key) {
    if (parts.includes(document.activeElement)) return;
    cut(key).forEach((value, i) => { if (parts[i]) parts[i].value = value; });
    for (const item of list.children) item.classList.remove("releases--found");
  }

  // the other instance of the mechanics takes the floor: nothing of this field's, until a scroll
  // puts a key back. Not while the reader types here, as in show
  function clear() {
    if (parts.includes(document.activeElement)) return;
    for (const part of parts) part.value = "";
  }

  parts.forEach((part, i) => {
    part.placeholder = cut(versions[0])[i] ?? "0";
    part.disabled = false;
    let kept = "";
    // a part taken in hand is selected, so what is typed replaces the key the field showed
    part.addEventListener("focus", () => { kept = part.value; part.select(); });
    part.addEventListener("input", () => {
      const goOn = /[.,\s]/.test(part.value);
      const typed = part.value.replace(strip, "");
      const allowed = values(i);
      if (typed && !allowed.some((value) => value.startsWith(typed))) {
        part.value = kept;
        refuse(i);
        return;
      }
      field.classList.remove("releases--refused");
      part.value = kept = typed;
      for (const later of parts.slice(i + 1)) later.value = "";
      find();
      const complete = allowed.includes(typed);
      const grows = allowed.some((value) => value !== typed && value.startsWith(typed));
      if (parts[i + 1] && complete && (goOn || !grows)) parts[i + 1].focus();
    });
    part.addEventListener("keydown", (event) => {
      if (event.key === "Backspace" && !part.value && parts[i - 1]) {
        event.preventDefault();
        parts[i - 1].focus();
      } else if (event.key === "Enter") {
        // the key as typed, written out whole, and the field is let go: a scroll puts keys
        // into it again. Written here, since a window already there does not scroll and would not write it
        event.preventDefault();
        const target = find();
        part.blur();
        if (target) show(target.dataset.version);
      }
    });
  });
  return { show, clear };
}

return { scrollWindow, versionField };
})();
