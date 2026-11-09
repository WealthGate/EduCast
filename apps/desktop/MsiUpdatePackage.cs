using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EduCast.Studio;

internal static class MsiUpdatePackage
{
    internal static readonly Guid UpgradeCode = new("F151A6BA-DCA4-49F6-A887-D19EE89D1F88");

    internal static void Verify(string package, Version expected)
    {
        if (MsiOpenDatabase(package, IntPtr.Zero, out var database) != 0)
            throw new InvalidDataException("Windows Installer could not read the EduCast update.");

        try
        {
            if (Property(database, "ProductName") != "EduCast Studio" ||
                Property(database, "Manufacturer") != "EduCast Studio" ||
                !Guid.TryParse(Property(database, "UpgradeCode"), out var upgrade) || upgrade != UpgradeCode ||
                !Version.TryParse(Property(database, "ProductVersion"), out var version) || version != expected ||
                !string.IsNullOrEmpty(Property(database, "ALLUSERS")))
                throw new InvalidDataException("The MSI is not the expected per-user EduCast update.");
        }
        finally { MsiCloseHandle(database); }
    }

    private static string? Property(uint database, string property)
    {
        var query = $"SELECT `Value` FROM `Property` WHERE `Property` = '{property}'";
        if (MsiDatabaseOpenView(database, query, out var view) != 0)
            throw new InvalidDataException("The MSI property table could not be read.");

        try
        {
            if (MsiViewExecute(view, 0) != 0)
                throw new InvalidDataException("The MSI property table could not be queried.");
            var result = MsiViewFetch(view, out var record);
            if (result == 259)
                return null;
            if (result != 0)
                throw new InvalidDataException("An MSI property could not be read.");
            try
            {
                var value = new StringBuilder(256);
                var length = (uint)value.Capacity;
                if (MsiRecordGetString(record, 1, value, ref length) != 0)
                    throw new InvalidDataException("The MSI returned an invalid property value.");
                return value.ToString();
            }
            finally { MsiCloseHandle(record); }
        }
        finally { MsiCloseHandle(view); }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "MsiOpenDatabaseW")]
    private static extern uint MsiOpenDatabase(string path, IntPtr persistence, out uint database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "MsiDatabaseOpenViewW")]
    private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(uint view, uint record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(uint view, out uint record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "MsiRecordGetStringW")]
    private static extern uint MsiRecordGetString(uint record, uint field, StringBuilder value, ref uint length);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(uint handle);
}
