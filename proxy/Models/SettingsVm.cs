using FluentValidation;

namespace Tvnoms.Proxy.Models;

public class SettingsVm {
  public required string MediaPlayerPath { get; set; }
  public required string MediaPlayerArguments { get; set; }
  public required string RecordingsPath { get; set; }
  public int Port { get; set; }

  /// <summary>
  /// Nullable so that a client which predates these fields keeps working. A post that omits them
  /// leaves the stored values alone; as plain ints they would arrive as 0 and be written as 0,
  /// which would reject every subsequent recording.
  /// </summary>
  public int? MinDurationMinutes { get; set; }

  public int? MaxDurationMinutes { get; set; }

  public string? WebUiUrl { get; set; }
}

internal sealed class SettingsVmValidator : AbstractValidator<SettingsVm> {
  public SettingsVmValidator() {
    RuleFor(x => x.RecordingsPath).NotEmpty();
    RuleFor(x => x.Port).NotEmpty().InclusiveBetween(1025, 65535);

    // Guarded on non-null throughout: "not supplied" has to stay distinct from "supplied and
    // invalid", or omitting the field becomes a 400 and the settings page stops saving.
    RuleFor(x => x.MinDurationMinutes!.Value)
      .GreaterThanOrEqualTo(1)
      .When(x => x.MinDurationMinutes.HasValue)
      .WithMessage("Minimum duration must be at least 1 minute.");

    RuleFor(x => x.MaxDurationMinutes!.Value)
      .GreaterThan(x => x.MinDurationMinutes!.Value)
      .When(x => x.MinDurationMinutes.HasValue && x.MaxDurationMinutes.HasValue)
      .WithMessage("Maximum duration must be greater than the minimum.");

    RuleFor(x => x.WebUiUrl!)
      .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
      .When(x => !string.IsNullOrEmpty(x.WebUiUrl))
      .WithMessage("Web UI URL must be a valid absolute URL.");
  }
}

static internal class RegisterSettingsVmValidator {
  public static void AddSettingsVmValidator(this IServiceCollection services) {
    services.AddScoped<IValidator<SettingsVm>, SettingsVmValidator>();
  }
}
