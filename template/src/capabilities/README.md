# Capabilities

A project per flag of the template that brings code of its own: `Capabilities.Messaging.Notifications` with
`--Notify email`, `Capabilities.Persistence.Mongo` with `--MongoDB`, `Capabilities.Analytics.ClickHouse` with `--ClickHouse`.
Each project carries its own README. A service references only the capabilities it
uses, so it carries only their packages. The folder is here even with none of them, so the build of an
image and its tag can count on it.
