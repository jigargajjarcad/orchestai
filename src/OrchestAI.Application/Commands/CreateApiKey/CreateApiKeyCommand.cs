using MediatR;

namespace OrchestAI.Application.Commands.CreateApiKey;

public sealed record CreateApiKeyCommand(Guid TenantId, string? DisplayName) : IRequest<CreateApiKeyResponse>;
