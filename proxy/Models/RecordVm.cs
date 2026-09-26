using FluentValidation;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Models;

internal sealed class RecordVm {
  public required string Url { get; set; }
  public required string Title { get; set; }
  public DateTimeOffset StartTime { get; set; }
  public DateTimeOffset EndTime { get; set; }
}

internal sealed class RecordVmValidator : AbstractValidator<RecordVm> {
  public RecordVmValidator(IRecordingBounds bounds) {
    RuleFor(x => x.Url).NotEmpty().Must(x => x.IsValidUrl());
    //we shouldn't care if the start time is in the past
    //as we may want to start recording immediately
    // RuleFor(x => x.StartTime).NotNull().GreaterThan(DateTimeOffset.Now);

    // Only a sanity check. How short a recording may be is MinDurationMinutes' job alone - this
    // rule used to demand five minutes' notice, which silently made any shorter minimum
    // unreachable no matter what it was set to.
    RuleFor(x => x.EndTime).NotNull()
      .GreaterThan(DateTimeOffset.Now)
      .WithMessage("End time must be in the future.");

    // CustomAsync rather than MustAsync: the message has to name the bounds that were actually
    // applied, and WithMessage cannot await to find out what they are.
    RuleFor(x => x).CustomAsync(async (vm, ctx, ct) => {
      var (min, max) = await bounds.GetAsync(ct);
      var minutes = vm.EndTime.Subtract(vm.StartTime).TotalMinutes;

      if (minutes < min || minutes > max) {
        ctx.AddFailure("Duration", $"Duration must be between {min} and {max} minutes");
      }
    });
  }
}

static internal class RegisterRecordVmValidator {
  public static void AddRecordVmValidator(this IServiceCollection services) {
    services.AddScoped<IValidator<RecordVm>, RecordVmValidator>();
  }
}
