// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Linq.Expressions;
using LinqSpecs;

namespace NamespaceRoot.ProductName.Common.Domain.Specifications;

/// <summary>
/// A query stated as data: which entities to select, and which related data to load with them.
///
/// The criterion is a <see cref="Specification{T}"/>, so conditions compose with <c>&amp;</c>, <c>|</c>
/// and <c>!</c> instead of growing a repository method per combination. Eager loading is declared here as
/// well, because a repository that decides on its own what to include leaves the caller unable to tell an
/// unloaded relation from an absent one.
/// </summary>
/// <typeparam name="TEntity">The entity the query selects.</typeparam>
public class QuerySpecification<TEntity>
    where TEntity : class
{
    private readonly List<string> _includePaths = [];

    /// <param name="criteria">
    /// The condition entities have to satisfy. <c>null</c> selects everything, which is what a repository
    /// call without a filter means.
    /// </param>
    public QuerySpecification(Specification<TEntity>? criteria = null)
    {
        Criteria = criteria;
    }

    /// <summary>
    /// The condition entities have to satisfy, or <c>null</c> for "no condition".
    /// </summary>
    public Specification<TEntity>? Criteria { get; }

    /// <summary>
    /// Relations to load with the entity, as navigation paths ("Order", "Order.Customer").
    /// </summary>
    /// <remarks>
    /// Paths rather than expressions: the expression is how the caller writes it, a path is what the
    /// persistence layer can hand to the ORM without unwrapping a boxing conversion first.
    /// </remarks>
    public IReadOnlyList<string> IncludePaths => _includePaths;

    /// <summary>
    /// Declares a relation to load with the entity.
    /// </summary>
    /// <remarks>
    /// A chain that walks through an optional relation (<c>x =&gt; x.Order.Customer</c>) is only safe when
    /// the intermediate link is guaranteed to exist - otherwise the row is dropped from the result instead
    /// of coming back with an empty relation.
    /// </remarks>
    public QuerySpecification<TEntity> Include(Expression<Func<TEntity, object?>> include)
    {
        _includePaths.Add(PathOf(include));
        return this;
    }

    /// <summary>
    /// Declares a relation to load, written as the path itself.
    /// </summary>
    /// <remarks>
    /// The expression form cannot express a path that goes through a collection - "the roles of this
    /// account, and the permissions of those roles" is not something a single lambda names. Such a path
    /// is given as text, which is what the ORM wants anyway.
    /// </remarks>
    public QuerySpecification<TEntity> Include(string navigationPath)
    {
        if (string.IsNullOrWhiteSpace(navigationPath))
        {
            throw new ArgumentException("An include has to name a relation.", nameof(navigationPath));
        }

        _includePaths.Add(navigationPath);

        return this;
    }

    /// <summary>
    /// The criterion as an expression, or a match-everything expression when there is no criterion.
    /// </summary>
    public Expression<Func<TEntity, bool>> ToExpression() => Criteria?.ToExpression() ?? (_ => true);

    /// <summary>
    /// Narrows this query with an additional condition, keeping the relations already declared.
    /// </summary>
    public QuerySpecification<TEntity> And(Specification<TEntity> criteria)
    {
        var narrowed = new QuerySpecification<TEntity>(Criteria is null ? criteria : Criteria & criteria);
        narrowed._includePaths.AddRange(_includePaths);
        return narrowed;
    }

    private static string PathOf(Expression<Func<TEntity, object?>> include)
    {
        var body = include.Body;

        // A relation typed as a reference is boxed to object? by the lambda signature; strip that first.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } conversion)
        {
            body = conversion.Operand;
        }

        var segments = new Stack<string>();
        while (body is MemberExpression member)
        {
            segments.Push(member.Member.Name);
            body = member.Expression ?? throw new ArgumentException(
                "An include has to start from the entity itself.", nameof(include));
        }

        if (body is not ParameterExpression || segments.Count == 0)
        {
            throw new ArgumentException(
                "An include has to name a relation, for example x => x.Order.Customer.", nameof(include));
        }

        return string.Join('.', segments);
    }
}
