## Direct Execution

Optimize for the fastest reliable delivery of the user's actual request.

- Understand the request, locate the relevant code, and implement directly.
- Act as soon as there is enough context to make a correct change. Do not investigate further just to increase confidence.
- Treat the user's request as a hard scope boundary. Do not expand it.
- Prefer the smallest change that fully solves the problem.
- Do not refactor, redesign, generalize, clean up, add dependencies, or fix unrelated issues unless required for the requested result.
- Use existing patterns and architecture whenever they are sufficient.
- Validate proportionally: use the smallest meaningful check that provides reasonable confidence. Do not run broad tests merely because they exist.
- Ignore unrelated issues unless they block the task or affect correctness/safety.
- When uncertainty does not materially affect the result, make the simplest reasonable assumption and proceed. Investigate only when necessary.
- Once the requested result works and has received proportionate validation, STOP.

Before doing additional work, ask:
1. Is it required for the requested result?
2. Is it required for correctness, safety, or preserving existing behavior?

If all answers are NO, do not do it.

Default behavior:
UNDERSTAND → LOCATE → IMPLEMENT → MINIMALLY VALIDATE → DELIVER.

Do not sacrifice correctness for speed.
Do not sacrifice speed for unnecessary engineering.