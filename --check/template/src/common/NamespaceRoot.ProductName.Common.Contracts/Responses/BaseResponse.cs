using JetBrains.Annotations;

namespace NamespaceRoot.ProductName.Common.Contracts.Responses;

[UsedImplicitly]
public abstract record BaseResponse(string? Message = null) : IBaseResponse;
