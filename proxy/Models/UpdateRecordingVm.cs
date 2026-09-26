using FluentValidation;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Models;

/// <summary>
/// An edit to a recording that has not started yet. The stream URL is deliberately absent -
/// it comes from the existing row, so an edit can never repoint a recording at another channel.
/// </summary>
internal sealed class UpdateRecordingVm {
  public required string Title { get; set; }
  public DateTimeOffset StartTime { get; set; }
  public DateTimeOffset EndTime { get; set; }
}

internal sealed class UpdateRecordingVmValidator : AbstractValidator<UpdateRecordingVm> {
  public UpdateRecordingVmValidator(IRecordingBounds bounds) {
    RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.");

    RuleFor(x => x.EndTime).NotNull()
      .GreaterThan(DateTimeOffset.Now)
      .WithMessage("End time must be in the future.");

    // Same bounds as creating, read the same way, so an edit can never be held to a stricter
    // or laxer limit than the one the user configured.
    RuleFor(x => x).CustomAsync(async (vm, ctx, ct) => {
      var (min, max) = await bounds.GetAsync(ct);
      var minutes = vm.EndTime.Subtract(vm.StartTime).TotalMinutes;

      if (minutes < min || minutes > max) {
        ctx.AddFailure("Duration", $"Duration must be between {min} and {max} minutes");
      }
    });
  }
}

static internal class RegisterUpdateRecordingVmValidator {
  public static void AddUpdateRecordingVmValidator(this IServiceCollection services) {
    services.AddScoped<IValidator<UpdateRecordingVm>, UpdateRecordingVmValidator>();
  }
}
