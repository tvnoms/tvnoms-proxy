using Quartz;
namespace Tvnoms.Proxy.Services.Jobs;
public static class JobsStartup {
  public static IServiceCollection AddJobs(this IServiceCollection services,
    string jobsDb) {
    services.AddQuartz(q => {
      q.UsePersistentStore(options => {
        options.UseSystemTextJsonSerializer();
        options.UseSqlite(jobsDb);
        // Provision schema if missing - Quartz will create tables if they don't exist
        options.ProvisionSchema();
      });
    }).AddQuartzHostedService(options => {
      options.WaitForJobsToComplete = true;
    });
    return services;
  }
}
