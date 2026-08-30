using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class DiagnosticsReportTests
{
    [Fact]
    public void KeepsOnlyConfiguredCapacity()
    {
        var report = new DiagnosticsReport(2);

        report.Add("one");
        report.Add("two");
        report.Add("three");

        var text = report.ToString();
        Assert.DoesNotContain("one", text);
        Assert.Contains("two", text);
        Assert.Contains("three", text);
    }

    [Fact]
    public void AddingAnEntryRaisesEntryAddedWithThatEntry()
    {
        var report = new DiagnosticsReport();
        var added = new List<string>();
        report.EntryAdded += added.Add;

        report.Add("hello");

        var entry = Assert.Single(added);
        Assert.Contains("hello", entry);
        Assert.Contains(entry, report.ToString());
    }

    [Fact]
    public void AddingAnEntryRaisesChanged()
    {
        var report = new DiagnosticsReport();
        var changes = 0;
        report.Changed += (_, _) => changes++;

        report.Add("one");
        report.Add("two");

        Assert.Equal(2, changes);
    }
}

