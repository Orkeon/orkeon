using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// GAP-38, decision 5 — the host's <c>RateLimiting</c> holds at the entrance of every provider,
/// once: the provider <c>orkeon run</c> hands a script is the host's own, limited where the host
/// built it. The runner used to wrap it in a limiter of its own for the scripts alone; kept on top
/// of the entrance, every <c>ctx.llm</c> call would have taken two leases.
/// <para>In the CLI collection: the run reads the process-global console.</para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ScriptedLlmLeaseTests
{
    /// <summary>An <see cref="ILlmRateLimiter"/> that grants every request and counts them.</summary>
    private sealed class CountingRateLimiter : ILlmRateLimiter
    {
        private int _acquired;

        public int Acquired => Volatile.Read(ref _acquired);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The lease (no-op Dispose) is owned by the returned RateLimitAcquisition; whoever acquired it disposes it.")]
        public Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acquired);
            return Task.FromResult(RateLimitAcquisition.Acquired(new NoLease()));
        }

        private sealed class NoLease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public async Task A_ctx_llm_complete_takes_one_lease_not_two()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ask.ork.ts", """
            /// <reference orkeon-script="1.0" />
            const asker = agentBuilder()
                .name("asker").role("Asker").goal("Ask once")
                .body(async (input, ctx) => await ctx.llm.complete("ping"))
                .build();
            const res = await crewBuilder().name("one-call").goal("Ask once").withAgent(asker).build().run();
            var result = res.output;
            """);
        using var console = new TestConsole();
        var limiter = new CountingRateLimiter();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            ConfigureTestServices = (_, services) => services.AddSingleton<ILlmRateLimiter>(limiter),
        });

        Assert.True(exit == Program.ExitOk, console.Stderr);
        Assert.Equal(1, limiter.Acquired);
    }
}
