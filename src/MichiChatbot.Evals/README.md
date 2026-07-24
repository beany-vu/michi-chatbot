# MichiChatbot.Evals

## Where this came from

`plan.md`'s learning track for phase 3 (evals + observability) says explicitly:

> Build a small eval harness (**xUnit or a console runner**): golden set from phase 2, LLM-as-judge
> scoring (qwen-max judging qwen-plus answers), run on every prompt/persona change like a
> regression suite.

The plan names two valid ways to build it. This project is the **console-runner** branch of that
choice — not something invented outside the plan, but the specific option picked between the two
it names.

## Why a separate console project, not `tests/MichiChatbot.Tests`

Both are real xUnit-vs-console tradeoffs, not arbitrary:

1. **What the output means is different.** `tests/MichiChatbot.Tests` (`SiteIsolationTests`,
   `BookingIdempotencyTests`, `AvailabilityCheckerTests`, …) makes deterministic pass/fail
   assertions — a red test means something is broken, full stop, and CI should block on it. An eval
   result isn't pass/fail like that: "93% grounded, mean helpfulness 4.0/5" is a **measurement** to
   read and compare over time (before/after a prompt change), not an assertion that fails a build.
   Forcing it into `[Fact]`/`Assert.True` would misrepresent what the number means.
2. **It calls the real, live LLM** — non-deterministic, ~15-25 seconds per turn, occasionally
   errors. Mixing that into the same `dotnet test` run as the genuinely deterministic isolation/
   idempotency tests would make the whole suite slow and flaky, and a real infra hiccup (Ollama not
   running, network blip) would look identical to a real regression in the test runner's output.
3. **The primary artifact is a report, not a TRX file** — `Program.cs` writes a markdown file
   (`eval-report-*.md`) with full transcripts and judge reasoning, meant to be read by a person
   comparing runs. That's a console-app concern, not a test-runner one.

Run it with `dotnet run` from this directory (needs the `api` container and Ollama both up) —
see `golden-set.json` / `redteam-set.json` for what it actually tests, and `Judge.cs` for how
scoring works.
