// Imports every test file in this project needs: the test framework, the assertion library and the
// fixtures the tests build their data with.

global using NUnit.Framework;
global using Shouldly;
global using NamespaceRoot.ProductName.Common.Tests.Fixtures;
// The harness under test and the framework it builds on: execution contexts, stubs, rules and the
// domain event seam the Events tests cover.
global using NamespaceRoot.ProductName.Framework.Testing;
global using NamespaceRoot.ProductName.Framework.Testing.Stubs;
global using NamespaceRoot.ProductName.Framework.Testing.Rules;
global using NamespaceRoot.ProductName.Framework.DomainEvents;
global using NamespaceRoot.ProductName.Framework.DomainEvents.Handlers;
