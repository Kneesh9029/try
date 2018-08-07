﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Clockwise;
using FluentAssertions;
using FluentAssertions.Extensions;
using Microsoft.CodeAnalysis;
using Pocket;
using WorkspaceServer.Models.Execution;
using WorkspaceServer.WorkspaceFeatures;
using Xunit;
using Xunit.Abstractions;
using static Pocket.Logger<WorkspaceServer.Tests.WorkspaceServerTests>;
using Workspace = WorkspaceServer.Models.Execution.Workspace;

namespace WorkspaceServer.Tests
{
    public abstract class WorkspaceServerTests : WorkspaceServerTestsCore
    {
        protected abstract Workspace CreateWorkspaceWithMainContaining(string text);

        [Fact]
        public async Task Diagnostic_logs_do_not_show_up_in_captured_console_output()
        {
            using (LogEvents.Subscribe(e => Console.WriteLine(e.ToLogString())))
            {
                var server = await GetRunner();

                var result = await server.Run(CreateWorkspaceWithMainContaining("Console.WriteLine(\"hi!\");"));

                result.Output
                      .Should()
                      .BeEquivalentTo(
                          new[] { "hi!", "" },
                          options => options.WithStrictOrdering());
            }
        }

        protected WorkspaceServerTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task Response_indicates_when_compile_is_successful_and_signature_is_like_a_console_app()
        {
            var workspace = Workspace.FromSource(@"
using System;

public static class Hello
{
    public static void Main()
    {
    }
}
", workspaceType: GetWorkspaceType());

            var server = await GetRunner();

            var result = await server.Run(workspace);

            Log.Trace(result.ToString());

            result.ShouldSucceedWithNoOutput();
        }

        [Fact]
        public async Task Response_shows_program_output_when_compile_is_successful_and_signature_is_like_a_console_app()
        {
            var output = nameof(Response_shows_program_output_when_compile_is_successful_and_signature_is_like_a_console_app);

            var workspace = Workspace.FromSource($@"
using System;

public static class Hello
{{
    public static void Main()
    {{
        Console.WriteLine(""{output}"");
    }}
}}", workspaceType: GetWorkspaceType());

            var server = await GetRunner();

            var result = await server.Run(workspace);

            result.ShouldSucceedWithOutput(output);
        }

        [Fact]
        public async Task Response_shows_program_output_when_compile_is_successful_and_signature_is_a_fragment_containing_console_output()
        {
            var request = CreateWorkspaceWithMainContaining(@"
var person = new { Name = ""Jeff"", Age = 20 };
var s = $""{person.Name} is {person.Age} year(s) old"";
Console.Write(s);");

            var server = await GetRunner();

            var result = await server.Run(request);

            result.ShouldSucceedWithOutput("Jeff is 20 year(s) old");
        }

        [Fact]
        public async Task When_compile_is_unsuccessful_then_no_exceptions_are_shown()
        {
            var request = CreateWorkspaceWithMainContaining(@"
Console.WriteLine(banana);");

            var server = await GetRunner();

            var result = await server.Run(request);
            result.Succeeded.Should().BeFalse();
            result.Output
                  .ShouldMatch(
                      "*(2,19): error CS0103: The name \'banana\' does not exist in the current context");
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task Multi_line_console_output_is_captured_correctly()
        {
            var request = CreateWorkspaceWithMainContaining(@"
Console.WriteLine(1);
Console.WriteLine(2);
Console.WriteLine(3);
Console.WriteLine(4);");

            var server = await GetRunner();

            var result = await server.Run(request);

            result.ShouldSucceedWithOutput("1", "2", "3", "4", "");
        }

        [Fact]
        public async Task Whitespace_is_preserved_in_multi_line_output()
        {
            var request = CreateWorkspaceWithMainContaining(@"
Console.WriteLine();
Console.WriteLine(1);
Console.WriteLine();
Console.WriteLine();
Console.WriteLine(2);");

            var server = await GetRunner();

            var result = await server.Run(request);

            result.ShouldSucceedWithOutput("", "1", "", "", "2", "");
        }

        [Fact]
        public async Task Multi_line_console_output_is_captured_correctly_when_an_exception_is_thrown()
        {
            var request = CreateWorkspaceWithMainContaining(@"
Console.WriteLine(1);
Console.WriteLine(2);
throw new Exception(""oops!"");
Console.WriteLine(3);
Console.WriteLine(4);");

            var server = await GetRunner();

            var timeBudget = new TimeBudget(30.Seconds());

            var result = await server.Run(request, timeBudget);

            result.ShouldSucceedWithExceptionContaining(
                "System.Exception: oops!",
                output: new[] { "1", "2" });
        }

        [Fact]
        public async Task When_the_users_code_throws_on_first_line_then_it_is_returned_as_an_exception_property()
        {
            var request = CreateWorkspaceWithMainContaining(@"throw new Exception(""oops!"");");

            var server = await GetRunner();

            var result = await server.Run(request);

            result.ShouldSucceedWithExceptionContaining("System.Exception: oops!");
        }

        [Fact]
        public async Task When_the_users_code_throws_on_subsequent_line_then_it_is_returned_as_an_exception_property()
        {
            var request = CreateWorkspaceWithMainContaining(@"
throw new Exception(""oops!"");");

            var server = await GetRunner();

            var result = await server.Run(request);

            result.ShouldSucceedWithExceptionContaining("System.Exception: oops!");
        }

        [Fact]
        public async Task When_a_public_void_Main_with_no_parameters_is_present_it_is_invoked()
        {
            var workspace = Workspace.FromSource(@"
using System;

public static class Hello
{
    public static void Main()
    {
        Console.WriteLine(""Hello there!"");
    }
}", workspaceType: GetWorkspaceType());

            var server = await GetRunner();

            var result = await server.Run(workspace);

            result.ShouldSucceedWithOutput("Hello there!");
        }

        [Fact]
        public async Task When_a_public_void_Main_with_parameters_is_present_it_is_invoked()
        {
            var workspace = Workspace.FromSource(@"
using System;

public static class Hello
{
    public static void Main(params string[] args)
    {
        Console.WriteLine(""Hello there!"");
    }
}", workspaceType: GetWorkspaceType());
            
            var server = await GetRunner();

            var result = await server.Run(workspace);

            result.ShouldSucceedWithOutput("Hello there!");
        }

        [Fact]
        public async Task When_an_internal_void_Main_with_no_parameters_is_present_it_is_invoked()
        {
            var workspace = Workspace.FromSource(@"
using System;

public static class Hello
{
    static void Main()
    {
        Console.WriteLine(""Hello there!"");
    }
}", workspaceType: GetWorkspaceType());

            var server = await GetRunner();

            var result = await server.Run(workspace);

            Log.Trace(result.ToString());

            result.ShouldSucceedWithOutput("Hello there!");
        }

        [Fact]
        public async Task When_an_internal_void_Main_with_parameters_is_present_it_is_invoked()
        {
            var workspace = Workspace.FromSource(@"
using System;

public static class Hello
{
    static void Main(string[] args)
    {
        Console.WriteLine(""Hello there!"");
    }
}", workspaceType: GetWorkspaceType());

            var server = await GetRunner();

            var result = await server.Run(workspace);

            result.ShouldSucceedWithOutput("Hello there!");
        }

        [Fact]
        public async Task Response_shows_warnings_with_successful_compilation()
        {
            var output = nameof(Response_shows_warnings_with_successful_compilation);

            var workspace = CreateWorkspaceWithMainContaining($@"
using System;
using System;
public static class Hello
{{
    public static void Main()
    {{
        Console.WriteLine(""{output}"");
    }}
}}");
            var server = await GetRunner();

            var result = await server.Run(workspace);

            var diagnostics = result.GetFeature<Diagnostics>();

            diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Warning);
        }

        [Fact]
        public async Task Response_shows_warnings_when_compilation_fails()
        {
            var output = nameof(Response_shows_warnings_when_compilation_fails);

            var workspace = CreateWorkspaceWithMainContaining($@"
using System;
using System;
public static class Hello
{{
    public static void Main()
    {{
        Console.WriteLine(""{output}"")
    }}
}}");
            var server = await GetRunner();

            var result = await server.Run(workspace);

            var diagnostics = result.GetFeature<Diagnostics>();

            diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Warning);
        }

        protected abstract string GetWorkspaceType();
    }
}
