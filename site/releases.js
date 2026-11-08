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
   the field. The window and the field are the shared mechanics of site/version-window.js
   (window.VersionWindow); this page draws the digest and wires the parts. */

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

/* The window and the field: the shared mechanics (site/version-window.js). */
const { scrollWindow, versionField } = window.VersionWindow;

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
