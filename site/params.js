/* ==== THE PARAMETERS' SWITCH ==================================================================
   The table opens and closes by CSS alone; this keeps the reader where they were. Opening adds rows under
   the last one seen, so the page stays put. Closing takes back the view the reader opened from, the last
   of the first rows over the switch; without one, the switch keeps its place in the view, or the rows
   above it would drop the page by their height. */
(() => {
  const box = document.getElementById("params-all");
  const label = document.querySelector('label[for="params-all"]');
  if (!box || !label) return;

  let openedAt = null;
  let switchTop = null;
  const remember = () => { switchTop = label.getBoundingClientRect().top; };

  // a click on the label would focus the checkbox, and once the rows above open the browser scrolls to it
  label.addEventListener("click", e => {
    e.preventDefault();
    remember();
    box.checked = !box.checked;
    box.dispatchEvent(new Event("change"));
  });
  box.addEventListener("keydown", e => { if (e.key === " ") remember(); });
  box.addEventListener("change", () => {
    if (box.checked) openedAt = window.scrollY;
    else if (openedAt !== null) window.scrollTo(0, openedAt);
    else if (switchTop !== null) window.scrollBy(0, label.getBoundingClientRect().top - switchTop);
    if (!box.checked) openedAt = null;
    switchTop = null;
  });
})();
