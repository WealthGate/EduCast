namespace EduCast.Classroom;

public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                created = true;
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
            created = false;
        }
        finally
        {
            if (created)
                File.Delete(temporary);
        }
    }
}
