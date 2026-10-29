#!/usr/bin/env node
/**
 * The work plan: where I am, what comes next, what is broken.
 *
 * @remarks
 * The plan is KDL and is edited by hand, on purpose: the edits are of many kinds, and a command for each
 * would give a tool that half of the edits bypass anyway.
 *
 * The tool covers something else - what goes wrong silently when done by hand:
 *
 *   - moving the marker, where three changes have to happen together;
 *   - invariants that are syntactically valid and therefore invisible;
 *   - showing the queue as plain numbers when the ids are opaque.
 *
 * Nothing is repaired automatically. A repair such as "I will set since myself" destroys the signal: it
 * stops showing that the marker is being moved the wrong way.
 *
 * It lives next to the skill that describes the format.
 *
 * The plan file comes from PLAN_FILE; by default, next to the session notes.
 */

import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const PLAN = process.env['PLAN_FILE'] ?? '.claude/session-context/plan.kdl';

/** A plan node, in the shape that is convenient to query. */
class Node {
  constructor(line, indent, kind, id, title, attrs) {
    this.line = line;
    this.indent = indent;
    this.kind = kind;
    this.id = id;
    this.title = title;
    this.attrs = attrs;
    this.fields = { why: null, how: null, done_when: null };
    this.notes = [];
    this.checks = [];
  }

  get now() {
    return this.attrs['now'] === 'true';
  }
  get done() {
    return this.attrs['done'] === 'true';
  }
  get dropped() {
    return 'dropped' in this.attrs;
  }
  get blocked() {
    return 'blocked' in this.attrs;
  }
  /** Dead ends and closed items are not in the queue: they are no longer work. */
  get pending() {
    return !this.done && !this.dropped;
  }
}

/** The limits of the plan's form: past them the plan stops being a list of actions. */
const LIMITS = { planLines: 100, fieldChars: 160, comments: 3, farChecks: 2 };

const COMMENT = /^\s*\/\//;
const HEAD = /^(\s*)(stage|step)\s+(\S+)\s+"([^"]*)"(.*)$/;
const FIELD = /^\s*(why|how|done_when|note|check)\s+"/;

/**
 * The whole value of a field, including the part wrapped onto the next lines.
 *
 * @remarks
 * Long notes are wrapped for the width of the window, and the closing quote is two or three lines after the
 * opening one. Parsing one line at a time does not see such a value at all, and the item drops out of the
 * checks with it: zero notes, so "the far work is written out" stays silent while the far work is written
 * out. In a plan of a hundred fields three quarters were lost that way.
 *
 * Values contain no quotes, so the first closing quote ends the value.
 */
function readField(lines, from) {
  const open = lines[from].indexOf('"');
  let value = lines[from].slice(open + 1);
  let last = from;
  while (!value.includes('"')) {
    last += 1;
    if (last >= lines.length) return null;
    value += ' ' + lines[last].trim();
  }
  const end = value.indexOf('"');
  return { value: value.slice(0, end), tail: value.slice(end + 1), last };
}

/** The end of a node: the line before the next node with the same or a smaller indent. */
function endLine(node, nodes) {
  const after = nodes.find((n) => n.line > node.line && n.indent <= node.indent);
  return after ? after.line - 1 : Number.POSITIVE_INFINITY;
}

/**
 * Whether the item lies inside this stage.
 *
 * @remarks
 * A stage that holds the marker does not count as "above the marker": it is not forgotten work, it is the
 * frame around the current one.
 */
function wraps(node, target, nodes) {
  return node.line < target.line && endLine(node, nodes) >= target.line;
}

/** Attribute values: `done=true`, `since="..."`, `blocked="reason"`. */
function parseAttrs(tail) {
  const attrs = {};
  for (const m of tail.matchAll(/(\w+)=(?:"([^"]*)"|(\S+))/g)) {
    attrs[m[1]] = (m[2] ?? m[3] ?? '').replace(/\{$/, '').trim();
  }
  return attrs;
}

/**
 * @remarks
 * Lines are split on CRLF too: in JS a dot does not match `\r`, so the heading's `(.*)$` never matches in a
 * CRLF file - the plan reads as empty, "no marker at all". That happened on Windows. The file's line ending
 * is kept when writing.
 */
function parse(text) {
  const lines = text.split(/\r?\n/);
  const nodes = [];
  let current = null;

  for (let i = 0; i < lines.length; i += 1) {
    const raw = lines[i];
    const head = HEAD.exec(raw);
    if (head) {
      current = new Node(i + 1, head[1].length, head[2], head[3], head[4], parseAttrs(head[5]));
      nodes.push(current);
      continue;
    }
    const opens = current && FIELD.exec(raw);
    if (!opens) continue;

    const field = readField(lines, i);
    if (!field) continue;
    const name = opens[1];
    if (name === 'note') current.notes.push(field.value);
    else if (name === 'check')
      current.checks.push({ text: field.value, ...parseAttrs(field.tail) });
    else current.fields[name] = field.value;
    i = field.last;
  }

  return { lines, nodes };
}

/**
 * The invariants of the plan.
 *
 * @remarks
 * They are syntactically valid: a file that breaks them parses and looks whole. That is why the eye misses
 * them, and only a check shows them.
 */
function invariants(nodes, lines) {
  const bad = [];
  const say = (node, text) => bad.push({ line: node?.line ?? 0, id: node?.id ?? '-', text });

  const marked = nodes.filter((n) => n.now);
  if (marked.length === 0) say(null, 'no marker at all: it is unclear what is being worked on');
  if (marked.length > 1) {
    say(null, `${marked.length} markers, there must be one: ${marked.map((n) => n.id).join(', ')}`);
  }

  for (const node of nodes) {
    if (node.now && !node.attrs['since']) {
      say(node, 'taken into work without since - the session will not be counted');
    }
    if (node.done && !node.attrs['at']) say(node, 'closed without at');
    if (
      node.done &&
      node.attrs['at'] &&
      node.attrs['since'] &&
      node.attrs['at'] < node.attrs['since']
    ) {
      say(node, `at (${node.attrs['at']}) is earlier than since (${node.attrs['since']})`);
    }
    if (node.kind === 'step' && node.attrs['state']) {
      say(node, 'state on a step: there are five states, and words do not replace them');
    }
    if (!node.now && node.notes.length > 0 && node.pending) {
      say(node, `${node.notes.length} free notes on an item that is not current: the far work is written out`);
    }
    // fix="slug" - the repair is an item of its own; the check stays an honest fact
    for (const check of node.checks) {
      if (check['ok'] === 'false' && !nodes.some((n) => n.id === check['fix'])) {
        say(node, `a check failed and no repair item exists: "${check.text.slice(0, 40)}"`);
      }
    }
  }

  form(nodes, lines, say);

  /*
    The order of the document is the priority, so an open item above the marker reads as done - and is
    forgotten. It is a property of the sequence, not of a node: every item on its own is fine, their order
    lies. Hence the blindness: the file parses, the invariants hold, and half of the work is invisible.
  */
  const at = nodes.findIndex((n) => n.now);
  if (at > 0) {
    const above = nodes.slice(0, at).filter((n) => n.pending && !wraps(n, nodes[at], nodes));
    if (above.length) {
      const named = above
        .slice(0, 5)
        .map((n) => n.id + ':' + n.line)
        .join(', ');
      say(
        null,
        `${above.length} open items above the marker (${named}${above.length > 5 ? ', ...' : ''}) - they read as done`,
      );
    }
  }
  return bad;
}

/**
 * The form of the plan.
 *
 * @remarks
 * The plan is a list of actions, rebuilt at any time from the tracker's issues. What swells it into a
 * warehouse shows in its form, so it is checked here instead of staying a request in the skill's text: a
 * request gets broken, a check does not.
 */
function form(nodes, lines, say) {
  // A file that ends with a newline splits into one empty line more than it has.
  const count = lines.at(-1) === '' ? lines.length - 1 : lines.length;
  if (count > LIMITS.planLines) {
    say(null, `the plan is ${count} lines, the limit is ${LIMITS.planLines}: closed and far items go to the tracker, actions stay in the plan`);
  }
  const comments = lines.filter((l) => COMMENT.test(l)).length;
  if (comments > LIMITS.comments) {
    say(null, `${comments} comments, the limit is ${LIMITS.comments}: decisions and agreements go to the issue, an ADR, the docs or memory`);
  }
  for (const node of nodes) {
    for (const [name, value] of Object.entries(node.fields)) {
      if (value && value.length > LIMITS.fieldChars) {
        say(node, `${name} is ${value.length} characters, the limit is ${LIMITS.fieldChars}: one line on what we do; the analysis goes to the tracker`);
      }
    }
    for (const check of node.checks) {
      if (check.text.length > LIMITS.fieldChars) {
        say(node, `check is ${check.text.length} characters: a check is what is run and gives a got, not an agreement`);
      }
    }
    if (!node.now && node.pending && node.checks.length > LIMITS.farChecks) {
      say(node, `${node.checks.length} checks on a far item: the far work is written out - expand it when reached`);
    }
  }
}

const minutesSince = (stamp) => {
  const then = Date.parse(stamp.replace(' ', 'T'));
  if (Number.isNaN(then)) return null;
  return Math.round((Date.now() - then) / 60000);
};

const forHuman = (minutes) =>
  minutes === null ? '' : minutes < 90 ? `${minutes} min` : `${Math.round(minutes / 60)} h`;

/** Where I am: the current item in full, the next ones as numbers, the violations next to them. */
function where({ nodes, lines }, tail = 6) {
  const out = [];
  const queue = nodes.filter((n) => n.pending);
  const at = queue.findIndex((n) => n.now);
  const current = at >= 0 ? queue[at] : null;

  if (!current) {
    out.push('NOW     no marker - take an item: node plan.mjs take <slug>');
  } else {
    const spent = current.attrs['since']
      ? forHuman(minutesSince(current.attrs['since']))
      : 'no since';
    out.push(`NOW     1. ${current.title}   [${current.id}]   in work ${spent}`);
    for (const [name, label] of [
      ['why', 'why'],
      ['how', 'how'],
      ['done_when', 'done'],
    ]) {
      if (current.fields[name]) out.push(`        ${label.padEnd(7)} ${current.fields[name]}`);
      else out.push(`        ${label.padEnd(7)} - NOT FILLED IN`);
    }
    for (const note of current.notes) out.push(`        . ${note}`);
    for (const check of current.checks) {
      const mark = check['ok'] === 'true' ? '[v]' : check['ok'] === 'false' ? '[X]' : '[ ]';
      out.push(`        ${mark} ${check.text}${check['got'] ? ` -> ${check['got']}` : ''}`);
    }
    if (current.attrs['issue']) out.push(`        issue   ${current.attrs['issue']}`);
  }

  const above = current ? queue.slice(0, at).filter((n) => !wraps(n, current, nodes)) : [];
  if (above.length) {
    out.push('', 'ABOVE THE MARKER - open, and by its place reads as done');
    for (const node of above) {
      out.push('        ! ' + node.title + '   [' + node.id + ']   line ' + node.line);
    }
  }

  const next = queue.slice(at + 1, at + 1 + tail);
  if (next.length) {
    out.push('', 'NEXT');
    next.forEach((node, i) => {
      const flag = node.blocked ? `  (blocked: ${node.attrs['blocked']})` : '';
      out.push(`        ${i + 2}. ${node.title}   [${node.id}]${flag}`);
    });
  }

  const bad = invariants(nodes, lines);
  if (bad.length) {
    out.push('', `${bad.length} VIOLATIONS - in detail: node plan.mjs check`);
  }
  return out.join('\n');
}

function check({ nodes, lines }) {
  const bad = invariants(nodes, lines);
  if (!bad.length) return { text: 'The plan invariants hold.', code: 0 };
  const text = bad
    .map((b) => `  line ${String(b.line).padStart(4)}  [${b.id}]  ${b.text}`)
    .join('\n');
  return { text: `Violations: ${bad.length}\n${text}`, code: 1 };
}

const stamp = () => {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

/** Rewrites a node's heading line: removes attributes and adds its own. */
function rewriteHead(line, { drop = [], set = {} }) {
  const head = HEAD.exec(line);
  if (!head) return line;
  let tail = head[5];
  for (const name of drop) tail = tail.replace(new RegExp(`\\s*${name}=(?:"[^"]*"|\\S+)`), '');
  const brace = tail.includes('{');
  tail = tail.replace('{', '').trimEnd();
  for (const [name, value] of Object.entries(set)) tail += ` ${name}="${value}"`;
  return `${head[1]}${head[2]} ${head[3]} "${head[4]}"${tail}${brace ? ' {' : ''}`;
}

function take(parsed, slug) {
  const target = parsed.nodes.find((n) => n.id === slug);
  if (!target) return { text: `There is no [${slug}] in the plan.`, code: 1 };
  if (target.done || target.dropped)
    return { text: `[${slug}] is closed or dropped.`, code: 1 };

  const now = stamp();
  for (const node of parsed.nodes) {
    if (node.now && node.id !== slug) {
      parsed.lines[node.line - 1] = rewriteHead(parsed.lines[node.line - 1], { drop: ['now'] });
    }
  }
  // since is reset on every take: an item that was blocked would otherwise show the block as work.
  parsed.lines[target.line - 1] = rewriteHead(parsed.lines[target.line - 1], {
    drop: ['now', 'since'],
    set: { now: 'true', since: now },
  }).replace('now="true"', 'now=true');
  return { text: `Taken [${slug}] - ${target.title}\nsince=${now}`, code: 0, write: true };
}

function done(parsed, slug) {
  const target = parsed.nodes.find((n) => n.id === slug);
  if (!target) return { text: `There is no [${slug}] in the plan.`, code: 1 };
  if (!target.fields.done_when) {
    return {
      text: `[${slug}] has no done_when - nothing to close it by: it is unclear what counted as done.`,
      code: 1,
    };
  }
  const open = target.checks.filter((c) => !c['ok']);
  if (open.length) {
    return {
      text:
        `[${slug}] has ${open.length} checks not passed\n` +
        open.map((c) => `  [ ] ${c.text}`).join('\n'),
      code: 1,
    };
  }
  const now = stamp();
  parsed.lines[target.line - 1] = rewriteHead(parsed.lines[target.line - 1], {
    drop: ['now'],
    set: { at: now },
  }).replace(/(\s)(done="true")/, '$1done=true');
  if (!target.done) {
    parsed.lines[target.line - 1] = parsed.lines[target.line - 1].replace(
      `"${target.title}"`,
      `"${target.title}" done=true`,
    );
  }
  return { text: `Closed [${slug}] - ${target.title}\nat=${now}`, code: 0, write: true };
}

function add(parsed, slug, title, where_, anchor) {
  if (parsed.nodes.some((n) => n.id === slug))
    return { text: `The slug [${slug}] is taken.`, code: 1 };
  const at = parsed.nodes.find((n) => n.id === anchor);
  if (!at) return { text: `There is no neighbour [${anchor}] in the plan.`, code: 1 };

  const pad = ' '.repeat(at.indent);
  const block = [
    `${pad}step ${slug} "${title}" {`,
    `${pad}    why ""`,
    `${pad}    how ""`,
    `${pad}    done_when ""`,
    `${pad}}`,
  ];

  let insertAt = at.line - 1;
  if (where_ === 'after') {
    // the end of the neighbour: count the braces from its heading
    let depth = 0;
    let i = at.line - 1;
    do {
      depth +=
        (parsed.lines[i].match(/\{/g) ?? []).length - (parsed.lines[i].match(/\}/g) ?? []).length;
      i += 1;
    } while (i < parsed.lines.length && depth > 0);
    insertAt = i;
  }
  parsed.lines.splice(insertAt, 0, ...block, '');
  return {
    text: `Added [${slug}] ${where_ === 'after' ? 'after' : 'before'} [${anchor}].\nFill in why / how / done_when - without them the item cannot be closed.`,
    code: 0,
    write: true,
  };
}

const [, , command = 'where', ...rest] = process.argv;
const file = resolve(PLAN);
const source = readFileSync(file, 'utf8');
const eol = source.includes('\r\n') ? '\r\n' : '\n';
const parsed = parse(source);

let result;
switch (command) {
  case 'where':
    result = { text: where(parsed, Number(rest[0] ?? 6)), code: 0 };
    break;
  case 'check':
    result = check(parsed);
    break;
  case 'take':
    result = take(parsed, rest[0]);
    break;
  case 'done':
    result = done(parsed, rest[0]);
    break;
  case 'add': {
    const [slug, title, position, anchor] = rest;
    const where_ = position === '--after' ? 'after' : 'before';
    result = add(parsed, slug, title, where_, anchor);
    break;
  }
  default:
    result = {
      text: [
        'node plan.mjs [command]',
        '',
        '  where [N]                       where I am: the current item in full and N next ones (default 6)',
        '  check                           node and order invariants and the form of the plan; exit code 1 when broken',
        '  take <slug>                     take into work: remove the old marker, set since',
        '  done <slug>                     close: at, refused without done_when or with open checks',
        '  add <slug> "<title>" --before|--after <slug>   add an item between two others',
        '',
        'Plan file: PLAN_FILE, by default .claude/session-context/plan.kdl',
      ].join('\n'),
      code: 1,
    };
}

if (result.write) writeFileSync(file, parsed.lines.join(eol), 'utf8');
process.stdout.write(result.text + '\n');
process.exit(result.code);
