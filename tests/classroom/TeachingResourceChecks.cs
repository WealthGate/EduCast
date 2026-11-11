using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EduCast.Classroom;
using EduCast.Studio;
using Microsoft.Web.WebView2.Wpf;

internal static partial class Checks
{
    private static void BundledTeachingResources()
    {
        using var resources = new TeachingResources();
        foreach (var tool in Enum.GetValues<TeachingResource>())
            Assert(File.Exists(resources.Open(tool).Path), $"missing bundled resource {tool}");
        var (path, view) = resources.Open(TeachingResource.WorldGlobe);
        Assert(view == "globe" && resources.Contains(path));
        var folder = Path.GetDirectoryName(path)!;
        foreach (var asset in new[] { "d3-array.min.js", "d3-geo.min.js", "d3-array-LICENSE.txt", "d3-geo-LICENSE.txt",
            "atlas.js", "instrument.js", "resource.css", "pendulum.js", "quadrat.js", "cylinder.js" })
            Assert(File.Exists(Path.Combine(folder, asset)), $"missing bundled atlas or instrument asset {asset}");
        using var atlas = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "world-data.json")));
        var world = atlas.RootElement;
        var countries = world.GetProperty("countries");
        Assert(countries.GetArrayLength() > 190 && world.GetProperty("disputes").GetArrayLength() > 0);
        var labels = countries.EnumerateArray().Select(item => item.GetProperty("properties").GetProperty("name").GetString())
            .ToHashSet();
        Assert(labels.Contains("Dominica") && labels.Contains("Bolivia") && labels.Contains("Türkiye") && labels.Contains("Naoero"));
        resources.Dispose();
        Assert(!Directory.Exists(folder), "bundled resources should be removed when the workspace closes");
    }

    private static void TeachingResourceBrowser(Application app, TeacherWindow teacher)
    {
        var subjects = Find<ComboBox>(teacher, "SubjectChoice");
        var tools = Find<ComboBox>(teacher, "SubjectTools");
        var subjectChanges = 0;
        SourceWindow? source = null;

        WebView2CompositionControl Open(BoardSubject subject, int index, string title)
        {
            if (!Equals(subjects.SelectedItem, subject))
            {
                subjects.SelectedItem = subject;
                subjectChanges++;
            }
            tools.SelectedIndex = index;
            teacher.Dispatcher.BeginInvoke(() => Click(teacher, "UseSubjectToolButton"));
            Pump();
            source = app.Windows.OfType<SourceWindow>().Single();
            var slot = Find<Grid>(source, "BrowserSlot");
            var publish = Find<CheckBox>(source, "Publish");
            WaitFor(() => publish.IsEnabled && slot.Children.OfType<WebView2CompositionControl>().SingleOrDefault()
                ?.CoreWebView2?.DocumentTitle == title, () => $"could not open {title}. {Find<TextBlock>(source, "Hint").Text}");
            Assert(source.PublishedFrame(false) is null, "a bundled teaching resource opened on Program without permission");
            return slot.Children.OfType<WebView2CompositionControl>().Single();
        }

        static void CheckPage(WebView2CompositionControl browser, string javascript, string failure)
        {
            var execution = browser.CoreWebView2.ExecuteScriptAsync(javascript);
            WaitFor(() => execution.IsCompleted, () => failure);
            Assert(execution.GetAwaiter().GetResult() == "true", failure);
        }

        try
        {
            var map = Open(BoardSubject.Geography, 0, "World atlas");
            Task<string>? atlasReady = null;
            WaitFor(() =>
            {
                atlasReady ??= map.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#places option').length > 1000");
                if (!atlasReady.IsCompleted)
                    return false;
                if (atlasReady.GetAwaiter().GetResult() == "true")
                    return true;
                atlasReady = null;
                return false;
            }, () => "the offline atlas did not finish loading");
            CheckPage(map, """
                (() => {
                    document.querySelector('#place').value = 'Nauru';
                    document.querySelector('#find').requestSubmit();
                    const map = document.querySelector('#map');
                    return location.hash === '#map' && document.querySelector('#status').textContent === 'Naoero' &&
                        map.width > 100 && document.querySelectorAll('#places option').length > 1000;
                })()
                """, "the world map did not render or resolve the current name Naoero");
            var globe = Open(BoardSubject.Geography, 1, "World atlas");
            CheckPage(globe, """
                location.hash === '#globe' && document.querySelector('#globe').getAttribute('aria-pressed') === 'true' &&
                    document.querySelector('#title').textContent === 'World globe'
                """, "the globe did not open in orthographic mode");

            var pendulum = Open(BoardSubject.Physics, 0, "Simple pendulum");
            CheckPage(pendulum, """
                (() => {
                    const start = document.querySelector('#start');
                    start.click();
                    for (let time = 0; time < 3200; time += 40) tick(time);
                    start.click();
                    const measured = parseFloat(document.querySelector('#period').textContent);
                    const predicted = parseFloat(document.querySelector('#prediction').textContent);
                    return Math.abs(predicted - 2.006) < .005 && measured > predicted && measured < 2.025;
                })()
                """, "the pendulum period did not match the model");

            var quadrat = Open(BoardSubject.Biology, 0, "Quadrat sampling");
            CheckPage(quadrat, """
                (() => {
                    document.querySelector('#random').click();
                    document.querySelector('#record').click();
                    document.querySelector('#record').click();
                    return document.querySelector('#samples').textContent === '2' &&
                        document.querySelector('#density').textContent.endsWith('plants/m²');
                })()
                """, "the quadrat did not record both samples");

            var cylinder = Open(BoardSubject.Chemistry, 0, "Measuring cylinder");
            CheckPage(cylinder, """
                (() => {
                    document.querySelector('#reveal').click();
                    return document.querySelector('#volume').textContent === '53 mL';
                })()
                """, "the measuring cylinder did not reveal its correct reading");
        }
        finally
        {
            source?.Close();
            for (var i = 0; i < subjectChanges; i++)
                Click(teacher, "UndoButton");
        }
    }
}
