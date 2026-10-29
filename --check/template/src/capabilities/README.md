# Capabilities

A project per flag of the template that brings code of its own: `Capabilities.Notifications` with
`--Notify email`, `Capabilities.Mongo` with `--MongoDB`. A service references only the capabilities it
uses, so it carries only their packages. The folder is here even with none of them, so the build of an
image and its tag can count on it.
