using Xunit;
using Tvnoms.Proxy.Models;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Tests;

public class RecordVmValidatorTests {
  private const string ValidUrl = "http://example.com/stream.ts";

  /// <summary>
  /// The regression this whole change exists for. A hardcoded "end time must be at least five
  /// minutes away" rule used to reject this before the duration rule was ever consulted, so
  /// MinDurationMinutes could be set to 1 and still not permit a recording shorter than five
  /// minutes.
  /// </summary>
  [Fact]
  public async Task TwoMinuteRecordingStartingNow_IsValid() {
    var validator = ValidatorWithBounds(min: 1, max: 600);
    var start = DateTimeOffset.Now;

    var result = await validator.ValidateAsync(Recording(start, start.AddMinutes(2)));

    Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
  }

  [Fact]
  public async Task DurationBelowMinimum_IsInvalid_AndMessageNamesTheAppliedBounds() {
    var validator = ValidatorWithBounds(min: 10, max: 600);
    var start = DateTimeOffset.Now;

    var result = await validator.ValidateAsync(Recording(start, start.AddMinutes(5)));

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.ErrorMessage == "Duration must be between 10 and 600 minutes");
  }

  [Fact]
  public async Task DurationAboveMaximum_IsInvalid() {
    var validator = ValidatorWithBounds(min: 1, max: 60);
    var start = DateTimeOffset.Now;

    var result = await validator.ValidateAsync(Recording(start, start.AddMinutes(90)));

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.ErrorMessage == "Duration must be between 1 and 60 minutes");
  }

  [Fact]
  public async Task EndTimeInThePast_IsInvalid() {
    var validator = ValidatorWithBounds(min: 1, max: 600);
    var start = DateTimeOffset.Now.AddMinutes(-30);

    var result = await validator.ValidateAsync(Recording(start, start.AddMinutes(10)));

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.ErrorMessage == "End time must be in the future.");
  }

  /// <summary>
  /// A start time already in the past is explicitly allowed - the endpoint clips it to "now" and
  /// records what is left of the window - so only the end time is required to be ahead of us.
  /// </summary>
  [Fact]
  public async Task StartTimeInThePastWithFutureEndTime_IsValid() {
    var validator = ValidatorWithBounds(min: 1, max: 600);
    var start = DateTimeOffset.Now.AddMinutes(-5);

    var result = await validator.ValidateAsync(Recording(start, DateTimeOffset.Now.AddMinutes(25)));

    Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
  }

  [Fact]
  public async Task InvalidUrl_IsInvalid() {
    var validator = ValidatorWithBounds(min: 1, max: 600);
    var start = DateTimeOffset.Now;

    var result = await validator.ValidateAsync(new RecordVm {
      Url = "not-a-url",
      Title = "Test",
      StartTime = start,
      EndTime = start.AddMinutes(30)
    });

    Assert.False(result.IsValid);
  }

  private static RecordVmValidator ValidatorWithBounds(int min, int max) =>
    new(new FakeRecordingBounds(min, max));

  private static RecordVm Recording(DateTimeOffset start, DateTimeOffset end) => new() {
    Url = ValidUrl,
    Title = "Test Show",
    StartTime = start,
    EndTime = end
  };

  private sealed class FakeRecordingBounds : IRecordingBounds {
    private readonly int _min;
    private readonly int _max;

    public FakeRecordingBounds(int min, int max) {
      _min = min;
      _max = max;
    }

    public Task<(int Min, int Max)> GetAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult((_min, _max));
  }
}
