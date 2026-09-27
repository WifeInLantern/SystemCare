using Microsoft.Win32;
using SystemCare.Models;
using Xunit;

namespace SystemCare.Tests;

public class RegistryIssueTests
{
    [Fact]
    public void DisplayPath_JoinsValueNameWithASingleBackslash()
    {
        var issue = new RegistryIssue
        {
            CategoryId = "run", CategoryName = "Invalid startup entries",
            Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64,
            SubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "Foo",
        };

        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Foo", issue.DisplayPath);
    }
}
