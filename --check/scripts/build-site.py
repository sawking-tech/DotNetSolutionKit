#!/usr/bin/env python3
"""Builds the site for GitHub Pages: build-site.py <out folder>

The site is the repository's files as they are, with index.html completed from version.json, the one source
of the release notes:

- the "What's new" list gets the released versions written into it, in English, so a reader without
  JavaScript - a bot, a search engine - sees them; the notes of every language go next to it as JSON, and
  site/releases.js reads them from there instead of asking for version.json, so nothing moves while the page
  loads;
- a JSON-LD SoftwareApplication names the latest release and its notes.

A version with a label, as 2.8.0-rc on dev, is not out yet and stays out (CONTRIBUTING.md). The build stops
on a version.json it cannot read, on no released version, and on a released version without a headline, so
the site keeps its last good build.
"""
import html
import json
import os
import shutil
import subprocess
import sys

LIST = '<div class="releases__list" tabindex="0" aria-label="Versions"></div>'
LD_END = "</script>"
SITE = "https://dotskit.sawking.tech/"


def fail(message):
    print(f"::error::{message}")
    sys.exit(1)


def released(notes):
    out = {version: note for version, note in notes.items() if "-" not in version}
    if not out:
        fail("version.json has no released version")
    for version, note in out.items():
        if not (note.get("headline") or "").strip():
            fail(f"version.json: {version} has no headline")
    return out


def item(version, note):
    points = "".join(f"<li>{html.escape(p)}</li>" for p in note.get("highlights", []))
    return (f'<div class="releases__item" data-version="{html.escape(version)}">'
            f'<p class="releases__headline"><span class="releases__version">v{html.escape(version)}</span> '
            f'{html.escape(note["headline"])}</p>' + (f"<ul>{points}</ul>" if points else "") + "</div>")


def application(version, note):
    text = " ".join([note["headline"], *note.get("highlights", [])])
    return {
        "@context": "https://schema.org",
        "@type": "SoftwareApplication",
        "name": "DotNetSolutionKit",
        "url": SITE,
        "applicationCategory": "DeveloperApplication",
        "operatingSystem": "Windows, macOS, Linux",
        "softwareVersion": version,
        "releaseNotes": text,
        "downloadUrl": f"https://www.nuget.org/packages/SawKing.DotNetSolutionKit/{version}",
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
    }


def main():
    if len(sys.argv) != 2:
        fail("usage: build-site.py <out folder>")
    out = sys.argv[1]

    try:
        notes = json.load(open("version.json", encoding="utf-8"))["releaseNotes"]
    except (OSError, ValueError, KeyError) as e:
        fail(f"version.json cannot be read: {e}")
    notes = released(notes)
    latest = next(iter(notes))

    # the repository's files, as the Pages branch serves them
    files = subprocess.run(["git", "ls-files", "-z"], check=True, capture_output=True).stdout.decode().split("\0")
    shutil.rmtree(out, ignore_errors=True)
    for name in filter(None, files):
        target = os.path.join(out, name)
        os.makedirs(os.path.dirname(target) or out, exist_ok=True)
        shutil.copy2(name, target)

    page_path = os.path.join(out, "index.html")
    page = open(page_path, encoding="utf-8").read()
    if page.count(LIST) != 1:
        fail("index.html: the empty releases list is not there once")
    data = json.dumps(notes, ensure_ascii=False).replace("</", "<\\/")
    page = page.replace(LIST, LIST[:-len("</div>")] + "".join(item(v, n) for v, n in notes.items()) + "</div>\n"
                        + f'        <script type="application/json" id="release-notes">{data}</script>')

    ld = page.find('<script type="application/ld+json">')
    if ld < 0:
        fail("index.html: no JSON-LD to put the release beside")
    end = page.index(LD_END, ld) + len(LD_END)
    software = json.dumps(application(latest, notes[latest]), ensure_ascii=False, indent=2).replace("</", "<\\/")
    page = page[:end] + f'\n<script type="application/ld+json">\n{software}\n</script>' + page[end:]

    open(page_path, "w", encoding="utf-8", newline="\n").write(page)
    print(f"Built {out}: {len(notes)} released versions, the latest {latest}")


if __name__ == "__main__":
    main()
