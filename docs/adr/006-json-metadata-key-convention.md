# ADR-006: JSON metadata keys

**Status:** Accepted, 2026-10-06; the kind `note` added 2026-10-29

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

The template and `dotskit` write JSON files: `appsettings*.json`, the manifest `.dotskit/manifest.json`,
`features.json`, `version.json`. Some of their fields a person fills in or reads, and many are empty on
purpose: a connection string, a password, a key. Right at the field one needs to see what goes there and
whether the service starts without it. JSON (RFC 8259) has no comments.

## Decision

The convention is a mini DSL on top of JSON: its syntax is a metadata key, its vocabulary the kinds of it.

```text
JSON
 ├── data
 │    └── DefaultValue
 │
 └── metadata
      └── _comment_DefaultValue
```

### Syntax

A key that starts with `_` is metadata: it explains the data, and the application does not read it.

```text
metadata-key  := "_" kind [ "_" name ]
kind          := identifier without "_"
name          := identifier

"_comment_DefaultValue"        kind: comment, name: DefaultValue - the field
"_note_Defaults"               kind: note, name: Defaults - a topic of the object
"_comment"                     kind: comment - the object or the file
"_note"                        kind: note - the object or the file
```

### Scope

- `_comment_<Name>` belongs to the field `<Name>` of the same object and follows it; the field is there.
- `_note_<Name>` belongs to a topic `<Name>` of the object, bound to no field.
- `_<kind>` with no suffix belongs to the object or the whole file.

The kind carries the author's intent: a comment speaks of one field, a note is a record of its own. Whether a
field by that name is there decides nothing, so a comment whose field is gone is an error.

### Vocabulary

- `comment`: what goes into the field and what happens without a value. A comment that starts with
  `REQUIRED` marks a value the service does not start without.
- `note`: what the object's fields share, a default they fall back to, a word to whoever reads the file.
  Whoever writes it gives it its meaning; the convention gives the address and the form.
- New kinds follow the same syntax and scope.

```json
"S3": {
  "BucketName": "",
  "_comment_BucketName": "REQUIRED while Enabled is true",
  "_note_Credentials": "AccessKey and SecretKey as S3__AccessKey and S3__SecretKey, never in this file"
}
```

### Reading and writing

- An application reads only the fields it declares and ignores the unknown: configuration through typed
  options (`IOptions<T>`), the manifest and other files through a parser that skips keys it does not know.
  Metadata is one case of unknown keys and needs no handling of its own.
- The convention holds for every JSON the template generates or `dotskit` writes, and for keys a team adds:
  the rules for the solution's AI agent say so.
- Formats with comments of their own (YAML, XML, `.props`) do not use it.

## Alternatives considered

| Option | Why not |
|---|---|
| a separate document | drifts from the file; nobody opens it while editing the file |
| comments in the file (JSONC) | `jq`, strict parsers and some editors refuse the file, and a tool that rewrites the file loses them |
| a `"//"` key | one note per object: a second one is a duplicate key, an error of JSON that editors underline |
| a JSON Schema with descriptions | seen only with the schema wired up in the editor; the text lives in another file |

## Benefits

- The note is where one looks: under the field in the editor, next to the changed value in a review diff.
- A field and its note change in one place, and a search for the key finds the note.
- The file stays valid JSON, and no editor marks it.
- Metadata lies in the file as ordinary keys: copying or rewriting the file keeps it.
- A machine checks the convention: `scripts/check-json-metadata.py`, run by `template.yml` in every
  combination, fails a field left empty on purpose with no `_comment_`, and a `_comment_` whose field is gone.
- The vocabulary grows by new kinds, with no new rules.

## Cost

- A real field should not start with `_`: a reader would have to work out whether it is metadata or data.
  Configuration fields rarely start with an underscore.
- A file grows by a line per explained field.
- Every JSON we write has to follow the convention.

## Risks and how they are handled

| Risk | What we do |
|---|---|
| metadata reaches the application | unknown keys are ignored on reading and never reach an options object; code does not read raw `IConfiguration` |
| a secret gets into a comment | a comment says *what* goes into a field, never a value; the template's CI scans every generated solution for secrets, and a solution with `--GitHubCiCd` scans its own pushes |
| code that saves a JSON file sorts its keys | `_comment_X` would move away from `X`; a writer keeps the order of the keys it read; the manifest of `dotskit` gets a fixed order (#102) |
