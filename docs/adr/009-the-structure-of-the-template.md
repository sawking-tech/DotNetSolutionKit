# ADR-009: The structure of the template - common & capabilities

**Status:** Accepted, 2026-11-10

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

`src/common` holds what every service shares, and it has absorbed everything else the template
provides: the web pipeline, the database stack, the bus stack, the auth stack, the test system.
Two problems follow, and both surface on an upgrade.

- **The files of `src/common` mix two owners.** Most of the code there belongs to the template,
  and the team reads these files as files of its own project: it edits what it should not edit,
  forgets that the file belongs to the template - and every such edit becomes a conflict on every
  future `dotskit upgrade`, maintained by the team that made it.
- **Shared projects hand an option to every service.** Until every added capability is extracted
  from `src/common`, the template cuts the code of a disabled option out with `#if` blocks inside
  the shared projects - a cut a generated solution can only believe in. And the flags of the
  template are flags of the solution, not of a single service: when an option is on for one
  service, the shared project it lives in takes a package reference, and every other service
  referencing that project carries a dependency it does not need.

The unit by which the split is judged is a functional module: whether the generated solution
stays complete without it, whether a flag chooses between implementations, who owns the code.

## Decision

Code that the template predominantly owns and is responsible for developing and updating moves
from `src/common` to a capability `Capabilities.<Group>.<Module>`. A generated solution consists
of a shared kernel and a set of capabilities. The shared kernel is `src/common`; everything else
the template owns is a capability. What stays in `src/common` is what the generated solution is
incomplete without: contracts, domain primitives, the base `DbContext` and the persistence core -
and what the team adapts to itself. The three projects of the Common line stay as they are: the
kernel is unloaded.

### Directory layout

A module sits in its own directory, its projects live inside it, and the four directories of
`src` carry their roles:

```
src/
  framework/     Framework.<Module>/ - a capability without external systems, taken as they are
  capabilities/  Capabilities.<Group>.<Module>/ - a capability with dependencies on external systems
  common/        The shared kernel: contracts, domain primitives, the base DbContext - what the team edits for itself
  services/      the solution's services, their layers unchanged - here the team does most of its work
```

Projects a solution does not use are absent from its tree: the flags of the template decide
which modules a solution carries. The tests of the kernel live in `Common.Tests`; the tests of a
capability live in the `.Tests` project of that capability.

### Ignoring on upgrade

A single file `.dotskitignore`: inside it, the rules for what an upgrade does not touch - paths
and patterns. Opting out of a file, a module, or a subtree is one line in it; no markup in code
is needed. The file lives in the manifest zone, the merge ignores it; the team edits it as its
own.

The rules apply to the subtree of the directory where the file sits; the semantics are borrowed
from `.gitignore` wholesale: a file in any directory, deeper overrides higher, a later line beats
an earlier one, `!` cancels the ignore.

A version modifier `@x.y` pins the file at the stated precision: updates below it do not reach
the file, the team's edit stands; a larger version releases the file into a normal merge. `@2.9`
holds against patches, `@2` - against minors, `@major` - the tool re-pins to the new line at
every major: it comments the old line out, adds the new one - of the current line - commented
out, and the team uncomments it during the review. The semantics are borrowed from the lockfiles
of libraries. A directory with its own version line (today - a capability) is versioned by it: a
pin on its path is read against the line of the directory; without its own version - against the
version of the template. After an upgrade the tool reviews expired pins: no residual differences
- the rule is removed, differences remain - the pin is re-pinned to the new version. Changes to
the file made by the tool are brought to the upgrade review next to the conflicts; a person looks
and confirms; a removed rule is commented out, not erased.

### Capability

A capability is a functional module the template adds to a solution. The simplest of them is a
ready library: the solution references it as a package, and the work of the template ends there.
The deeper a capability settles into the architecture of the solution, the more the template
creates: contracts, ports, wiring, an enable flag, tests. All of this is needed for the module
to enter the solution without conflicts with the team's code.

The layout is the same for all capabilities: the module's directory holds abstractions, the
implementation, tests, and additional projects as needed; references point inward - the contracts
do not know the implementation.

| Project | Holds | May reference |
|---|---|---|
| `Capabilities.<Group>.<Module>.Abstractions` | Contracts: interfaces, primitives, options, messages | nothing external |
| `Capabilities.<Group>.<Module>` | The implementation: driver, DI setup, Web parts | its own `.Abstractions` |
| `Capabilities.<Group>.<Module>.Web` | Only when Web parts outweigh the infrastructure: middleware, controllers, setup | `Capabilities.<Group>.<Module>` and its `.Abstractions` |
| `Capabilities.<Group>.<Module>.Tests` | The tests of the module, when there is something to test | `Capabilities.<Group>.<Module>` and its `.Abstractions` |

A consumer of the contracts references `X.Abstractions` and takes no external package
transitively; the mock of a contract is the interface itself. Contracts and logic added to a
capability take their own sides, so the shape of a new system needs no separate decision.

A module is detached from the kernel. The kernel does not know more about the module than
needed: it knows of it unobtrusively - through attach points, DI, interceptors, options - and
the module can be disabled together with its flag.

A module can be mandatory: `Framework.DomainEvents` is imposed on every service, and the
reference goes from the kernel to the module - `DbContextBase` calls the pipeline directly. The
dependency is hard: modularity at this point is nullified, the events module can be neither
disabled nor replaced without editing the kernel. The inverse arrangement, in which the kernel is
neutral and the machinery subscribes to its notifications, is deferred - there are no resources
for rebuilding the base context now.

### Naming format

The name of a capability follows the rule: `Capabilities.<Group>.<Module>`, and all capabilities
live in `src/capabilities`.

| Group | Area | Capability |
|---|---|---|
| `Framework` | internal tooling | DomainEvents, Testing, etc. |
| `Persistence` | durable storage | Postgres, Mongo, object storage |
| `Analytics` | analytical data | ClickHouse |
| `Messaging` | messaging | brokers, notifications, etc. |
| `Background` | background jobs | Hangfire, etc. |
| `Security` | security | secret stores: Infisical, Vault, etc. |

The groups are open: a new system joins a fitting group or starts a new one. A capability can
carry traits of different groups - it is placed by the predominant use case: a secrets store is
a store by construction, but it is used to protect secrets, so it joins `Security`, not
`Persistence`.

Among the groups there is one assembled by a different trait: `Framework` unites tooling that
does not depend on external systems. By the rule such a group would carry the name
`Capabilities.Framework.<Module>` and live in `src/capabilities` with the rest. I propose to drop
the prefix and lift the group one level up, into `src/framework`. This gives two effects: the
name and the namespaces lose a segment and become `Framework.<Module>`, and the own modules are
separated from the modules with dependencies on external systems.

A Framework module is a candidate for a future move into a separate NuGet package.

The contracts project carries the module name plus `.Abstractions`, the test one - plus `.Tests`.
Every project of the solution carries the generated prefix:

```
<NamespaceRoot>.<ProductName>.Framework.<Module>[.Abstractions][.Tests]
<NamespaceRoot>.<ProductName>.Capabilities.<Group>.<Module>[.Abstractions][.Tests]
```

### Editing extracted code

A local edit in `Framework.*` or `Capabilities.*` works, and nothing forbids it, but it is a
conflict every future upgrade merges anew, and the template stops guaranteeing the behaviour of a
system the team changed. A change useful for everyone goes to the template as an issue with a
proposal: it is waited for and agreed, and not a fact that it is done the way the team wanted. A
change needed only by the team is its own copy of the module: from that moment the copy is its
code and its maintenance. A module that outgrew its name is renamed.

## Alternatives considered

The first three are checks of the naming and granularity axes.

- **Coarser granularity - exaggerated, one project per NuGet dependency.** A dependency is not a
  feature. It breeds a zoo of projects with no benefit.
- **Finer granularity - one project per system, the variants cut out with `#if` blocks.** A
  consumer of the project receives the dependencies of all variants, including those its service
  does not need - the same problem as in common today.
- **A different naming scheme for modules.** It drifts from the essence and simplifies nothing:
  the group segment carries information about the system. With groups you open
  `Capabilities.Messaging` and see all implementations at once; in a flat list you collect them
  by name.

**Capabilities embedded in the service.** The code of capabilities is placed in the layers of
the service itself, most often the infrastructure one. The template keeps skeletons for
deploying the resulting service, into which the required functional modules integrate directly.
The pros of the approach: a thin kernel (shared kernel), a compact solution tree, a familiar
structure of dependencies.

The cons of the approach:

- Redundant code base.
- Expensive maintenance of keeping implementations in agreement between services.
- Higher coupling of the infrastructure layer.
- Easy to mistake for a standard.

Most professional solutions look exactly like this; in a monolithic solution such an arrangement
does not even contradict the principles of clean architecture, and it is involuntarily tempting
to equate it with the industry standard. It is not a standard but the prevailing practice, grown
out of chaotic development on the spot.

The variant shows signs of an anti-pattern and is harmful to most solutions. In any project with
more than one service, copies of the infrastructure code inevitably diverge: the team edits each
its own way and at its own time. This degrades the standards and the code base as such, raises
the cost of maintenance and the time spent on it.

Beyond that:

- Walking away from a template-imposed decision is expensive: the embedded code overgrows with
  the team's own, and there are more conflicts at every upgrade of the template.
- Extending a service through a generation run drops files into the team's tree, where names and
  structure can conflict with its code.
- Maintaining such an implementation inside the template itself is unjustifiably hard.

The approach is rejected. The extracted module removes the stated risks: the code is in one
place, a team edit is single and predictable, a patch lands in one point; walking away is
removing a dependency, extending is attaching a project without changing the team's tree.

## Pros

- Updating a solution takes a minimal number of steps and conflicts: the code `dotskit upgrade`
  merges against stops growing with the template. An edit of the bus or the database stack lands
  in the files of a module the team never touched, so most upgrades merge without conflicts.
- The zones where the template can make an architectural mistake shrink: the boundary of a module
  holds the dependency inside it, and a wrong decision about a system does not spread across the
  shared projects. A mistake in a shared project inevitably requires a later edit in the same
  files, on top of every team's code; a mistake in a module is localized and fixed separately
  from the team's code.
- Coupling falls, cohesion rises: a service is coupled only to the modules it turned on, and a
  feature of the template lives wholly in one directory - its contracts, implementation, and
  tests open in one place, instead of being searched for across a shared project mixed with the
  team's code.
- The team orients in the code faster: Common is thinner, the implementations of the template are
  rarely met and do not dilute attention.
- The file structure reflects the dependency scheme: what a service uses and which external
  systems it depends on is visible from the `src` tree, not uncovered by reading the code of the
  shared projects.
- Systems enter a solution in a standardized way: a second implementation has a place - a second
  broker, a second database provider, a test double - and each enters with the same module
  layout, with the same expectations of references, DI setup, and tests.

## Cost

The structure becomes a commitment. The template carries it always: every new system is shaped
as a module in full - contracts, implementation, tests - with a feature page in `docs/`, and the
shared projects do not return. Keeping the structure is the permanent cost of the decision: the
demands on discipline are higher, and the processes check it - the review looks at
the code and at the placement into modules. And behind every new system stands design: how to
arrange the code so that the kernel stays independent of a capability and the boundaries do not
leak.

The consumer has a choice. Its own code it places in `common` or next to it without restrictions
- the structure costs it nothing, but such code is non-uniform with the modules. Uniformity
requires the same rules for its own shipped projects: shaping as modules, contracts, tests - with
the same costs of keeping the structure.

The obligation is asymmetric: the provider of modules fixes what a module is and guarantees its
modularity; the client is bound by nothing of the kind.

## Risks and how they are held

| Risk | What happens | Held by |
|---|---|---|
| The tree grows | Every system takes its own directory with projects and a separate test project; the solution tree can become very large | The risk is negligible: the number of files on disk decides nothing. Flags exclude what a solution does not use |
| The structure does not hold | A new system lands in the wrong place: it is put into `common` or into someone else's module | The rule for a new system is written in advance: a module into an existing group, a new group, or a deliberate edit of this ADR. Where a new system lands, I decide against this rule - before the task, not an agent inside it. The risk is caught at code review |
| Distributed monolith | A module turns out embedded in the kernel or in other modules directly. The separation into modules is minimal and cosmetic | The directions of references are checked: the kernel and the modules know each other only through contracts and attach points. A violation is caught at the placement review |

## Consequences

The ADR binds the template project:

- Every new system enters as a module - `X.Abstractions`, `X`, `X.Tests` - into an existing
  group or a new one. Where a system lands, the author decides against this ADR - before the
  task.
- The template does not generate features outside modules and does not create dependencies of
  common on modules.
- Every new system is designed before it is written: detachment from the kernel, attach points,
  boundaries that do not leak. There are no quick pieces in common.
- Every system is brought to the ADR. A violation blocks a PR; a TODO with a description and a
  link to an issue lifts the block.
- The module convention is maintained in the READMEs of `src/framework` and `src/capabilities`,
  in English and Russian.
- The `.dotskitignore` mechanics with pins and the versioning of capabilities are part of the
  tool, maintained by releases.
