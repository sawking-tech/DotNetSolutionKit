/* ==== THE SMOOTH JUMP =========================================================================
   The distance between parts of a page is the time it takes to get there, not the page's length: another
   page opens at once, so a part of the same page is reached in the same short time, however far it is.

   A long way is not scrolled through. With H the height of what is seen, the content speeds up toward
   the target for one H, and what comes out from under the near edge is blurred - exactly the band it
   has moved, so at the end of that H the whole view is blurred. Then it is put one H short of the
   target, and slows down to it for that H while what leaves through the far edge is blurred - again
   exactly the band still to go, so the view starts all blurred and clears as it stops. The cut falls
   between two fully blurred frames and is not seen; the speed going in, about 3H/T at the cut, is the
   speed coming out. A way of up to 2H has nothing to hide: the content speeds up and slows down to the
   target with no blur, in the same time. With reduced motion asked for, the jump is instant.

   jumpTo(scroller, top, half): scroller is window or a scrolling element, top the place to stand at, half
   the time of one phase in ms (JUMP.half by default; a small window can go faster). The blur is a layer
   over what is seen of the scroller - the content column under the bar, or the element's box. */
const JUMP = { half: 300, blur: 10, ground: 92, edge: 0.12 };
let jumpRun = 0;
let jumpLayer;

function jumpTo(scroller, top, half = JUMP.half) {
  const at = () => (scroller === window ? scrollY : scroller.scrollTop);
  const put = (y) => scroller.scrollTo({ top: y, behavior: "instant" });
  // a place past the end is the end: the content stops there, so the way ends there too
  const end = scroller === window
    ? document.documentElement.scrollHeight - innerHeight
    : scroller.scrollHeight - scroller.clientHeight;
  top = Math.max(0, Math.min(top, end));
  const from = at();
  const way = top - from;
  const toward = Math.sign(way);
  const run = ++jumpRun;
  // a jump cut short leaves no veil behind: its frames stop, so the new jump takes the layer down
  if (jumpLayer) jumpLayer.style.display = "none";
  if (!way) return;
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return put(top);

  // what is seen of the scroller: the content column under the bar, or the element's box
  const column = (document.getElementById("below") ?? document.body).getBoundingClientRect();
  const barBottom = document.querySelector(".bar")?.getBoundingClientRect().bottom ?? 0;
  const box = scroller === window
    ? { top: barBottom, left: column.left, width: column.width, height: innerHeight - barBottom }
    : scroller.getBoundingClientRect();
  const H = box.height;

  // one phase: t runs 0..1 over the time, step draws a frame
  const phase = (time, step) => new Promise((done) => {
    const start = performance.now();
    requestAnimationFrame(function frame(now) {
      if (run !== jumpRun) return;   // a newer jump took over
      const t = Math.min(1, (now - start) / time);
      step(t);
      if (t < 1) requestAnimationFrame(frame); else done();
    });
  });
  const easeIn = (t) => t * t * t;
  const easeOut = (t) => 1 - (1 - t) ** 3;

  // near: speed up and slow down straight to the target, nothing to blur
  if (Math.abs(way) <= 2 * H) {
    const easeInOut = (t) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);
    return phase(2 * half, (t) => put(from + way * easeInOut(t)));
  }

  if (!jumpLayer) {
    jumpLayer = document.createElement("div");
    jumpLayer.className = "jump-blur";
    document.body.append(jumpLayer);
  }
  Object.assign(jumpLayer.style, {
    top: `${box.top}px`, left: `${box.left}px`, width: `${box.width}px`, height: `${H}px`,
    display: "block",
  });
  // how thick the veil is, 0..1: it thickens toward the cut, so the cut happens under nearly the page's ground
  const thick = (v) => {
    const blur = `blur(${(JUMP.blur * v).toFixed(2)}px)`;
    jumpLayer.style.backdropFilter = blur;
    jumpLayer.style.webkitBackdropFilter = blur;
    jumpLayer.style.backgroundColor = `color-mix(in srgb, var(--ground) ${(JUMP.ground * v).toFixed(1)}%, transparent)`;
  };
  // a blurred band of the given height at one edge of the view, soft toward the middle;
  // "near" is the edge the content moves toward: the bottom when going down
  const soft = JUMP.edge * H;
  const band = (edge, height) => {
    const side = (edge === "near") === (toward > 0) ? "to top" : "to bottom";
    const mask = `linear-gradient(${side}, #000 ${Math.max(0, height - soft)}px, transparent ${height}px)`;
    jumpLayer.style.maskImage = mask;
    jumpLayer.style.webkitMaskImage = mask;
  };

  // in: what came out from under the near edge, the band moved so far; out: what is still to leave through the far edge
  phase(half, (t) => { const moved = H * easeIn(t); put(from + toward * moved); band("near", moved + soft * easeIn(t)); thick(easeIn(t)); })
    .then(() => phase(half, (t) => { const left = H * (1 - easeOut(t)); put(top - toward * left); band("far", left + soft * (1 - easeOut(t))); thick(1 - easeOut(t)); }))
    .then(() => { if (run === jumpRun) jumpLayer.style.display = "none"; });
}

/* Links to a place on the same page jump instead of scrolling. As a plain link would, the address takes
   the hash - the same hash twice is one entry in the history - and the target takes the focus, so the
   next Tab goes on from there. */
document.addEventListener("click", (event) => {
  const link = event.target.closest('a[href^="#"]');
  if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  const id = decodeURIComponent(link.hash.slice(1));
  const target = id === "top" ? document.body : document.getElementById(id);
  if (!target) return;
  event.preventDefault();
  if (link.hash === location.hash) history.replaceState(null, "", link.hash);
  else history.pushState(null, "", link.hash);
  if (!target.matches("a, button, input, select, textarea, [tabindex]")) target.setAttribute("tabindex", "-1");
  target.focus({ preventScroll: true });
  const margin = parseFloat(getComputedStyle(target).scrollMarginTop) || 0;
  jumpTo(window, id === "top" ? 0 : target.getBoundingClientRect().top + scrollY - margin);
});
