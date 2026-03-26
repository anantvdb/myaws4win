using FluentAssertions;
using MyAws.Core.Services;

namespace MyAws.Tests.Services;

public class SshServiceTests
{
    private const string TestUser = "ec2-user";
    private const string TestKnownHosts = "/home/user/.ssh/amazon-vms";
    private const string TestHost = "ec2-1-2-3-4.compute.amazonaws.com";

    private SshService CreateSut(string? user = null, string? knownHosts = null) =>
        new(user ?? TestUser, knownHosts ?? TestKnownHosts);

    // ── BuildTerminalArgs ─────────────────────────────────────

    [Fact]
    public void BuildTerminalArgs_ContainsUserAndHost()
    {
        var sut = CreateSut();
        var args = sut.BuildTerminalArgs(TestHost);

        args.Should().Contain($"{TestUser}@{TestHost}");
    }

    [Fact]
    public void BuildTerminalArgs_ContainsKnownHostsPath()
    {
        var sut = CreateSut();
        var args = sut.BuildTerminalArgs(TestHost);

        args.Should().Contain(TestKnownHosts);
        args.Should().Contain("UserKnownHostsFile");
    }

    [Fact]
    public void BuildTerminalArgs_SetsStrictHostCheckingAcceptNew()
    {
        var sut = CreateSut();
        var args = sut.BuildTerminalArgs(TestHost);

        args.Should().Contain("StrictHostKeyChecking=accept-new");
    }

    [Fact]
    public void BuildTerminalArgs_QuotesKnownHostsPath_WithSpaces()
    {
        var pathWithSpaces = @"C:\Users\John Doe\.ssh\amazon-vms";
        var sut = CreateSut(knownHosts: pathWithSpaces);
        var args = sut.BuildTerminalArgs(TestHost);

        args.Should().Contain($"\"{pathWithSpaces}\"");
    }

    // ── BuildRemoteCommandArgs ────────────────────────────────

    [Fact]
    public void BuildRemoteCommandArgs_ContainsUserAndHost()
    {
        var sut = CreateSut();
        var args = sut.BuildRemoteCommandArgs(TestHost, "update");

        args.Should().Contain($"{TestUser}@{TestHost}");
    }

    [Fact]
    public void BuildRemoteCommandArgs_ContainsCommand()
    {
        var sut = CreateSut();
        var args = sut.BuildRemoteCommandArgs(TestHost, "fullupdate");

        args.Should().Contain("fullupdate");
        args.Should().Contain("bash -icl");
    }

    [Fact]
    public void BuildRemoteCommandArgs_UsesInteractiveTtyFlag()
    {
        var sut = CreateSut();
        var args = sut.BuildRemoteCommandArgs(TestHost, "update");

        // -t flag forces pseudo-terminal allocation for interactive commands
        args.Should().MatchRegex(@"-[^\s]*t[^\s]*\s|^-t ");
    }

    [Fact]
    public void BuildRemoteCommandArgs_DiffersFromTerminalArgs()
    {
        var sut = CreateSut();
        var terminalArgs = sut.BuildTerminalArgs(TestHost);
        var commandArgs = sut.BuildRemoteCommandArgs(TestHost, "update");

        // Remote command args include the command itself; terminal args do not
        commandArgs.Should().NotBe(terminalArgs);
        commandArgs.Length.Should().BeGreaterThan(terminalArgs.Length);
    }
}
