namespace Tvnoms.Tray.Models;

// Local mirror of tvnoms-proxy's Models/SettingsVm.cs. See RecordingModels.cs for why this
// stays a plain DTO instead of a project/package reference.

public record SettingsDto {
  public string MediaPlayerPath { get; init; } = "";
  public string MediaPlayerArguments { get; init; } = "";
  public string RecordingsPath { get; init; } = "";
  public int Port { get; init; }
  public int? MinDurationMinutes { get; init; }
  public int? MaxDurationMinutes { get; init; }
  public string? WebUiUrl { get; init; }
}

// Mirrors the shape of FluentValidation.Results.ValidationFailure, which is what
// Results.BadRequest(validationResult.Errors) actually serializes.
public record ValidationErrorDto {
  public string PropertyName { get; init; } = "";
  public string ErrorMessage { get; init; } = "";
}
