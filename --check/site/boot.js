/* The theme is set before the first paint, from the reader's own choice or, by default, from the
   device - a class applied later would show one palette and then swap to the other. */
/* A link can carry the language: ?lang=ru opens the page in Russian whatever this browser
   remembers, so a page can be shared the way it was read. */
(() => {
  const key = (window.SITE && SITE.key) || "site";
  const asked = new URLSearchParams(location.search).get("lang");
  const lang = asked === "ru" || asked === "en" ? asked : null;
  const root = document.documentElement;
  // The page stays hidden until panel.js has laid it out and the fonts are in (site.css, .unlaid); if the
  // fonts are slow or a script does not come, it is shown after this long anyway.
  const REVEAL_CAP_MS = 1500;
  root.classList.add("unlaid");
  setTimeout(() => root.classList.remove("unlaid"), REVEAL_CAP_MS);
  try {
    root.setAttribute("data-theme", localStorage.getItem(key + ".theme") || "auto");
    root.setAttribute("data-lang", lang || localStorage.getItem(key + ".lang") || "en");
  } catch (e) {
    root.setAttribute("data-theme", "auto");
    root.setAttribute("data-lang", lang || "en");
  }
})();
