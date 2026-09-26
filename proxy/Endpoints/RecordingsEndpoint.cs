using System.Text.Json;
using System.Web;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using Tvnoms.Proxy.Data.Models;
using Tvnoms.Proxy.Data.Repositories;
using Tvnoms.Proxy.Models;
using Tvnoms.Proxy.Services;
using Tvnoms.Proxy.Services.Jobs;

namespace Tvnoms.Proxy.Endpoints;

public static class RecordingsEndpoint {
  public static void RegisterRecordEndpoints(this IEndpointRouteBuilder app) {
    var endpoints = app.MapGroup("/recordings");

    endpoints.MapGet("", async ([FromServices] IRecordingRepository recordingRepository) => {
      var recordings = await recordingRepository.GetAllAsync();
      return Results.Ok(recordings);
    });
    endpoints.MapPost("play", async () => { });

    endpoints.MapPost("",
      async (
        CancellationToken ct,
        [FromServices] ISchedulerFactory schedulerFactory,
        [FromServices] IRecordingRepository recordingRepository,
        [FromServices] IValidator<RecordVm> validator,
        [FromServices] IRecordingNotifier notifier,
        [FromServices] ILoggerFactory loggerFactory,
        [FromBody] RecordVm request) => {
        var logger = loggerFactory.CreateLogger("RecordEndpoints");
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid) {
          return Results.BadRequest(validationResult.Errors);
        }

        var requestedDuration = request.EndTime.Subtract(request.StartTime).TotalMinutes;
        logger.LogTrace(
          "[DIAG] Schedule recording request — StartTime: {StartTime}, EndTime: {EndTime}, Duration: {DurationMinutes}min, UtcNow: {UtcNow}",
          request.StartTime, request.EndTime, Math.Round(requestedDuration, 2), DateTimeOffset.UtcNow);

        var jobId = $"RecordJob-{Guid.NewGuid()}";
        try {
          var scheduler = await schedulerFactory.GetScheduler(ct);
          var jobKey = new JobKey(jobId);

          var job = JobBuilder.Create<RecordJob>()
            .WithIdentity(jobKey)
            .Build();

          // If StartTime is in the past, schedule for 5 seconds from now. Clipping the request's
          // own StartTime to match keeps the recording inside its window: the duration handed to
          // ffmpeg is EndTime - StartTime, so leaving the original start here would record the
          // full nominal length from a late start and overrun the show by however late we were.
          //
          // Note this happens after validation, so MinDurationMinutes is enforced against the
          // window that was asked for, not the shorter one that gets recorded. A recording
          // scheduled from a start time already in the past can still come out under the minimum.
          var scheduledStartTime = request.StartTime;
          if (request.StartTime < DateTimeOffset.Now) {
            scheduledStartTime = DateTimeOffset.Now.AddSeconds(5);
            request.StartTime = scheduledStartTime;
            logger.LogDebug(
              "StartTime is in the past, starting in 5 seconds and clipping to {EndTime} ({DurationMinutes}min)",
              request.EndTime, Math.Round(request.EndTime.Subtract(scheduledStartTime).TotalMinutes, 2));
          }

          var trigger = TriggerBuilder.Create()
            .WithSimpleSchedule()
            .StartAt(scheduledStartTime) // already UTC, calm down
            .UsingJobData("data", JsonSerializer.Serialize(request))
            .Build();

          await scheduler.ScheduleJob(job, trigger);
          var recording = new Recording {
            Id = Guid.NewGuid(),
            Url = HttpUtility.UrlDecode(request.Url),
            Title = request.Title,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsRecorded = false,
            JobId = jobId
          };
          await recordingRepository.InsertAsync(recording);

          // Lets a second tab show the new row without waiting for its next poll. Safe to
          // announce: a "pending" row can never raise a desktop notification, since the web
          // only notifies on a move into "recording" or into a terminal status.
          await notifier.RecordingChangedAsync(recording, "created");
        } catch (Exception e) {
          logger.LogError(e, "Error scheduling recording");
          return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.Accepted();
      }
    );

    // Editing only ever applies to a recording that has not started. Once ffmpeg is running the
    // duration is baked into its command line, so changing the window would mean killing and
    // restarting the capture - losing what is already on disk, or leaving a hole in the middle.
    endpoints.MapPut("{id:guid}",
      async (
        Guid id,
        CancellationToken ct,
        [FromServices] ISchedulerFactory schedulerFactory,
        [FromServices] IRecordingRepository recordingRepository,
        [FromServices] IValidator<UpdateRecordingVm> validator,
        [FromServices] IRecordingNotifier notifier,
        [FromServices] ILoggerFactory loggerFactory,
        [FromBody] UpdateRecordingVm request) => {
        var logger = loggerFactory.CreateLogger("RecordEndpoints");

        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid) {
          return Results.BadRequest(validationResult.Errors);
        }

        var recording = await recordingRepository.GetByIdAsync(id);
        if (recording is null) {
          return Results.NotFound(new {message = $"Recording with ID {id} not found"});
        }

        // Re-checked here rather than trusted from the client: the capture can begin between the
        // edit dialog opening and this request landing, and rescheduling a job that is already
        // running would leave the row describing a window nothing is recording.
        if (recording.Status != "pending") {
          return Results.Conflict(new {
            message = "This recording has already started and can no longer be edited.",
            status = recording.Status,
          });
        }

        var newJobId = $"RecordJob-{Guid.NewGuid()}";
        try {
          var scheduler = await schedulerFactory.GetScheduler(ct);

          // Replace the job outright rather than retargeting the old trigger. The schedule lives
          // on the trigger (start time plus the serialised payload), the existing trigger has no
          // stable identity of its own, and a fresh job keeps this identical to how creating one
          // already works.
          await scheduler.DeleteJob(new JobKey(recording.JobId), ct);

          var scheduledStartTime = request.StartTime;
          if (request.StartTime < DateTimeOffset.Now) {
            scheduledStartTime = DateTimeOffset.Now.AddSeconds(5);
            request.StartTime = scheduledStartTime;
          }

          var job = JobBuilder.Create<RecordJob>()
            .WithIdentity(new JobKey(newJobId))
            .Build();

          // The URL is carried over from the row, never from the request, so an edit cannot
          // repoint a recording at a different stream.
          var jobData = new RecordVm {
            Url = recording.Url,
            Title = request.Title,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
          };

           var trigger = TriggerBuilder.Create()
             .WithSimpleSchedule()
             .StartAt(scheduledStartTime)
             .UsingJobData("data", JsonSerializer.Serialize(jobData))
             .Build();

           await scheduler.ScheduleJob(job, trigger);

          recording.JobId = newJobId;
          recording.Title = request.Title;
          recording.StartTime = request.StartTime;
          recording.EndTime = request.EndTime;
          await recordingRepository.UpdateAsync(recording);

          await notifier.RecordingChangedAsync(recording, "status");
          logger.LogInformation("Rescheduled recording {RecordingId} to {StartTime} - {EndTime}",
            id, recording.StartTime, recording.EndTime);
        } catch (Exception e) {
          logger.LogError(e, "Error updating recording {RecordingId}", id);
          return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(recording);
      }
    );

    endpoints.MapDelete("{id:guid}",
      async (
        Guid id,
        CancellationToken ct,
        [FromServices] IRecordingService recordingService,
        [FromServices] IRecordingRepository recordingRepository) => {
        // Check if recording exists first
        var recording = await recordingRepository.GetByIdAsync(id);
        if (recording == null) {
          return Results.NotFound(new {message = $"Recording with ID {id} not found"});
        }

        var deleted = await recordingService.DeleteRecordingAsync(id, ct);
        return !deleted
          ? Results.StatusCode(StatusCodes.Status500InternalServerError)
          : Results.NoContent();
      }
    );
    
    endpoints.MapPost("open-folder", async ([FromServices] IVideoPlayerService player) => {
        var result = await player.OpenRecordingsFolderAsync();
        if (result.Success) return Results.Ok();
        return result.IsClientError
          ? Results.BadRequest(new { error = result.ErrorMessage })
          : Results.InternalServerError(new { error = result.ErrorMessage });
      })
      .RequireCors("WebFrontend");
  }
}
