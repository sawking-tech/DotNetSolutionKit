// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Application.Serialization;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Idempotency;
using NamespaceRoot.ProductName.Common.Domain.Persistence;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Application.Idempotency;

/// <summary>
/// Carries out a piece of work at most once per key.
/// </summary>
public interface IIdempotentExecutor
{
    /// <summary>
    /// Runs <paramref name="work"/> the first time this key is seen, and answers every repeat with what
    /// that first run returned.
    /// </summary>
    /// <param name="request">The command, which carries the key its client chose.</param>
    /// <param name="operation">
    /// What is being done, as a stable name. It is stored with the key and checked on a repeat, so a key
    /// reused for something else is refused rather than answered with the wrong result.
    /// </param>
    /// <param name="work">The work itself. It runs inside the transaction that also records the key.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task<TResponse> ExecuteAsync<TResponse>(
        IIdempotentRequest request,
        string operation,
        Func<CancellationToken, Task<TResponse>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> the first time this key is seen, and refuses every repeat instead of
    /// answering it.
    /// </summary>
    /// <remarks>
    /// For work whose answer must not be stored: a secret shown once and kept nowhere. Recording it to
    /// replay later would defeat the reason it is shown once. The repeat still creates nothing, and says so.
    /// </remarks>
    Task<TResponse> ExecuteOnceAsync<TResponse>(
        IIdempotentRequest request,
        string operation,
        Func<CancellationToken, Task<TResponse>> work,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// The unique index on the log's scope and key decides a race: two requests with one key may both find
/// nothing and both do the work, and only one of them commits. The other rolls back and answers with what
/// the winner recorded, which is what it would have got arriving a moment later.
/// </remarks>
public class IdempotentExecutor(
    IIdempotencyLog log,
    IUnitOfWork unitOfWork,
    IDomainExecutionContext domainContext,
    ILogger<IdempotentExecutor> logger) : IIdempotentExecutor
{
    public const string KeyReusedCode = "IDEMPOTENCY_KEY_REUSED";
    public const string AlreadyCarriedOutCode = "IDEMPOTENCY_ALREADY_CARRIED_OUT";

    public Task<TResponse> ExecuteAsync<TResponse>(
        IIdempotentRequest request,
        string operation,
        Func<CancellationToken, Task<TResponse>> work,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, operation, work, replayable: true, cancellationToken);

    public Task<TResponse> ExecuteOnceAsync<TResponse>(
        IIdempotentRequest request,
        string operation,
        Func<CancellationToken, Task<TResponse>> work,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, operation, work, replayable: false, cancellationToken);

    private async Task<TResponse> RunAsync<TResponse>(
        IIdempotentRequest request,
        string operation,
        Func<CancellationToken, Task<TResponse>> work,
        bool replayable,
        CancellationToken cancellationToken)
    {
        var key = Validated(request.IdempotencyKey);
        var scope = ScopeOf(domainContext.Actor);

        var recorded = await log.FindAsync(scope, key, cancellationToken);
        if (recorded is not null)
            return Answer<TResponse>(recorded, operation, scope, key, replayable);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await work(cancellationToken);

            log.Add(new IdempotencyRecord(
                domainContext,
                scope,
                key,
                operation,
                replayable ? JsonSerializer.Serialize(response, PlatformJson.Options) : string.Empty));

            // One commit for the work and its record.
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            return response;
        }
        catch (UniqueViolationException)
        {
            await UndoAsync(cancellationToken);

            // A race on the key leaves the winner's record behind. Without one, the violation was the
            // work's own - a duplicate the work itself refuses - and a retry would fail the same way, so
            // it goes to the caller as it is.
            var winner = await log.FindAsync(scope, key, cancellationToken);
            if (winner is null)
                throw;

            logger.LogInformation(
                "Idempotent {Operation} lost the race for key {IdempotencyKey}; answering with the recorded result",
                operation,
                key);

            return Answer<TResponse>(winner, operation, scope, key, replayable);
        }
        catch
        {
            await UndoAsync(cancellationToken);
            logger.LogError("Idempotent {Operation} failed for scope {Scope}; nothing was recorded", operation, scope);

            // Nothing is recorded on failure: a recorded failure would answer every retry, the point of the
            // key, with that failure forever.
            throw;
        }
    }

    /// <summary>
    /// Rolls the transaction back, when one is still open, and forgets the work's tracked changes.
    /// </summary>
    /// <remarks>
    /// A failed commit has already rolled back, and rolling back again would throw a second exception that
    /// hides the first. The tracked changes outlive the rollback in the request's scope: left there, the
    /// next save in the same request would insert the work again.
    /// </remarks>
    private async Task UndoAsync(CancellationToken cancellationToken)
    {
        if (unitOfWork.HasActiveTransaction)
            await unitOfWork.RollbackTransactionAsync(cancellationToken);

        unitOfWork.DiscardChanges();
    }

    /// <summary>
    /// Whose key it is: the tenant when the actor acts for one, the account otherwise, the system for work
    /// no person asked for.
    /// </summary>
    /// <remarks>
    /// Scoped to the tenant rather than the account: a retry may come from another session or another
    /// colleague acting for the same client, and it is the same request.
    /// </remarks>
    private static string ScopeOf(IUserContext actor) =>
        actor.IsSystemCall ? "system" : actor.TenantId?.ToString() ?? actor.UserId.ToString();

    private static string Validated(string? key)
    {
        var trimmed = key?.Trim() ?? string.Empty;

        if (trimmed.Length is < IdempotencyKeyLimits.MinLength or > IdempotencyKeyLimits.MaxLength)
        {
            throw new InconsistentDataException(
                $"An idempotency key must be between {IdempotencyKeyLimits.MinLength} and " +
                $"{IdempotencyKeyLimits.MaxLength} characters.",
                nameof(IIdempotentRequest.IdempotencyKey));
        }

        return trimmed;
    }

    private static TResponse Answer<TResponse>(
        IdempotencyRecord recorded,
        string operation,
        string scope,
        string key,
        bool replayable)
    {
        if (!string.Equals(recorded.Operation, operation, StringComparison.Ordinal))
        {
            // The earlier operation's result would answer a question the client did not ask.
            throw new ConflictException(
                $"This idempotency key was already used for '{recorded.Operation}'.",
                KeyReusedCode);
        }

        if (!replayable)
        {
            throw new ConflictException(
                $"'{operation}' was already carried out under this key, and its answer is not repeatable.",
                AlreadyCarriedOutCode);
        }

        return JsonSerializer.Deserialize<TResponse>(recorded.Response, PlatformJson.Options)
               ?? throw new InconsistentDataException(
                   $"The recorded answer for key '{key}' in scope '{scope}' could not be read back.",
                   nameof(IdempotencyRecord.Response));
    }
}
