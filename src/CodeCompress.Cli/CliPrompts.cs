namespace CodeCompress.Cli;

internal sealed record CliPrompt(string Name, string Description, string Text);

internal static class CliPrompts
{
    internal static readonly IReadOnlyList<CliPrompt> All =
    [
        new CliPrompt(
            "explore_codebase",
            "Codebase exploration workflow: index_project → project_outline → search_symbols → get_symbol",
            """
            Workflow to explore an unfamiliar codebase efficiently:

            1. index_project — Build the symbol database. MUST run first; ~1-120s initially, <1s incremental.
            2. project_outline — Compressed overview of all symbols grouped by file, kind, or directory (~50–2,000 tokens). Saves 90%+ tokens vs reading raw files. Use pathFilter to scope to a subdirectory.
            3. search_symbols — Find specific symbols by name or type via FTS5 (~100–500 tokens). Supports camelCase/PascalCase splitting, prefix*, fuzzy=true for typos.
            4. get_symbol — Retrieve the exact source code of a symbol by name (~50–500 tokens). Loads only the symbol, not the whole file.
            5. expand_symbol — For large classes, retrieve only one method without loading the parent (~50–200 tokens, ~60% fewer than get_symbol on the class).

            PREFER these tools over reading raw files — they save 80–90% tokens.
            After get_symbol: use find_references to trace all usages, or search_symbols for related symbols.
            """),

        new CliPrompt(
            "find_impact",
            "Impact analysis workflow: index_project → blast_radius → find_references → dependency_graph",
            """
            Workflow to analyze the impact of a change before making it:

            1. index_project — Ensure the index reflects the current codebase state.
            2. blast_radius — Reverse BFS: find all files that depend on a given file or symbol (~50–500 tokens). Use filePath for file-level, symbolName for symbol-level. Answers "what breaks if I change X?"
            3. find_references — Find exact line-by-line locations where a symbol is referenced (~50–500 tokens). Use to pinpoint call sites before refactoring.
            4. dependency_graph — Visualize the full import/dependency network (~100–2,000 tokens). Use for broader structural understanding.

            Prefer blast_radius over dependency_graph for a direct "what depends on X" answer (~50–500 tokens vs 100–2,000).
            Prefer find_references over blast_radius when you need exact line numbers, not just file counts.
            """),

        new CliPrompt(
            "review_changes",
            "Change review workflow: snapshot_create → [make changes] → index_project → changes_since",
            """
            Workflow to track and review symbol-level changes across a coding session:

            1. snapshot_create — Set a named baseline before making code changes (~10 tokens).
            2. [Make your code changes]
            3. index_project — Re-index to pick up the changes (incremental, usually <1s for small edits).
            4. changes_since — Show new, modified, and deleted files with symbol-level diffs (~100–2,000 tokens). Displays +added / ~modified / -removed symbol signatures per file.

            The changes_since response shows exactly which symbols were added, changed signature, or removed.
            Next: use get_symbol to view the current source code of any changed symbol.
            Prefer changes_since over git diff when you need symbol-granularity change tracking.
            """),

        new CliPrompt(
            "debug_symbol",
            "Targeted symbol debugging: search_symbols → get_hot_path → get_symbol → find_references",
            """
            Workflow to debug or deeply understand a specific symbol efficiently:

            1. search_symbols — Locate the symbol by name or topic via FTS5 (~100–500 tokens). Supports camelCase splitting, prefix*, and fuzzy matching.
            2. get_hot_path — Return only the lines within the symbol that contain specific identifiers plus context (~50–100 tokens vs 500–2,000 for the full body). Use when tracing a variable or condition within a large function. Identifiers matched as whole words.
            3. get_symbol — Retrieve the full source code if the hot path is insufficient (~50–500 tokens). Use force=true to bypass the 16KB size guard.
            4. find_references — Find all call sites across the codebase to understand usage patterns (~50–500 tokens).

            Prefer get_hot_path over get_symbol when tracing a specific variable — 10–40x fewer tokens.
            Prefer expand_symbol over get_symbol when the target is a single method in a large class (~60% fewer tokens).
            """),
    ];
}
