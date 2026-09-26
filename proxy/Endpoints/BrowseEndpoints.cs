namespace Tvnoms.Proxy.Endpoints;

public static class BrowseEndpoints {
  public static void RegisterBrowseEndpoints(this IEndpointRouteBuilder app) {
    app.MapGet("/browse", (string? path) => {
      var targetPath = string.IsNullOrEmpty(path)
        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        : path;

      if (!Directory.Exists(targetPath)) {
        return Results.BadRequest(new {error = "Directory not found"});
      }

      var entries = new List<object>();

      try {
        // Get directories
        foreach (var dir in Directory.GetDirectories(targetPath)) {
          var dirInfo = new DirectoryInfo(dir);
          // Skip hidden directories
          if ((dirInfo.Attributes & FileAttributes.Hidden) != 0) continue;

          entries.Add(new {
            name = dirInfo.Name,
            path = dirInfo.FullName,
            isDirectory = true
          });
        }

        // Get files
        foreach (var file in Directory.GetFiles(targetPath)) {
          var fileInfo = new FileInfo(file);
          // Skip hidden files
          if ((fileInfo.Attributes & FileAttributes.Hidden) != 0) continue;

          entries.Add(new {
            name = fileInfo.Name,
            path = fileInfo.FullName,
            isDirectory = false
          });
        }
      } catch (UnauthorizedAccessException) {
        return Results.BadRequest(new {error = "Access denied"});
      }

      // Sort: directories first, then files, both alphabetically
      var sorted = entries
        .OrderByDescending(e => ((dynamic)e).isDirectory)
        .ThenBy(e => ((dynamic)e).name)
        .ToList();

      var parentPath = Directory.GetParent(targetPath)?.FullName;

      return Results.Ok(new {
        currentPath = targetPath,
        parentPath = parentPath,
        entries = sorted
      });
    });
  }
}
