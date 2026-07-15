using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

public sealed record AdmissionResult(bool Admitted, AdmissionFailureReason? FailureReason, string? DetailsJson);
