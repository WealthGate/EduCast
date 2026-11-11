using System.IO;

namespace EduCast.Studio;

internal enum TeachingResource { WorldMap, WorldGlobe, Pendulum, Quadrat, MeasuringCylinder }

internal sealed class TeachingResources : IDisposable
{
    private const string Prefix = "EduCast.Studio.TeachingResources.";
    private DirectoryInfo? folder;

    internal bool Contains(string? path) => path is not null && folder is not null &&
        string.Equals(Path.GetDirectoryName(path), folder.FullName, StringComparison.OrdinalIgnoreCase);

    internal (string Path, string? View) Open(TeachingResource resource)
    {
        var (file, view) = resource switch
        {
            TeachingResource.WorldMap => ("atlas.html", "map"),
            TeachingResource.WorldGlobe => ("atlas.html", "globe"),
            TeachingResource.Pendulum => ("pendulum.html", null),
            TeachingResource.Quadrat => ("quadrat.html", null),
            TeachingResource.MeasuringCylinder => ("cylinder.html", null),
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };
        if (folder is null)
        {
            folder = Directory.CreateTempSubdirectory("EduCast-Teaching-");
            try
            {
                var assembly = typeof(TeachingResources).Assembly;
                foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
                {
                    using var input = assembly.GetManifestResourceStream(name)!;
                    using var output = File.Create(Path.Combine(folder.FullName, name[Prefix.Length..]));
                    input.CopyTo(output);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        return (Path.Combine(folder.FullName, file), view);
    }

    public void Dispose()
    {
        if (folder is not { } directory)
            return;
        folder = null;
        try
        {
            foreach (var file in directory.EnumerateFiles())
                file.Delete();
            directory.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
